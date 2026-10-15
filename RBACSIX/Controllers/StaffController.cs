using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;

namespace RBACSIX.Controllers;

[Authorize(Roles = "Staff")]
public class StaffController : Controller
{
    private readonly AppDbContext _db;

    public StaffController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var batches = await GetAssignedBatches(userId)
            .OrderBy(batch => batch.StartDate)
            .Select(batch => new BatchSummaryViewModel
            {
                Id = batch.Id,
                Name = batch.Name,
                CourseName = batch.Course.Name,
                ScheduleDescription = batch.ScheduleDescription ?? string.Empty,
                StartDate = batch.StartDate,
                EndDate = batch.EndDate,
                StudentCount = batch.Enrollments.Count
            })
            .ToListAsync();
        return View(batches);
    }

    [HttpGet]
    public async Task<IActionResult> Attendance(int batchId, DateTime? date)
    {
        var batch = await GetAssignedBatches(User.FindFirstValue(ClaimTypes.NameIdentifier))
            .Include(item => item.Enrollments)
                .ThenInclude(enrollment => enrollment.Student)
            .SingleOrDefaultAsync(item => item.Id == batchId);
        if (batch is null)
            return NotFound();

        var attendanceDate = (date ?? DateTime.Today).Date;
        var enrollmentIds = batch.Enrollments.Select(enrollment => enrollment.Id).ToList();
        var existing = await _db.AttendanceRecords
            .Where(record => enrollmentIds.Contains(record.EnrollmentId) && record.Date == attendanceDate)
            .ToDictionaryAsync(record => record.EnrollmentId);

        return View(new MarkAttendanceViewModel
        {
            BatchId = batch.Id,
            BatchName = batch.Name,
            Date = attendanceDate,
            Students = batch.Enrollments
                .OrderBy(enrollment => enrollment.Student.FullName)
                .Select(enrollment =>
                {
                    var hasRecord = existing.TryGetValue(enrollment.Id, out var record);
                    return new StudentAttendanceViewModel
                    {
                        EnrollmentId = enrollment.Id,
                        StudentName = enrollment.Student.FullName,
                        IsPresent = hasRecord && record!.IsPresent,
                        Notes = hasRecord ? record!.Notes : null
                    };
                })
                .ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Attendance(MarkAttendanceViewModel model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var batch = await GetAssignedBatches(userId)
            .Include(item => item.Enrollments)
            .SingleOrDefaultAsync(item => item.Id == model.BatchId);
        if (batch is null)
            return NotFound();

        var expectedIds = batch.Enrollments.Select(enrollment => enrollment.Id).OrderBy(id => id).ToArray();
        var submittedIds = model.Students.Select(student => student.EnrollmentId).OrderBy(id => id).ToArray();
        if (!expectedIds.SequenceEqual(submittedIds))
            return BadRequest("Attendance must include exactly the students enrolled in this batch.");

        if (!ModelState.IsValid)
            return await ReloadAttendanceView(model);

        var attendanceDate = model.Date.Date;
        var currentRecords = await _db.AttendanceRecords
            .Where(record => expectedIds.Contains(record.EnrollmentId) && record.Date == attendanceDate)
            .ToDictionaryAsync(record => record.EnrollmentId);

        foreach (var student in model.Students)
        {
            if (currentRecords.TryGetValue(student.EnrollmentId, out var record))
            {
                record.IsPresent = student.IsPresent;
                record.Notes = student.Notes?.Trim();
                record.MarkedByUserId = userId!;
            }
            else
            {
                _db.AttendanceRecords.Add(new AttendanceRecord
                {
                    EnrollmentId = student.EnrollmentId,
                    Date = attendanceDate,
                    IsPresent = student.IsPresent,
                    Notes = student.Notes?.Trim(),
                    MarkedByUserId = userId!
                });
            }
        }

        await _db.SaveChangesAsync();
        TempData["Message"] = "Attendance saved.";
        return RedirectToAction(nameof(Attendance), new { batchId = batch.Id, date = attendanceDate.ToString("yyyy-MM-dd") });
    }

    [HttpGet]
    public async Task<IActionResult> Results(int batchId)
    {
        var batch = await GetAssignedBatches(User.FindFirstValue(ClaimTypes.NameIdentifier))
            .Include(item => item.Enrollments)
                .ThenInclude(enrollment => enrollment.Student)
            .SingleOrDefaultAsync(item => item.Id == batchId);
        if (batch is null)
            return NotFound();

        return View(new RecordResultsViewModel
        {
            BatchId = batch.Id,
            BatchName = batch.Name,
            Students = batch.Enrollments
                .OrderBy(enrollment => enrollment.Student.FullName)
                .Select(enrollment => new StudentResultEntryViewModel
                {
                    EnrollmentId = enrollment.Id,
                    StudentName = enrollment.Student.FullName
                })
                .ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Results(RecordResultsViewModel model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var batch = await GetAssignedBatches(userId)
            .Include(item => item.Enrollments)
            .SingleOrDefaultAsync(item => item.Id == model.BatchId);
        if (batch is null)
            return NotFound();

        var expectedIds = batch.Enrollments.Select(enrollment => enrollment.Id).OrderBy(id => id).ToArray();
        var submittedIds = model.Students.Select(student => student.EnrollmentId).OrderBy(id => id).ToArray();
        if (!expectedIds.SequenceEqual(submittedIds))
            return BadRequest("Results must include exactly the students enrolled in this batch.");

        if (model.Students.Any(student => student.MarksObtained > model.MaxMarks))
            ModelState.AddModelError(nameof(model.Students), "A student's score cannot exceed the maximum marks.");

        if (!ModelState.IsValid)
        {
            model.BatchName = batch.Name;
            var studentNames = await _db.Enrollments
                .Where(enrollment => expectedIds.Contains(enrollment.Id))
                .ToDictionaryAsync(enrollment => enrollment.Id, enrollment => enrollment.Student.FullName);
            foreach (var student in model.Students)
                student.StudentName = studentNames[student.EnrollmentId];
            return View(model);
        }

        var examName = model.ExamName.Trim();
        var examDate = model.ExamDate.Date;
        var currentResults = await _db.ExamResults
            .Where(result => expectedIds.Contains(result.EnrollmentId) &&
                             result.ExamName == examName &&
                             result.ExamDate == examDate)
            .ToDictionaryAsync(result => result.EnrollmentId);
        foreach (var student in model.Students)
        {
            if (currentResults.TryGetValue(student.EnrollmentId, out var result))
            {
                result.MarksObtained = student.MarksObtained;
                result.MaxMarks = model.MaxMarks;
                result.RecordedByUserId = userId!;
            }
            else
            {
                _db.ExamResults.Add(new ExamResult
                {
                    EnrollmentId = student.EnrollmentId,
                    ExamName = examName,
                    ExamDate = examDate,
                    MarksObtained = student.MarksObtained,
                    MaxMarks = model.MaxMarks,
                    RecordedByUserId = userId!
                });
            }
        }

        await _db.SaveChangesAsync();
        TempData["Message"] = "Results recorded.";
        return RedirectToAction(nameof(Index));
    }

    private IQueryable<Batch> GetAssignedBatches(string? userId)
    {
        return _db.Batches.Where(batch => batch.StaffUserId == userId);
    }

    private async Task<IActionResult> ReloadAttendanceView(MarkAttendanceViewModel model)
    {
        var batch = await GetAssignedBatches(User.FindFirstValue(ClaimTypes.NameIdentifier))
            .Include(item => item.Enrollments)
                .ThenInclude(enrollment => enrollment.Student)
            .SingleAsync(item => item.Id == model.BatchId);
        var posted = model.Students.ToDictionary(student => student.EnrollmentId);
        model.BatchName = batch.Name;
        model.Students = batch.Enrollments
            .OrderBy(enrollment => enrollment.Student.FullName)
            .Select(enrollment =>
            {
                var attendance = posted[enrollment.Id];
                return new StudentAttendanceViewModel
                {
                    EnrollmentId = enrollment.Id,
                    StudentName = enrollment.Student.FullName,
                    IsPresent = attendance.IsPresent,
                    Notes = attendance.Notes
                };
            })
            .ToList();
        return View(nameof(Attendance), model);
    }
}
