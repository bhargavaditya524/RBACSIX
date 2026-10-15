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

    public StudentController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var student = await _db.StudentProfiles
            .Where(profile => profile.UserId == userId)
            .Select(profile => new StudentDashboardViewModel
            {
                StudentName = profile.FullName,
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
                            .ToList()
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync();

        if (student is null)
            return NotFound("Your student profile is not linked to this account. Contact the coaching center administrator.");

        return View(student);
    }
}
