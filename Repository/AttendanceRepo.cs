using AsyncAwait.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsyncAwait.Repository
{
    internal class AttendanceRepo
    {
        public AttendanceRepo() { }

        public async Task<List<AttendanceRow>> GetAttendancePageAsync(int year, int month, int page, int pageSize, CancellationToken ct)
        {
            //Every iteration from the service call the cancellation token will be checked
            await Task.Delay(150, ct); //Simulate IO wait

            //Fake stop after 5 page

            if (page > 5) return  new List<AttendanceRow>();

            var rows = new List<AttendanceRow>();

            for(int i = 0; i < pageSize; i++)
            {
                rows.Add(new AttendanceRow(
                        EmployeeId: (page -1) * pageSize + i + 1,
                        ClockInUtc: new DateTime(year, month, 1).AddDays((page - 1) * pageSize + i),
                        ClockOutUtc: null

                    ));
            }

            return rows;
        }

        /*
           Contract:
                goal: Simulate get last clockedin
                input: employeeId
                output: DateTime    
                operation: IO bound
         */
        public async Task<DateTime> GetLastClockAsync(int employeeId, int minutesAgo)
        {
            await Task.Delay(2000); //Simulate IO wait
            return DateTime.UtcNow.AddMinutes(-minutesAgo); //Pretend clocked in 1 minute ago
        }

        /*
            Contract:
                goal: Simulate save clockin
                input: employeeId, clockInTime
                output: void
         */
        public async Task SaveClockInTime(int employeeId, DateTime clockInTime)
        {
            await Task.Delay(1000); //Simulate IO wait
            //Pretend to save clock in time
        }
    }
}
