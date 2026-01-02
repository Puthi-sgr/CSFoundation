using AsyncAwait.API;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsyncAwait.Service
{
    /***
     * Contract:
     *      Goal: Dispatch payslip in batch to employees via telegram
     *      Constraint: 
     *          - External API (Telegram) is I/O and sometimes is slow
     *          - External API (Telegram) can hit rate limits
     *          - Some sends randomly fails but can be retried
     *          - Some sends permanently fail (invalid chat ID, user blocked bot)
     *      Requirments:
     *          1. Send(Best-effort mode): attempt to send payslip to all employees, log failures
     *          2. Send(Strict mode): attempt to send payslip to all employees, if any failure, everything fails
     *      Checklist:
     *          - Limits concurrency (e.g., max 10 sends in-flight)
                - Retries transient errors up to 3 times (exponential backoff)
                - Handles rate limiting (wait RetryAfter)
                - Supports two modes: BestEffortContinue vs StrictStopOnFailure
                - Produces a report with per-employee outcome
       Concept:
            External api: The service you communicate with from outside of your system
            Rate limit: A restriction made by external service to limit the amount of request you made. For safety purposes
            Transient error: A temporary error that can be resolved by retrying the operation  
            Permanent error: An error that cannot be resolved by retrying the operation
            -> Your system can over load external service by continuously sending request to them

                Skeleton analysis:
                - Enum mode
                - Enum dispatch status(sent, failedTransient, failedPermanent, ratelimited, cancelled)? Comes from the potential error types and the outcomes after you dispatch
                - DispatchResultItem (Part)
                - DispatchResult (Whole)
       Design:
            Mode:
                BestEffortContinue:
                    Prevention:
                        controlled concurrency using SemaphoreSlim (max 10 sends in-flight)
                    Reaction:
                        Error: 
                          RatelimitError: wait RetryAfter, then retry based on the retry policy
                          TransientError:
                                - Retry up to 3 times with exponential backoff 200ms, 400ms, 800ms and then log failure
                                - Report failure employee and report success employees
                          PermanentError (telegram block, wrong creds):
                                - Log failure and report failure employee 
                StrictStopOnFailure
                        Error: 
                          RatelimitError: wait RetryAfter, then retry based on the retry policy
                          TransientError:
                                - Retry up to 3 times with exponential backoff 200ms, 400ms, 800ms and then fail the entire operation
                                - Report failure employee and stop processing further employees
                          PermanentError (telegram block, wrong creds):
                                - Fail the entire operation immediately and report failure employee
     *
     */
    public enum PaySlipDispatchMode
    {
        BestEffortContinue,
        StrictStopOnFailure
    }

    public enum DispatchStatus
    {
        Sent,
        FailedTransient,
        FailedPermanent,
        RateLimited,
        Cancelled
    }

    public record DispatchItemResult
    (
        int EmployeeId,
        DispatchStatus Status,
        int Attempts,
        string? ErrorMessage
    );

    public record DispatchResult
    (
        int Total,
        int Sent,
        int Failed,
        IReadOnlyCollection<DispatchItemResult> ItemResults //Owned this collection no body can modify it
    );
    internal class PayslipService
    {
        private readonly Telegram _telegramClient;
        
        public PayslipService(Telegram telegramClient)
        {
            _telegramClient = telegramClient;
        }

        public async Task<DispatchResult> SendPayslipAsync(
            List<int> employeeId,
            PaySlipDispatchMode mode,
            int maxConcurrency = 10,
            CancellationToken cancellationToken = default
            )
        {
            var resultBag = new ConcurrentBag<DispatchItemResult>(); //Accumulator
            var gate = new SemaphoreSlim(maxConcurrency);

            // Used only for StrictStopOnFailure (cancel rest when first failure happens)
            using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            //The task return is has not run yet. Its just a shell. Once something touch it, it will run
            var tasks = employeeId.Select(async empId =>
            {
                await gate.WaitAsync(stopCts.Token);

                try
                {
                    DispatchItemResult r = await SendOneWithPolicyAsync(empId, mode, stopCts.Token);
                    resultBag.Add(r);//This is a guard section. Only one can execute at a time because it is using concurrent bag.

                    if (mode == PaySlipDispatchMode.StrictStopOnFailure && r.Status != DispatchStatus.Sent)
                    {
                        stopCts.Cancel(); //Cancellation is a cooperative form of failure. it does not stop the thread abruptly
                    }
                }
                catch (OperationCanceledException)
                {
                    resultBag.Add(new DispatchItemResult(
                        EmployeeId: empId,
                        Status: DispatchStatus.Cancelled,
                        Attempts: 0,
                        ErrorMessage: "Operation cancelled"
                     ));
                }
                finally
                {
                    gate.Release();
                }
            });

            try { await Task.WhenAll(tasks); }catch (OperationCanceledException)
            {
                // Swallow, as individual tasks have already recorded cancellation results
            }

            int sent = resultBag.Count(x => x.Status == DispatchStatus.Sent);
            int failed = resultBag.Count(x => x.Status != DispatchStatus.Sent);

            return new DispatchResult(
                Total: employeeId.Count,
                Sent: sent,
                Failed: failed,
                ItemResults: resultBag.ToArray() //ToArray to make it read-only
             );
        }

        private async Task<DispatchItemResult> SendOneWithPolicyAsync(
            int employeeId,
            PaySlipDispatchMode mode,
            CancellationToken cancellationToken
        )
        {
            const int maxAttempts = 3;
            int attempt = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                try
                {
                    await _telegramClient.SendAsync(employeeId, cancellationToken);
                    return new DispatchItemResult(
                        EmployeeId: employeeId,
                        Status: DispatchStatus.Sent,
                        Attempts: attempt,
                        ErrorMessage: null
                     );
                }
                catch (AsyncAwait.Exception.RateLimitException rlEx)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return new DispatchItemResult(
                            EmployeeId: employeeId,
                            Status: DispatchStatus.Cancelled,
                            Attempts: attempt,
                            ErrorMessage: "Operation cancelled"
                         );
                    }

                    if (attempt > maxAttempts)
                    {
                        return new(
                            EmployeeId: employeeId,
                            Status: DispatchStatus.FailedTransient,
                            Attempts: attempt - 1,
                            ErrorMessage: "Exceeded max retries due to rate limiting"
                         );
                    }

                    try
                    {
                        await Task.Delay(rlEx.RetryAfter);
                    }
                    catch (OperationCanceledException)
                    {
                        return new DispatchItemResult(
                            EmployeeId: employeeId,
                            Status: DispatchStatus.Cancelled,
                            Attempts: attempt,
                            ErrorMessage: "Operation cancelled during rate limit wait"
                         );
                    }
                    //Loop back
                }
                catch (AsyncAwait.Exception.PermanentSendException pEx)
                {
                    return new DispatchItemResult(
                        EmployeeId: employeeId,
                        Status: DispatchStatus.FailedPermanent,
                        Attempts: attempt,
                        ErrorMessage: pEx.Message
                     );
                }
                catch (OperationCanceledException)
                {
                    return new DispatchItemResult(
                        EmployeeId: employeeId,
                        Status: DispatchStatus.Cancelled,
                        Attempts: attempt,
                        ErrorMessage: "Operation cancelled"
                     );
                }
                catch (AsyncAwait.Exception.TransientSendException tEx)
                {
                    if (cancellationToken.IsCancellationRequested) return new DispatchItemResult(
                        EmployeeId: employeeId,
                        Status: DispatchStatus.Cancelled,
                        Attempts: attempt,
                        ErrorMessage: "Operation cancelled"
                     );

                    if (attempt >= maxAttempts) return new DispatchItemResult(
                        EmployeeId: employeeId,
                        Status: DispatchStatus.FailedTransient,
                        Attempts: attempt,
                        ErrorMessage: "Exceeded max retries due to transient errors"
                     );

                    var backOffDelay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                    try
                    {
                        await Task.Delay(backOffDelay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return new DispatchItemResult(
                            EmployeeId: employeeId,
                            Status: DispatchStatus.Cancelled,
                            Attempts: attempt,
                            ErrorMessage: "Operation cancelled during backoff wait"
                         );
                    }
                }
            }

        }

    }
}
