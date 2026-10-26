using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    private readonly UserManager<Users> _userManager;
    private readonly TimeZoneInfo _coachingTimeZone;
    private readonly IPrivateFileStorage _fileStorage;
    private readonly ILogger<StudentController> _logger;

    public StudentController(
        AppDbContext db,
        UserManager<Users> userManager,
        TimeZoneInfo coachingTimeZone,
        IPrivateFileStorage fileStorage,
        ILogger<StudentController> logger)
    {
        _db = db;
        _userManager = userManager;
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
                        PresentCount = enrollment.AttendanceRecords.Count(record =>
                            record.IsPresent &&
                            !enrollment.LeaveRequests.Any(request =>
                                request.Status == "Approved" &&
                                request.StartDate <= record.Date &&
                                request.EndDate >= record.Date)),
                        AttendanceCount = enrollment.AttendanceRecords.Count(record =>
                            !enrollment.LeaveRequests.Any(request =>
                                request.Status == "Approved" &&
                                request.StartDate <= record.Date &&
                                request.EndDate >= record.Date)),
                        TuitionFee = enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee,
                        MiscellaneousCharges = enrollment.Charges.Sum(charge => charge.Amount),
                        Payments = enrollment.Payments.Sum(payment => payment.Amount),
                        Balance = (enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee) +
                                  enrollment.Charges.Sum(charge => charge.Amount) -
                                  enrollment.Payments.Sum(payment => payment.Amount),
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
    public async Task<IActionResult> EnrollmentRequests()
    {
        var studentId = await GetStudentProfileIdAsync();
        if (studentId is null)
            return NotFound();

        var requestedBatchIds = await _db.EnrollmentRequests
            .Where(request => request.StudentProfileId == studentId &&
                              request.Status == "Pending")
            .Select(request => request.BatchId)
            .ToListAsync();
        var enrolledBatchIds = await _db.Enrollments
            .Where(enrollment => enrollment.StudentProfileId == studentId)
            .Select(enrollment => enrollment.BatchId)
            .ToListAsync();
        return View(new StudentEnrollmentRequestsViewModel
        {
            AvailableBatches = await _db.Batches
                .Where(batch => batch.Course.IsActive &&
                                !enrolledBatchIds.Contains(batch.Id) &&
                                !requestedBatchIds.Contains(batch.Id))
                .OrderBy(batch => batch.Course.Name)
                .ThenBy(batch => batch.Name)
                .Select(batch => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(
                    batch.Capacity.HasValue && batch.Enrollments.Count >= batch.Capacity.Value
                        ? $"{batch.Course.Name} — {batch.Name} (currently full)"
                        : $"{batch.Course.Name} — {batch.Name}",
                    batch.Id.ToString()))
                .ToListAsync(),
            Requests = await _db.EnrollmentRequests
                .Where(request => request.StudentProfileId == studentId)
                .OrderByDescending(request => request.RequestedUtc)
                .Select(request => new EnrollmentRequestViewModel
                {
                    Id = request.Id,
                    BatchId = request.BatchId,
                    StudentProfileId = request.StudentProfileId,
                    StudentName = request.Student.FullName,
                    BatchName = request.Batch.Name,
                    CourseName = request.Batch.Course.Name,
                    Message = request.Message,
                    Status = request.Status,
                    RequestedUtc = request.RequestedUtc,
                    ReviewNote = request.ReviewNote,
                    Capacity = request.Batch.Capacity,
                    EnrollmentCount = request.Batch.Enrollments.Count
                }).ToListAsync()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestEnrollment(CreateEnrollmentRequestViewModel model)
    {
        var studentId = await GetStudentProfileIdAsync();
        if (studentId is null)
            return NotFound();

        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var batchIsAvailable = await _db.Batches.AnyAsync(batch =>
            batch.Id == model.BatchId && batch.Course.IsActive);
        if (!batchIsAvailable)
            return NotFound();
        if (await _db.Enrollments.AnyAsync(enrollment =>
                enrollment.StudentProfileId == studentId && enrollment.BatchId == model.BatchId))
            ModelState.AddModelError(nameof(model.BatchId), "You are already enrolled in this batch.");
        if (await _db.EnrollmentRequests.AnyAsync(request =>
                request.StudentProfileId == studentId &&
                request.BatchId == model.BatchId &&
                request.Status == "Pending"))
            ModelState.AddModelError(nameof(model.BatchId), "A request for this batch is already pending.");
        if (!ModelState.IsValid)
        {
            await transaction.RollbackAsync();
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage));
            return RedirectToAction(nameof(EnrollmentRequests));
        }

        _db.EnrollmentRequests.Add(new EnrollmentRequest
        {
            StudentProfileId = studentId.Value,
            BatchId = model.BatchId,
            Message = model.Message?.Trim(),
            Status = "Pending",
            RequestedUtc = DateTime.UtcNow
        });
        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(exception, "Could not save an enrollment request for student {StudentProfileId} and batch {BatchId}.", studentId.Value, model.BatchId);
            TempData["ErrorMessage"] = "The request could not be saved. It may already be pending; refresh and try again.";
            return RedirectToAction(nameof(EnrollmentRequests));
        }
        TempData["Message"] = "Your enrollment request was sent to the administrator.";
        return RedirectToAction(nameof(EnrollmentRequests));
    }

    [HttpGet]
    public IActionResult RegisterParent() => View(new RegisterParentViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterParent(RegisterParentViewModel model)
    {
        var student = await GetStudentProfileAsync();
        if (student is null)
            return NotFound();
        if (!ModelState.IsValid)
            return View(model);
        if (await _userManager.FindByEmailAsync(model.Email) is not null)
        {
            ModelState.AddModelError(nameof(model.Email),
                "An account with this email already exists. Contact an administrator to link an existing Parent account.");
            return View(model);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var user = new Users
        {
            UserName = model.Email,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            LockoutEnabled = true
        };
        var created = await _userManager.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            AddIdentityErrors(created);
            return View(model);
        }
        var assigned = await _userManager.AddToRoleAsync(user, "Parent");
        if (!assigned.Succeeded)
        {
            AddIdentityErrors(assigned);
            return View(model);
        }

        _db.ParentProfiles.Add(new ParentProfile { UserId = user.Id, FullName = model.FullName.Trim() });
        _db.ParentStudentLinks.Add(new ParentStudentLink
        {
            ParentUserId = user.Id,
            StudentProfileId = student.Id
        });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Message"] = "Parent account created and linked to your profile.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> LeaveRequests()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var studentId = await _db.StudentProfiles
            .Where(profile => profile.UserId == userId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync();
        if (studentId is null)
            return NotFound();

        var model = new LeaveRequestsViewModel
        {
            PageTitle = "My leave requests",
            SelectedStudentProfileId = studentId,
            Enrollments = await _db.Enrollments
                .Where(enrollment => enrollment.StudentProfileId == studentId)
                .OrderBy(enrollment => enrollment.Batch.Name)
                .Select(enrollment => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(
                    enrollment.Batch.Name, enrollment.Id.ToString()))
                .ToListAsync(),
            Requests = await _db.LeaveRequests
                .Where(request => request.Enrollment.StudentProfileId == studentId)
                .OrderByDescending(request => request.CreatedUtc)
                .Select(request => new LeaveRequestItemViewModel
                {
                    Id = request.Id,
                    EnrollmentId = request.EnrollmentId,
                    StudentName = request.Enrollment.Student.FullName,
                    BatchName = request.Enrollment.Batch.Name,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    Reason = request.Reason,
                    Status = request.Status,
                    RequestedByName = "You",
                    ReviewNote = request.ReviewNote,
                    CreatedUtc = request.CreatedUtc
                })
                .ToListAsync()
        };
        return View("~/Views/Shared/LeaveRequests.cshtml", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestLeave(CreateLeaveRequestViewModel model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var studentId = await _db.StudentProfiles
            .Where(profile => profile.UserId == userId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync();
        if (studentId is null)
            return NotFound();
        if (!await _db.Enrollments.AnyAsync(enrollment =>
                enrollment.Id == model.EnrollmentId &&
                enrollment.StudentProfileId == studentId))
            return NotFound();
        if (model.StartDate.Date > model.EndDate.Date)
            ModelState.AddModelError(nameof(model.EndDate), "End date must be on or after the start date.");
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage));
            return RedirectToAction(nameof(LeaveRequests));
        }

        _db.LeaveRequests.Add(new LeaveRequest
        {
            EnrollmentId = model.EnrollmentId,
            RequestedByUserId = userId!,
            StartDate = model.StartDate.Date,
            EndDate = model.EndDate.Date,
            Reason = model.Reason.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Leave request submitted.";
        return RedirectToAction(nameof(LeaveRequests));
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

    private Task<StudentProfile?> GetStudentProfileAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return _db.StudentProfiles.SingleOrDefaultAsync(profile => profile.UserId == userId);
    }

    private async Task<int?> GetStudentProfileIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await _db.StudentProfiles
            .Where(profile => profile.UserId == userId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync();
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
    }
}
