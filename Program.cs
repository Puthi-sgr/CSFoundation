using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AsyncAwait.Attendance;
using AsyncAwait.Service;

internal class Program
{
    private static async Task Main(string[] args)
    {
        int testEmployeeId = 1;
        int clockedInAgo = 1;
        var attendanceService = new AttendanceService();
        var payrollService = new PayrollService();



        PayslipBuildResult? payrollResult = await payrollService.BuildPayslipAsync(testEmployeeId, PayRollBuildMode.FullStrict);

        Console.WriteLine("Payroll Result:");
        Console.WriteLine(payrollResult.ToString());
        Console.WriteLine("Payroll warngings:");
        foreach (var warning in payrollResult.Warnings)
        {
            Console.WriteLine(warning.Message);
        }
    }
}