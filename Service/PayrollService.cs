using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsyncAwait.API;
using AsyncAwait.Repository;


/*
    Contract:
        goal:
            - Build payslip can quickly output the DRAFT on partial data (Payroll preview)
            - Build payslip must output the FULL payslip with all data (Official payslip)
        data sources constraint:
            - GetBaseSalaryAsync(employeeId) Critical
            - GetAttendanceAsync(employeeId, period) Critical
            - GetAllowancesAsync(employeeId, period) Optional (can default to 0 in preview)
            - GetDeductionsAsync(employeeId, period) Optional (can default to 0 in preview)
            - GetTaxRateAsync(employeeId) Optional in preview, Critical in finalize (company policy)
            - GetExchangeRateAsync(currency) Optional (if they pay in multiple currency—use fallback cached rate)
        constraint:
            - External Deps like tax can sometimes fail
            - FULL payslip must have all data

    Design:
        -  A mode to switch between preview and full build. Why? Same ops but slightly different behavior
        -  Use result object pattern to return partial data with warning, full data with success, and complete failure with meaning full message
        -  Use Safe fetch wrapper to handle exception by value instead of exception by throw -> Can still return partial data with meaningful warnings

 */
public record PayslipSummary(
    int EmployeeId,
    decimal BaseSalary,
    int PaidHours,
    int OvertimeHours,
    decimal Allowances,
    decimal Deductions,
    decimal GrossPay,
    decimal TaxAmount,
    decimal NetPay
);
/***
 * The reasoning behind this is when you know an operation can partially fail
 * -> Instead of writing duplication logic or if else or try catch everywhere
 * -> We can use ENUM is a knob where we can switch the mode of the one operation
 */
public enum PayRollBuildMode
{
    PreviewBestEffort,
    FullStrict
}

/*
 * The reason we want to use the result object pattern is to give out meaning error message and handle partial failure with meaningful warning
 * This allow room for partial success to be return
 * **/
public record BuildWarning(string Code, string Source, string Message);
public record PayslipBuildResult(
    bool Success,
    PayslipSummary? Payslip,
    IReadOnlyList<BuildWarning> Warnings, //List of warning written down by my operation
    string? ErrorMessage //If completely failed, what is the error message
    );

/*
 *  
 */
public record FetchResult<T>(bool Ok, T? Value, Exception? Error);
public static class SafeFetch
{
    public static async Task<FetchResult<T>> TryAsync<T>(Func<Task<T>> op)
    {
        try
        {
            return new FetchResult<T>(true, await op(), null);
        }
        catch (Exception ex)
        {
            return new FetchResult<T>(false, default, ex); //When error still return default value, not quite sure
        }
    }
}


namespace AsyncAwait.Service
{
    internal class PayrollService
    {
        private readonly Tax _taxService;
        private readonly HRRepo _hrRepo;

        public PayrollService()
        {
            _taxService = new Tax();
            _hrRepo = new HRRepo();
        }

