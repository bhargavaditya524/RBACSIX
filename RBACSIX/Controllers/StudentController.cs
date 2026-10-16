using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models.ViewModels;

namespace RBACSIX.Controllers;

[Authorize(Roles = "Student")]
public class StudentController : Controller
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _coachingTimeZone;

    public StudentController(AppDbContext db, TimeZoneInfo coachingTimeZone)
    {
        _db = db;
        _coachingTimeZone = coachingTimeZone;
    }

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var now = DateTime.UtcNow;
        var student = await _db.StudentProfiles
            .Where(profile => profile.UserId == userId)
            .Select(profile => new StudentDashboardViewModel
            {
                StudentName = profile.FullName,
                TimeZoneId = _coachingTimeZone.Id,
                Batches = profile.Enrollments
                    .OrderBy(enrollment => enrollment.Batch.StartDate)
                    .Select(enrollment => new StudentBatchViewModel
                    {
                        BatchName = enrollment.Batch.Name,
                        CourseName = enrollment.Batch.Course.Name,
                        StaffName = enrollment.Batch.StaffUser == null ||
                                    enrollment.Batch.StaffUser.StaffProfile == null
                            ? null
                            : enrollment.Batch.StaffUser.StaffProfile.FullName,
                        ScheduleDescription = enrollment.Batch.ScheduleDescription,
                        StartDate = enrollment.Batch.StartDate,
                        EndDate = enrollment.Batch.EndDate,
                        PresentCount = enrollment.AttendanceRecords.Count(record => record.IsPresent),
                        AttendanceCount = enrollment.AttendanceRecords.Count,
                        Results = enrollment.Results
                            .OrderByDescending(result => result.ExamDate)
                            .Select(result => new StudentResultSummaryViewModel
                            {
                                ExamName = result.ExamName,
                                ExamDate = result.ExamDate,
                                MarksObtained = result.MarksObtained,
                                MaxMarks = result.MaxMarks
                            })
                            .ToList(),
                        ScheduledClasses = enrollment.Batch.ScheduledClasses
                            .OrderBy(scheduledClass => scheduledClass.StartTimeUtc)
                            .Select(scheduledClass => new StudentClassViewModel
                            {
                                Title = scheduledClass.Title,
                                StartTimeLocal = scheduledClass.StartTimeUtc,
                                DurationMinutes = scheduledClass.DurationMinutes,
                                JoinUrl = scheduledClass.JoinUrl,
                                IsUpcoming = scheduledClass.StartTimeUtc >= now
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync();

        if (student is null)
            return NotFound("Your student profile is not linked to this account. Contact the coaching center administrator.");

        foreach (var scheduledClass in student.Batches.SelectMany(batch => batch.ScheduledClasses))
        {
            scheduledClass.StartTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(scheduledClass.StartTimeLocal, DateTimeKind.Utc),
                _coachingTimeZone);
        }

        return View(student);
    }
}
