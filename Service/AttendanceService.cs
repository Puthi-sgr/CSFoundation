using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    }
}
