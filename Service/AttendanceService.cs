using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO; // Add this using directive
using System.Threading; // Add this using directive
using AsyncAwait.Models;
using AsyncAwait.Repository;

namespace AsyncAwait.Attendance
{
    internal class AttendanceService
    {
        private readonly AttendanceRepo _attendanceRepo;

        public AttendanceService()
        {
            _attendanceRepo = new AttendanceRepo();
        }

        /*
            Contract:
                goal: Clock in employee if not already clocked in within last 2 minutes
                input: employeeId
                constraint: prevent Your backend must prevent duplicate clock-ins caused by:
                    slow internet (user taps twice)
                    app retries
                    user confusion (“did it work?”)

                output: bool (true if clocked in, false if already clocked in recently)
            Algorithm:
                Pre-condition:
                    - employeeId is valid
                    
                Operation:
                    - Get last clockin time from AttendanceRepo
                    - If last clockin times is within 2 minutes -> return "Already clocked in recently"
                    - Else
                        - Save current time as clockin time in AttendanceRepo
                        - return "Clock-in successful"
               Post-condition:
       
                    - Saved record of clock-in employee             
                    
         */
        public async Task<string> ClockInAsync(int employeeId, int clockedInAgo)
        {
            if (employeeId <= 0)
                throw new ArgumentException("Invalid employeeId");

            DateTime? lastClockInTime = await _attendanceRepo.GetLastClockAsync(employeeId, clockedInAgo);

            DateTime Now = DateTime.UtcNow;

            if (lastClockInTime.HasValue)
            {
                TimeSpan timeSpan = Now - lastClockInTime.Value;
                if(timeSpan.TotalMinutes < 2)
                {
                    return "Already clocked in recently";
                }                          
            }
            await _attendanceRepo.SaveClockInTime(employeeId, Now);
            return "Clock-in successful";
        }

        /*
         * Contract:
         *    goal: Create a exportable attendance report
         *    requirements:
         *      - Safely cancellable 
         *      - Can be retried later after cancellation
         *    constraint:
         *      - Cancellation might corrupt data due to not killing thread properly
         *      - Cancellation takes too long to process
         *      
         * Concept:
         *     - Attendance report generation can be a long-running task because you have to fetch page by page write csv line by line it can reach up to millions of records
         *     - The attendence report generation is cancellable because the user might fetch the wrong date or its too long or service is out
         *     
         * Operation analysis:
         *     -  Create attendance row (Represent one row from the db)
         *     -  Create export status enum (To tell you whats going on overall)
         *     -  Create a repo that accepts cancellation token (If the service cannot take cancellation token it cannot be cancelled quickly)
         *     -  Create a service that accepts cancellation token
         *     
         * 
         *    
         */
        public async Task<ExportResult> ExportMonthlyAttendanceCsvAsync(
            int year, int month, CancellationToken ct
            )
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"attendence_{year}_{month}_tmmp.csv");
            string finalPath = Path.Combine(Path.GetTempPath(), $"attendence_{year}_{month}.csv");

            try
            {
                using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(fs);

                //Step write to header
                await writer.WriteLineAsync("Employee Id, Clock In UTC, Clock Out UTC");

                //Step 4b: Paging loop
                int page = 1;
                const int pageSize = 100; //1 page have 100 records

                while (true)
                {
                    ct.ThrowIfCancellationRequested(); //The part where we check whether the use click cancel or not

                    var rows = await _attendanceRepo.GetAttendancePageAsync(year, month, page, pageSize, ct);

                    if (rows.Count == 0) break; //No more data

                    foreach(var row in rows)
                    {
                        string line = $"{row.EmployeeId}, {row.ClockInUtc:O}, {(row.ClockOutUtc.HasValue ? row.ClockOutUtc.Value.ToString("O") : "")}";
                        await writer.WriteLineAsync(line); //Write line to csv file should be hanging in the RAM
                    }

                    page++;
                }

                await writer.FlushAsync(); //Ensure all data is written to the file
                writer.Close();

                //Step 5: Move the temp file if success
                // Use overwrite to ensure that if the final file already exists, it will be replaced
                File.Move(tempPath, finalPath, overwrite: true);

                return new ExportResult(ExportStatus.Success, finalPath, null);
            }
            catch (OperationCanceledException)
            {
                //Clean up temp file the and delete half written file
                if (File.Exists(tempPath)) File.Delete(tempPath);

                return new ExportResult(ExportStatus.Cancelled, null, "Export cancelled by user.");
            }
            catch (System.Exception ex)
            {
                if(File.Exists(tempPath)) File.Delete(tempPath);

                return new ExportResult(ExportStatus.Failed, null, $"Export failed: {ex.Message}");
            }
        }
    }
}
