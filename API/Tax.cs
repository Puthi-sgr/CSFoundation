using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsyncAwait.API
{
    internal class Tax
    {
        public Tax() { }
        public async Task<decimal> TaxRate(int employeeId)
        {
            throw new NotImplementedException();
            await Task.Delay(150); // Simulate computation delay
            // Simple tax calculation: 10% of gross salary
            return 0.10m;
        }
    }
}
