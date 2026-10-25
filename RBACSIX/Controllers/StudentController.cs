using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;
using RBACSIX.Services;

namespace RBACSIX.Controllers;

[Authorize(Roles = "Student")]
public class StudentController : Controller
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _coachingTimeZone;
    private readonly IPrivateFileStorage _fileStorage;
    private readonly ILogger<StudentController> _logger;

    public StudentController(
        AppDbContext db,
        TimeZoneInfo coachingTimeZone,
        IPrivateFileStorage fileStorage,
        ILogger<StudentController> logger)
    {
        _db = db;
        _coachingTimeZone = coachingTimeZone;
        _fileStorage = fileStorage;
        _logger = logger;
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
                            .Where(scheduledClass =>
                                scheduledClass.StartTimeUtc.AddMinutes(scheduledClass.DurationMinutes) > now)
                            .OrderBy(scheduledClass => scheduledClass.StartTimeUtc)
                            .Select(scheduledClass => new StudentClassViewModel
                            {
                                Title = scheduledClass.Title,
                                StartTimeLocal = scheduledClass.StartTimeUtc,
                                DurationMinutes = scheduledClass.DurationMinutes,
                                JoinUrl = scheduledClass.JoinUrl,
                                CanJoin = scheduledClass.StartTimeUtc.AddMinutes(scheduledClass.DurationMinutes) > now
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

    [HttpGet]
    public async Task<IActionResult> Learning()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var profile = await _db.StudentProfiles
            .SingleOrDefaultAsync(item => item.UserId == userId);
        if (profile is null)
            return NotFound("Your student profile is not linked to this account.");

        var batches = await _db.Batches
            .Where(batch => batch.Enrollments.Any(enrollment => enrollment.StudentProfileId == profile.Id))
            .Include(batch => batch.Course)
            .Include(batch => batch.StudyMaterials)
            .Include(batch => batch.Assignments)
                .ThenInclude(assignment => assignment.Submissions
                    .Where(submission => submission.StudentProfileId == profile.Id))
            .OrderBy(batch => batch.StartDate)
            .ToListAsync();

        var model = new StudentLearningViewModel
        {
            StudentName = profile.FullName,
            Batches = batches.Select(batch => new StudentLearningBatchViewModel
            {
                BatchId = batch.Id,
                BatchName = batch.Name,
                CourseName = batch.Course.Name,
                Materials = batch.StudyMaterials
                    .OrderByDescending(material => material.CreatedUtc)
                    .Select(material => new StudyMaterialSummaryViewModel
                    {
                        Id = material.Id,
                        Title = material.Title,
                        Description = material.Description,
                        ResourceType = material.ResourceType,
                        ExternalUrl = material.ExternalUrl,
                        OriginalFileName = material.OriginalFileName,
                        CreatedUtc = material.CreatedUtc
                    })
                    .ToList(),
                Assignments = batch.Assignments
                    .OrderByDescending(assignment => assignment.CreatedUtc)
                    .Select(assignment =>
                    {
                        var submission = assignment.Submissions.SingleOrDefault();
                        return new StudentAssignmentViewModel
                        {
                            Id = assignment.Id,
                            Title = assignment.Title,
                            Description = assignment.Description,
                            MaxScore = assignment.MaxScore,
                            CreatedUtc = assignment.CreatedUtc,
                            SubmissionId = submission?.Id,
                            OriginalFileName = submission?.OriginalFileName,
                            Comments = submission?.Comments,
                            SubmittedUtc = submission?.SubmittedUtc,
                            Score = submission?.Score,
                            Feedback = submission?.Feedback,
                            IsGraded = submission?.GradedUtc is not null
                        };
                    })
                    .ToList()
            }).ToList()
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<IActionResult> SubmitAssignment(SubmitAssignmentViewModel model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var profileId = await _db.StudentProfiles
            .Where(profile => profile.UserId == userId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync();
        if (profileId is null)
            return NotFound();

        var assignment = await _db.Assignments
            .SingleOrDefaultAsync(item =>
                item.Id == model.AssignmentId &&
                item.Batch.Enrollments.Any(enrollment => enrollment.StudentProfileId == profileId));
        if (assignment is null)
            return NotFound();
        if (model.File is null)
        {
            TempData["ErrorMessage"] = "Select a file to submit.";
            return RedirectToAction(nameof(Learning));
        }

        var submission = await _db.AssignmentSubmissions
            .SingleOrDefaultAsync(item =>
                item.AssignmentId == assignment.Id &&
                item.StudentProfileId == profileId);
        if (submission?.GradedUtc is not null)
        {
            TempData["ErrorMessage"] = "This submission has been graded and can no longer be changed.";
            return RedirectToAction(nameof(Learning));
        }

        StoredUpload upload;
        try
        {
            upload = await _fileStorage.SaveSubmissionAsync(model.File, HttpContext.RequestAborted);
        }
        catch (InvalidUploadException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToAction(nameof(Learning));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not store a submission for assignment {AssignmentId}.", assignment.Id);
            TempData["ErrorMessage"] = "The submission file could not be saved. Try again.";
            return RedirectToAction(nameof(Learning));
        }

        var oldFileName = submission?.StoredFileName;
        if (submission is null)
        {
            submission = new AssignmentSubmission
            {
                AssignmentId = assignment.Id,
                StudentProfileId = profileId.Value
            };
            _db.AssignmentSubmissions.Add(submission);
        }
        submission.StoredFileName = upload.StoredFileName;
        submission.OriginalFileName = upload.OriginalFileName;
        submission.ContentType = upload.ContentType;
        submission.Comments = model.Comments?.Trim();
        submission.SubmittedUtc = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception, "Could not save a submission for assignment {AssignmentId}.", assignment.Id);
            await TryDeleteStoredFileAsync(upload.StoredFileName);
            TempData["ErrorMessage"] = "The submission could not be saved. Try again.";
            return RedirectToAction(nameof(Learning));
        }

        var oldFileRemoved = oldFileName is null ||
                             string.Equals(oldFileName, upload.StoredFileName, StringComparison.Ordinal) ||
                             await TryDeleteStoredFileAsync(oldFileName);
        if (!oldFileRemoved)
        {
            TempData["ErrorMessage"] = "Your new submission was saved, but the previous file could not be removed. Contact the coaching center.";
            return RedirectToAction(nameof(Learning));
        }
        TempData["Message"] = "Assignment submitted.";
        return RedirectToAction(nameof(Learning));
    }

    [HttpGet]
    public async Task<IActionResult> DownloadStudyMaterial(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var material = await _db.StudyMaterials
            .Where(item => item.Id == id &&
                           item.Batch.Enrollments.Any(enrollment => enrollment.Student.UserId == userId))
            .Select(item => new { item.StoredFileName, item.OriginalFileName, item.ContentType })
            .SingleOrDefaultAsync();
        if (material?.StoredFileName is null)
            return NotFound();

        try
        {
            return File(_fileStorage.OpenRead(material.StoredFileName), material.ContentType ?? "application/pdf",
                material.OriginalFileName ?? "study-material.pdf");
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not read study material {MaterialId}.", id);
            return Problem("The study material could not be downloaded.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> DownloadSubmission(int assignmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var submission = await _db.AssignmentSubmissions
            .Where(item => item.AssignmentId == assignmentId && item.Student.UserId == userId)
            .Select(item => new { item.StoredFileName, item.OriginalFileName, item.ContentType })
            .SingleOrDefaultAsync();
        if (submission is null)
            return NotFound();

        try
        {
            return File(_fileStorage.OpenRead(submission.StoredFileName), submission.ContentType,
                submission.OriginalFileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not read submission for assignment {AssignmentId}.", assignmentId);
            return Problem("The assignment file could not be downloaded.");
        }
    }

    private async Task<bool> TryDeleteStoredFileAsync(string storedFileName)
    {
        try
        {
            await _fileStorage.DeleteAsync(storedFileName);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not remove replaced/orphaned upload {StoredFileName}.", storedFileName);
            return false;
        }
    }
}
