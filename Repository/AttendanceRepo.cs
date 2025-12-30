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