        /*
            Contract:
                goal: to build payslip summary for an employee that support
                    - FinalStrict mode: all data must be present
                    - PreviewBestEffort mode: missing optional data can be defaulted to 0 and return partial payslip with warning

                constraint: 
                    - every data fetch is IO bound is synchronous ops can be very slow
                    - BaseSalary, Attendance are critical -> must be present in both mode
                    - Tax API, Deduction, Allowance can sometimes fail (Tolerated in preview mode)
                    - Tax, Allowance are optional -> default to 0 in preview
                   
                input: employeeId
            Design:
                - Use the fan-out/fan-in pattern for sort of parallel data fetching. Once the declared variable is calling the task. The timer or IO will start immediatly
        .       - Hence the when all will finish only if the slowest task is finished
                - Critical failure return payslipresult with complete failure message

                - Preview mode 
                    - Partial failure: return payslipresult with partial data and warning message if any optional data is missing
                    - No failure: return payslipresult with full data
                - Full mode 
                    - If no fail Return full data
                    othewise return complete failure message

            Algorithm:
                Pre-condition:
                    employeeId is valid
                Operation:
                    Fan-out:
                        Fetch BaseSalary from HRRepo
                        Fetch Attendance (paid hours, overtime hours) from HRRepo
                        Fetch Allowances from HRRepo
                        Fetch Deductions from HRRepo
                        Fetch TaxRate from Tax API
                    Fan-in:
                        Whenall to await all fetch tasks
                    Pre-process:
                        BaseSalary, Attendance must be present
                    Retrieve optional data;
                        if tax, allowance, deduction fetch failed in preview mode, default to 0 and log warning
                    Calculate:
                        GrossPay = BaseSalary + Allowances - Deductions
                        TaxAmount = GrossPay * TaxRate
                        NetPay = GrossPay - TaxAmount
                Post-condition:
                    - built payslip with all filled records for final mode
                    - partial payslip with warnings for preview mode if any optional data is missing
                    - partial payslip with no warning for preview mode if all data is present
         */
        public async Task<PayslipBuildResult> BuildPayslipAsync(int employeeId, PayRollBuildMode mode)
        {
            //Start all task fan-out
            Task<FetchResult<decimal>> baseSalaryTask = SafeFetch.TryAsync(() => _hrRepo.GetBaseSalaryAsync(employeeId));
            Task<FetchResult<(int paidHours, int overtimeHours)>> attendanceTask = SafeFetch.TryAsync(() => _hrRepo.GetAttendanceAsync(employeeId));
            Task<FetchResult<decimal>> allowancesTask = SafeFetch.TryAsync(() => _hrRepo.GetAllowancesAsync(employeeId));
            Task<FetchResult<decimal>> deductionsTask = SafeFetch.TryAsync(() => _hrRepo.GetDeductionsAsync(employeeId));
            Task<FetchResult<decimal>> taxAmountTask = SafeFetch.TryAsync(() => _taxService.TaxRate(employeeId)); //deferred start
 
            //Await all task results

            await Task.WhenAll(baseSalaryTask, attendanceTask, allowancesTask, deductionsTask);
            //When this part fininsh we know that the promise has been returned;

            FetchResult<decimal> baseSalary = baseSalaryTask.Result;
            FetchResult<(int paidHours, int overtimeHours)> attendance = attendanceTask.Result;
            FetchResult<decimal> allowances = allowancesTask.Result;
            FetchResult<decimal> deductions = deductionsTask.Result;
            FetchResult<decimal> taxRate = taxAmountTask.Result;
            //Extract data from fetch results

            //Critical error
            if(baseSalary.Ok.Equals(false)) 
               return new PayslipBuildResult(
                   Success: false,
                    Payslip: null,
                    Warnings: Array.Empty<BuildWarning>(),
                    ErrorMessage: $"Failed to fetch base salary for employee {employeeId}: {baseSalary.Error?.Message}"
                );
            if(attendance.Ok.Equals(false)) 
                return new PayslipBuildResult(
                   Success: false,
                    Payslip: null,
                    Warnings: Array.Empty<BuildWarning>(),
                    ErrorMessage: $"Failed to fetch attendance for employee {employeeId}: {attendance.Error?.Message}"
                );

            var warnings = new List<BuildWarning>();
            //Mode behavior
            decimal taxRateValue = 0m;
            if (taxRate.Ok) taxRateValue = taxRate.Value!;
            else
            {
                if(mode == PayRollBuildMode.FullStrict)
                {
                    return new PayslipBuildResult(
                        Success: false,
                        Payslip: null,
                        Warnings: Array.Empty<BuildWarning>(),
                        ErrorMessage: $"Failed to fetch tax rate for employee {employeeId}: {taxRate.Error?.Message}"
                    );
                }
                warnings.Add(new BuildWarning(
                    Code: "TAX_FETCH_FAILED",
                    Source: "TaxService",
                    Message: $"Failed to fetch tax rate for employee {employeeId}, defaulting to 0: {taxRate.Error?.Message}"
                ));
            }

            decimal allowancesValue = 0m;
            if(allowances.Ok) allowancesValue = allowances.Value!;
            else
            {
                if(mode == PayRollBuildMode.FullStrict)
                {
                    return new PayslipBuildResult(
                        Success: false,
                        Payslip: null,
                        Warnings: Array.Empty<BuildWarning>(),
                        ErrorMessage: $"Failed to fetch allowances for employee {employeeId}: {allowances.Error?.Message}"
                    );
                }
                warnings.Add(new BuildWarning(
                    Code: "ALLOWANCE_FETCH_FAILED",
                    Source: "HRRepo",
                    Message: $"Failed to fetch allowances for employee {employeeId}, defaulting to 0: {allowances.Error?.Message}"
                ));
            }

            decimal deductionsValue = 0m;

            if(deductions.Ok) deductionsValue = deductions.Value!;
            else
            {
                if(mode == PayRollBuildMode.FullStrict)
                {
                    return new PayslipBuildResult(
                        Success: false,
                        Payslip: null,
                        Warnings: Array.Empty<BuildWarning>(),
                        ErrorMessage: $"Failed to fetch deductions for employee {employeeId}: {deductions.Error?.Message}"
                    );
                }
                warnings.Add(new BuildWarning(
                    Code: "DEDUCTION_FETCH_FAILED",
                    Source: "HRRepo",
                    Message: $"Failed to fetch deductions for employee {employeeId}, defaulting to 0: {deductions.Error?.Message}"
                ));
            }

            //Build the payslip
            decimal GrossPay = baseSalary.Value + allowances.Value - deductions.Value;
            decimal TaxAmount = GrossPay * taxRate.Value!;
            decimal NetPay = GrossPay - TaxAmount;

            return new PayslipBuildResult(
                Success: true,
                Payslip: new PayslipSummary(
                    EmployeeId: employeeId,
                    BaseSalary: baseSalary.Value,
                    PaidHours: attendance.Value!.paidHours,
                    OvertimeHours: attendance.Value!.overtimeHours,
                    Allowances: allowancesValue,
                    Deductions: deductionsValue,
                    GrossPay: GrossPay,
                    TaxAmount: TaxAmount,
                    NetPay: NetPay
                ),
                Warnings: warnings,
                ErrorMessage: null
            );
        }
    }
}
