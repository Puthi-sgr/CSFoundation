using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsyncAwait.Repository
{
    internal class HRRepo
    {
        public HRRepo() { }
        public async Task<decimal> GetBaseSalaryAsync(int employeeId)
        {
            await Task.Delay(250); // DB
            return 500m;
        }

        public async Task<(int paidHours, int overtimeHours)> GetAttendanceAsync(int employeeId)
        {
            await Task.Delay(400); // Timekeeping DB
            return (paidHours: 160, overtimeHours: 12);
        }

        public async Task<decimal> GetAllowancesAsync(int employeeId)
        {
            await Task.Delay(300); // DB
            return 50m;
        }

        public async Task<decimal> GetDeductionsAsync(int employeeId)
        {
            await Task.Delay(1000); // DB
            return 20m;
        }
    }
}
