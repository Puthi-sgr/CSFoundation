using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsyncAwait.Models
{
    public record AttendanceRow(int EmployeeId, DateTime ClockInUtc, DateTime? ClockOutUtc);

    public enum ExportStatus
    {
        Success, 
        Cancelled,
        Failed
    }

    public record ExportResult(ExportStatus Status, string? FilePath, string? ErrorMessage);
}
