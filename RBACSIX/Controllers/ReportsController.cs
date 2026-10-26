using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models.ViewModels;

namespace RBACSIX.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        if (User.IsInRole("Admin"))
            return View(await BuildAdminReportAsync());
        if (User.IsInRole("Staff"))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null ||
                !await _db.StaffProfiles.AnyAsync(profile => profile.UserId == userId && profile.IsApproved))
                return Forbid();
            return View(await BuildStaffReportAsync(userId));
        }
        if (User.IsInRole("Student"))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Forbid();
            return View(await BuildStudentReportAsync(userId));
        }

        return Forbid();
    }

    private async Task<ReportsViewModel> BuildAdminReportAsync()
    {
        var records = _db.AttendanceRecords
            .Where(record => !record.Enrollment.LeaveRequests.Any(request =>
                request.Status == "Approved" &&
                request.StartDate <= record.Date &&
                request.EndDate >= record.Date));
        var totalSessions = await records.CountAsync();
        var attendedSessions = await records.CountAsync(record => record.IsPresent);
        var courseEnrollments = await _db.Courses
            .OrderBy(course => course.Name)
            .Select(course => new ReportChartPointViewModel
            {
                Label = course.Name,
                Value = course.Batches.SelectMany(batch => batch.Enrollments).Count()
            })
            .ToListAsync();
        var payments = await _db.EnrollmentPayments.SumAsync(payment => (decimal?)payment.Amount) ?? 0m;
        var balances = await _db.Enrollments.SumAsync(enrollment =>
            (decimal?)((enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee) +
                       enrollment.Charges.Sum(charge => charge.Amount) -
                       enrollment.Payments.Sum(payment => payment.Amount))) ?? 0m;
        var attendanceByMonth = await records
            .GroupBy(record => new { record.Date.Year, record.Date.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Present = group.Count(record => record.IsPresent),
                Total = group.Count()
            })
            .OrderBy(group => group.Year)
            .ThenBy(group => group.Month)
            .ToListAsync();
        var monthlyAttendance = attendanceByMonth
            .Select(group => new ReportChartPointViewModel
            {
                Label = $"{group.Year}-{group.Month:00}",
                Value = group.Total == 0 ? 0 : decimal.Round(group.Present * 100m / group.Total, 1)
            })
            .ToList();

        return new ReportsViewModel
        {
            RoleName = "Admin",
            PageTitle = "Admin reports",
            Metrics =
            [
                new() { Label = "Students", Value = (await _db.StudentProfiles.CountAsync()).ToString() },
                new() { Label = "Enrollments", Value = (await _db.Enrollments.CountAsync()).ToString() },
                new() { Label = "Payments received", Value = payments.ToString("C") },
                new() { Label = "Outstanding balance", Value = balances.ToString("C") },
                new() { Label = "Attendance rate", Value = FormatPercentage(attendedSessions, totalSessions) }
            ],
            Charts =
            [
                MakeChart("Enrollments by course", "students", courseEnrollments),
                MakeChart("Attendance rate by month", "%", monthlyAttendance, 100)
            ]
        };
    }

    private async Task<ReportsViewModel> BuildStaffReportAsync(string userId)
    {
        var assignedBatches = await _db.Batches
            .Where(batch => batch.StaffUserId == userId)
            .OrderBy(batch => batch.Name)
            .Select(batch => new
            {
                batch.Name,
                AttendanceTotal = batch.Enrollments
                    .SelectMany(enrollment => enrollment.AttendanceRecords)
                    .Count(record => !record.Enrollment.LeaveRequests.Any(request =>
                        request.Status == "Approved" &&
                        request.StartDate <= record.Date &&
                        request.EndDate >= record.Date)),
                AttendancePresent = batch.Enrollments
                    .SelectMany(enrollment => enrollment.AttendanceRecords)
                    .Count(record => record.IsPresent &&
                        !record.Enrollment.LeaveRequests.Any(request =>
                            request.Status == "Approved" &&
                            request.StartDate <= record.Date &&
                            request.EndDate >= record.Date)),
                AverageResult = batch.Enrollments
                    .SelectMany(enrollment => enrollment.Results)
                    .Select(result => (decimal?)(result.MarksObtained * 100m / result.MaxMarks))
                    .Average()
            })
            .ToListAsync();
        var totalAttendance = assignedBatches.Sum(batch => batch.AttendanceTotal);
        var presentAttendance = assignedBatches.Sum(batch => batch.AttendancePresent);
        var attendancePoints = assignedBatches.Select(batch => new ReportChartPointViewModel
        {
            Label = batch.Name,
            Value = batch.AttendanceTotal == 0
                ? 0
                : decimal.Round(batch.AttendancePresent * 100m / batch.AttendanceTotal, 1)
        }).ToList();
        var resultPoints = assignedBatches.Select(batch => new ReportChartPointViewModel
        {
            Label = batch.Name,
            Value = decimal.Round(batch.AverageResult ?? 0, 1)
        }).ToList();

        return new ReportsViewModel
        {
            RoleName = "Staff",
            PageTitle = "Staff reports",
            Metrics =
            [
                new() { Label = "Assigned batches", Value = assignedBatches.Count.ToString() },
                new() { Label = "Students in my batches", Value = (await _db.Batches.Where(batch => batch.StaffUserId == userId).SelectMany(batch => batch.Enrollments).Select(enrollment => enrollment.StudentProfileId).Distinct().CountAsync()).ToString() },
                new() { Label = "Attendance rate", Value = FormatPercentage(presentAttendance, totalAttendance) }
            ],
            Charts =
            [
                MakeChart("Attendance rate by assigned batch", "%", attendancePoints, 100),
                MakeChart("Average result by assigned batch", "%", resultPoints, 100)
            ]
        };
    }

    private async Task<ReportsViewModel> BuildStudentReportAsync(string userId)
    {
        var studentId = await _db.StudentProfiles
            .Where(profile => profile.UserId == userId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync();
        if (studentId is null)
            return new ReportsViewModel { RoleName = "Student", PageTitle = "My reports" };

        var enrollments = await _db.Enrollments
            .Where(enrollment => enrollment.StudentProfileId == studentId)
            .OrderBy(enrollment => enrollment.Batch.Name)
            .Select(enrollment => new
            {
                enrollment.Batch.Name,
                AttendanceTotal = enrollment.AttendanceRecords.Count(record =>
                    !enrollment.LeaveRequests.Any(request =>
                        request.Status == "Approved" &&
                        request.StartDate <= record.Date &&
                        request.EndDate >= record.Date)),
                AttendancePresent = enrollment.AttendanceRecords.Count(record =>
                    record.IsPresent &&
                    !enrollment.LeaveRequests.Any(request =>
                        request.Status == "Approved" &&
                        request.StartDate <= record.Date &&
                        request.EndDate >= record.Date)),
                AverageResult = enrollment.Results
                    .Select(result => (decimal?)(result.MarksObtained * 100m / result.MaxMarks))
                    .Average(),
                Balance = (enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee) +
                          enrollment.Charges.Sum(charge => charge.Amount) -
                          enrollment.Payments.Sum(payment => payment.Amount)
            })
            .ToListAsync();
        var totalSessions = enrollments.Sum(enrollment => enrollment.AttendanceTotal);
        var attendedSessions = enrollments.Sum(enrollment => enrollment.AttendancePresent);
        var attendance = enrollments.Select(enrollment => new ReportChartPointViewModel
        {
            Label = enrollment.Name,
            Value = enrollment.AttendanceTotal == 0
                ? 0
                : decimal.Round(enrollment.AttendancePresent * 100m / enrollment.AttendanceTotal, 1)
        }).ToList();
        var results = enrollments.Select(enrollment => new ReportChartPointViewModel
        {
            Label = enrollment.Name,
            Value = decimal.Round(enrollment.AverageResult ?? 0, 1)
        }).ToList();
        var balances = enrollments.Select(enrollment => new ReportChartPointViewModel
        {
            Label = enrollment.Name,
            Value = Math.Max(0, enrollment.Balance)
        }).ToList();

        return new ReportsViewModel
        {
            RoleName = "Student",
            PageTitle = "My reports",
            Metrics =
            [
                new() { Label = "Enrolled batches", Value = enrollments.Count.ToString() },
                new() { Label = "Attendance rate", Value = FormatPercentage(attendedSessions, totalSessions) },
                new() { Label = "Average result", Value = FormatAverage(enrollments.Select(enrollment => enrollment.AverageResult)) },
                new() { Label = "Outstanding balance", Value = enrollments.Sum(enrollment => enrollment.Balance).ToString("C") }
            ],
            Charts =
            [
                MakeChart("Attendance by batch", "%", attendance, 100),
                MakeChart("Average result by batch", "%", results, 100),
                MakeChart("Outstanding balance by batch", "currency", balances)
            ]
        };
    }

    private static ReportChartViewModel MakeChart(
        string title,
        string unit,
        List<ReportChartPointViewModel> points,
        decimal? maximum = null)
    {
        var positiveMaximum = points.Count == 0 ? 0 : points.Max(point => point.Value);
        return new ReportChartViewModel
        {
            Title = title,
            Unit = unit,
            Maximum = maximum ?? (positiveMaximum == 0 ? 1 : positiveMaximum),
            Points = points
        };
    }

    private static string FormatPercentage(int numerator, int denominator) =>
        denominator == 0 ? "No attendance recorded" : $"{numerator * 100m / denominator:0.0}%";

    private static string FormatAverage(IEnumerable<decimal?> values)
    {
        var results = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return results.Count == 0 ? "No results recorded" : $"{results.Average():0.0}%";
    }
}
