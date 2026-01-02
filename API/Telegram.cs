using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AsyncAwait.Exception;
namespace AsyncAwait.API
{
    internal class Telegram
    {
        private static readonly Random Rng = new();

        public async Task SendAsync(int employeeId, CancellationToken ct)
        {
            await Task.Delay(120, ct); // network I/O

            // Simulated outcomes:
            int x = Rng.Next(1, 101);

            if (x <= 10) throw new RateLimitException(TimeSpan.FromMilliseconds(500));       // 10% rate limit
            if (x <= 25) throw new TransientSendException("Temporary network error");       // 15% transient
            if (x <= 30) throw new PermanentSendException("Invalid chat ID / user blocked"); // 5% permanent

            // else success
        }
    }
}
