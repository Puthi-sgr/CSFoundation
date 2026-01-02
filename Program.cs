using AsyncAwait.API;
using AsyncAwait.Attendance;
using AsyncAwait.Service;
using System;
using System.Diagnostics;
using System.Threading.Tasks;


internal class Program
{
    private static async Task Main(string[] args)
    {
        var svc = new AttendanceService();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(10000)); // cancel mid-export

        var result = await svc.ExportMonthlyAttendanceCsvAsync(2025, 12, cts.Token);
        Console.WriteLine(result);
    }
}