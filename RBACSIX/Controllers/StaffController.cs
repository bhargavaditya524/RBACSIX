using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;
using RBACSIX.Services;

namespace RBACSIX.Controllers;

[Authorize(Roles = "Staff")]
public class StaffController : Controller
{
    private readonly AppDbContext _db;
    private readonly IZoomMeetingService _zoomMeetingService;
    private readonly IPrivateFileStorage _fileStorage;
    private readonly TimeZoneInfo _coachingTimeZone;
    private readonly ILogger<StaffController> _logger;

    public StaffController(
        AppDbContext db,
        IZoomMeetingService zoomMeetingService,
        IPrivateFileStorage fileStorage,
        TimeZoneInfo coachingTimeZone,
        ILogger<StaffController> logger)
    {
        _db = db;
        _zoomMeetingService = zoomMeetingService;
        _fileStorage = fileStorage;
        _coachingTimeZone = coachingTimeZone;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var now = DateTime.UtcNow;
        var batches = await GetAssignedBatches(userId)
            .Include(batch => batch.ScheduledClasses
                .Where(scheduledClass => scheduledClass.StartTimeUtc.AddMinutes(scheduledClass.DurationMinutes) > now))
            .Include(batch => batch.Enrollments)
            .Include(batch => batch.Course)
            .OrderBy(batch => batch.StartDate)
            .ToListAsync();
        var model = batches.Select(batch => new BatchSummaryViewModel
            {
                Id = batch.Id,
                Name = batch.Name,
                CourseName = batch.Course.Name,
                ScheduleDescription = batch.ScheduleDescription ?? string.Empty,
                StartDate = batch.StartDate,
                EndDate = batch.EndDate,
                StudentCount = batch.Enrollments.Count,
                ScheduledClassCount = batch.ScheduledClasses.Count,
                ScheduledClasses = batch.ScheduledClasses
                    .OrderBy(scheduledClass => scheduledClass.StartTimeUtc)
                    .Select(scheduledClass => new ScheduledClassSummaryViewModel
                    {
                        Id = scheduledClass.Id,
                        Title = scheduledClass.Title,
                        BatchName = batch.Name,
                        StartTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(
                            DateTime.SpecifyKind(scheduledClass.StartTimeUtc, DateTimeKind.Utc),
                            _coachingTimeZone),
                        DurationMinutes = scheduledClass.DurationMinutes,
                        ZoomMeetingId = scheduledClass.ZoomMeetingId,
                        IsUpcoming = scheduledClass.StartTimeUtc >= now,
                        CanDelete = scheduledClass.CreatedByStaffUserId == userId
                    })
                    .ToList()
            })
            .ToList();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScheduledClass(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var scheduledClass = await _db.ScheduledClasses
            .SingleOrDefaultAsync(item =>
                item.Id == id &&
                item.CreatedByStaffUserId == userId);
        if (scheduledClass is null)
            return NotFound();

        try
        {
            await _zoomMeetingService.DeleteMeetingAsync(
                scheduledClass.ZoomMeetingId,
                HttpContext.RequestAborted);
        }
        catch (ZoomMeetingException exception)
        {
            _logger.LogWarning(
                exception,
                "Staff user {UserId} could not delete Zoom class {ScheduledClassId}; retaining its record.",
                userId,
                scheduledClass.Id);
            TempData["ErrorMessage"] = "Zoom could not cancel this class. The class was not deleted; try again.";
            return RedirectToAction(nameof(Index));
        }

        _db.ScheduledClasses.Remove(scheduledClass);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "Zoom class {ScheduledClassId} was canceled, but its local record could not be deleted.",
                scheduledClass.Id);
            TempData["ErrorMessage"] = "The Zoom meeting was canceled, but the class record could not be deleted. Contact an administrator.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Message"] = "Zoom class deleted.";
        return RedirectToAction(nameof(Index));
    }

        [HttpGet]
        public async Task<IActionResult> BatchLearning(int batchId)
        {
            var batch = await GetAssignedBatches(User.FindFirstValue(ClaimTypes.NameIdentifier))
                .Include(item => item.Course)
                .SingleOrDefaultAsync(item => item.Id == batchId);
            if (batch is null)
                return NotFound();

            return View(await BuildBatchLearningViewModelAsync(batch));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(21 * 1024 * 1024)]
        public async Task<IActionResult> CreateStudyMaterial(CreateStudyMaterialViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var batch = await GetAssignedBatches(userId).SingleOrDefaultAsync(item => item.Id == model.BatchId);
            if (batch is null)
                return NotFound();

            StoredUpload? upload = null;
            if (string.Equals(model.ResourceType, "Pdf", StringComparison.OrdinalIgnoreCase))
            {
                if (model.File is null)
                    ModelState.AddModelError(nameof(model.File), "Select a PDF file.");
                if (!string.IsNullOrWhiteSpace(model.ExternalUrl))
                    ModelState.AddModelError(nameof(model.ExternalUrl), "A PDF material cannot also have a link.");
            }
            else if (string.Equals(model.ResourceType, "Link", StringComparison.OrdinalIgnoreCase))
            {
                if (model.File is not null)
                    ModelState.AddModelError(nameof(model.File), "A link material cannot also include a file.");
                if (!Uri.TryCreate(model.ExternalUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                    ModelState.AddModelError(nameof(model.ExternalUrl), "Enter a valid HTTP or HTTPS link.");
            }
            else
            {
                ModelState.AddModelError(nameof(model.ResourceType), "Choose PDF or link.");
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                    .SelectMany(value => value.Errors)
                    .Select(error => error.ErrorMessage));
                return RedirectToAction(nameof(BatchLearning), new { batchId = model.BatchId });
            }

            if (string.Equals(model.ResourceType, "Pdf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    upload = await _fileStorage.SavePdfMaterialAsync(model.File!, HttpContext.RequestAborted);
                }
                catch (InvalidUploadException exception)
                {
                    TempData["ErrorMessage"] = exception.Message;
                    return RedirectToAction(nameof(BatchLearning), new { batchId = model.BatchId });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError(exception, "Could not store study material for batch {BatchId}.", model.BatchId);
                    TempData["ErrorMessage"] = "The study material could not be saved. Try again.";
                    return RedirectToAction(nameof(BatchLearning), new { batchId = model.BatchId });
                }
            }

            var material = new StudyMaterial
            {
                BatchId = batch.Id,
                CreatedByStaffUserId = userId,
                Title = model.Title.Trim(),
                Description = model.Description?.Trim(),
                ResourceType = model.ResourceType,
                ExternalUrl = string.Equals(model.ResourceType, "Link", StringComparison.OrdinalIgnoreCase)
                    ? model.ExternalUrl!.Trim()
                    : null,
                StoredFileName = upload?.StoredFileName,
                OriginalFileName = upload?.OriginalFileName,
                ContentType = upload?.ContentType,
                CreatedUtc = DateTime.UtcNow
            };
            _db.StudyMaterials.Add(material);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Could not save study material metadata for batch {BatchId}.", batch.Id);
                if (upload is not null)
                    await TryDeleteStoredFileAsync(upload.StoredFileName);
                TempData["ErrorMessage"] = "The study material could not be saved. Try again.";
                return RedirectToAction(nameof(BatchLearning), new { batchId = batch.Id });
            }

            TempData["Message"] = "Study material added for this batch.";
            return RedirectToAction(nameof(BatchLearning), new { batchId = batch.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAssignment(CreateAssignmentViewModel model)
        {
            var batch = await GetAssignedBatches(User.FindFirstValue(ClaimTypes.NameIdentifier))
                .SingleOrDefaultAsync(item => item.Id == model.BatchId);
            if (batch is null)
                return NotFound();
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Enter a title, instructions, and a valid maximum score.";
                return RedirectToAction(nameof(BatchLearning), new { batchId = model.BatchId });
            }

            _db.Assignments.Add(new Assignment
            {
                BatchId = batch.Id,
                CreatedByStaffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                Title = model.Title.Trim(),
                Description = model.Description.Trim(),
                MaxScore = model.MaxScore,
                CreatedUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Message"] = "Assignment posted for this batch.";
            return RedirectToAction(nameof(BatchLearning), new { batchId = batch.Id });
        }

        [HttpGet]
        public async Task<IActionResult> AssignmentSubmissions(int assignmentId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var assignment = await _db.Assignments
                .Where(item => item.Id == assignmentId && item.Batch.StaffUserId == userId)
                .Select(item => new GradeSubmissionsViewModel
                {
                    AssignmentId = item.Id,
                    AssignmentTitle = item.Title,
                    MaxScore = item.MaxScore,
                    Submissions = item.Submissions
                        .OrderBy(submission => submission.SubmittedUtc)
                        .Select(submission => new GradeSubmissionEntryViewModel
                        {
                            Id = submission.Id,
                            StudentName = submission.Student.FullName,
                            OriginalFileName = submission.OriginalFileName,
                            Comments = submission.Comments,
                            SubmittedUtc = submission.SubmittedUtc,
                            Score = submission.Score,
                            Feedback = submission.Feedback,
                            IsGraded = submission.GradedUtc != null
                        })
                        .ToList()
                })
                .SingleOrDefaultAsync();
            return assignment is null ? NotFound() : View(assignment);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadStudyMaterial(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var material = await _db.StudyMaterials
                .Where(item => item.Id == id && item.Batch.StaffUserId == userId)
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
        public async Task<IActionResult> DownloadSubmission(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var submission = await _db.AssignmentSubmissions
                .Where(item => item.Id == id && item.Assignment.Batch.StaffUserId == userId)
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
                _logger.LogError(exception, "Could not read assignment submission {SubmissionId}.", id);
                return Problem("The assignment file could not be downloaded.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GradeSubmission(GradeAssignmentSubmissionViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var submission = await _db.AssignmentSubmissions
                .Include(item => item.Assignment)
                .ThenInclude(assignment => assignment.Batch)
                .SingleOrDefaultAsync(item =>
                    item.Id == model.SubmissionId &&
                    item.Assignment.Batch.StaffUserId == userId);
            if (submission is null)
                return NotFound();
            if (!ModelState.IsValid || !model.Score.HasValue || model.Score.Value > submission.Assignment.MaxScore)
            {
                TempData["ErrorMessage"] = $"Score must be between 0 and {submission.Assignment.MaxScore}.";
                return RedirectToAction(nameof(AssignmentSubmissions), new { assignmentId = submission.AssignmentId });
            }

            submission.Score = model.Score.Value;
            submission.Feedback = model.Feedback?.Trim();
            submission.GradedByStaffUserId = userId;
            submission.GradedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Message"] = "Submission graded.";
            return RedirectToAction(nameof(AssignmentSubmissions), new { assignmentId = submission.AssignmentId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBatchAnnouncement(CreateBatchAnnouncementViewModel model)
        {
            var batch = await GetAssignedBatches(User.FindFirstValue(ClaimTypes.NameIdentifier))
                .SingleOrDefaultAsync(item => item.Id == model.BatchId);
            if (batch is null)
                return NotFound();
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Enter an announcement title and message.";
                return RedirectToAction(nameof(BatchLearning), new { batchId = model.BatchId });
            }

            _db.Announcements.Add(new Announcement
            {
                BatchId = batch.Id,
                CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                Title = model.Title.Trim(),
                Body = model.Body.Trim(),
                CreatedUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Message"] = "Announcement posted for this batch.";
            return RedirectToAction(nameof(BatchLearning), new { batchId = batch.Id });
        }

    [HttpGet]
    public async Task<IActionResult> ScheduleClass(int batchId)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddHours(1), _coachingTimeZone);
        var batch = await GetAssignedBatches(User.FindFirstValue(ClaimTypes.NameIdentifier))
            .Include(item => item.Course)
            .SingleOrDefaultAsync(item => item.Id == batchId);
        if (batch is null)
            return NotFound();

        var model = new CreateScheduledClassViewModel
        {
            BatchId = batch.Id,
            BatchName = batch.Name,
            CourseName = batch.Course.Name,
            TimeZoneId = _coachingTimeZone.Id,
            ClassDate = localNow.Date,
            ClassTime = localNow.TimeOfDay
        };
        await PopulateScheduledClassesAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduleClass(CreateScheduledClassViewModel model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var batch = await GetAssignedBatches(userId)
            .Include(item => item.Course)
            .SingleOrDefaultAsync(item => item.Id == model.BatchId);
        if (batch is null)
            return NotFound();

        if (!model.ClassTime.HasValue ||
            model.ClassTime.Value < TimeSpan.Zero ||
            model.ClassTime.Value >= TimeSpan.FromDays(1))
            ModelState.AddModelError(nameof(model.ClassTime), "Enter a valid class time.");

        if (!model.ClassDate.HasValue)
            ModelState.AddModelError(nameof(model.ClassDate), "Enter a class date.");
        else if (model.ClassDate.Value.Date < batch.StartDate.Date || model.ClassDate.Value.Date > batch.EndDate.Date)
            ModelState.AddModelError(nameof(model.ClassDate), "The class date must be within the batch dates.");

        var localStartTime = DateTime.MinValue;
        if (model.ClassDate.HasValue &&
            model.ClassTime.HasValue &&
            model.ClassTime.Value >= TimeSpan.Zero &&
            model.ClassTime.Value < TimeSpan.FromDays(1))
        {
            localStartTime = model.ClassDate.Value.Date.Add(model.ClassTime.Value);
            if (_coachingTimeZone.IsInvalidTime(localStartTime))
            {
                ModelState.AddModelError(nameof(model.ClassTime), "This time does not exist because of a clock change. Choose another time.");
            }
            else if (_coachingTimeZone.IsAmbiguousTime(localStartTime))
            {
                ModelState.AddModelError(nameof(model.ClassTime), "This time occurs twice because of a clock change. Choose another time.");
            }
        }

        if (!ModelState.IsValid)
        {
            model.BatchName = batch.Name;
            model.CourseName = batch.Course.Name;
            model.TimeZoneId = _coachingTimeZone.Id;
            await PopulateScheduledClassesAsync(model);
            return View(model);
        }

        var startTimeUtc = TimeZoneInfo.ConvertTimeToUtc(localStartTime, _coachingTimeZone);
        if (startTimeUtc <= DateTime.UtcNow)
        {
            ModelState.AddModelError(nameof(model.ClassDate), "Schedule the class for a future date and time.");
            model.BatchName = batch.Name;
            model.CourseName = batch.Course.Name;
            model.TimeZoneId = _coachingTimeZone.Id;
            await PopulateScheduledClassesAsync(model);
            return View(model);
        }

        ZoomMeeting meeting;
        try
        {
            meeting = await _zoomMeetingService.CreateMeetingAsync(
                model.Title.Trim(),
                startTimeUtc,
                model.DurationMinutes,
                _coachingTimeZone.Id,
                HttpContext.RequestAborted);
        }
        catch (ZoomMeetingException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            model.BatchName = batch.Name;
            model.CourseName = batch.Course.Name;
            model.TimeZoneId = _coachingTimeZone.Id;
            await PopulateScheduledClassesAsync(model);
            return View(model);
        }

        if (!Uri.TryCreate(meeting.JoinUrl, UriKind.Absolute, out var joinUri) ||
            joinUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(joinUri.Host, "zoom.us", StringComparison.OrdinalIgnoreCase) &&
            !joinUri.Host.EndsWith(".zoom.us", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Zoom returned a join URL with an unexpected host for meeting {MeetingId}.",
                meeting.MeetingId);
            await TryDeleteZoomMeetingAsync(meeting.MeetingId);
            ModelState.AddModelError(string.Empty, "Zoom returned an invalid class join link. No class was saved.");
            model.BatchName = batch.Name;
            model.CourseName = batch.Course.Name;
            model.TimeZoneId = _coachingTimeZone.Id;
            await PopulateScheduledClassesAsync(model);
            return View(model);
        }

        var scheduledClass = new ScheduledClass
        {
            BatchId = batch.Id,
            CreatedByStaffUserId = userId!,
            Title = model.Title.Trim(),
            StartTimeUtc = startTimeUtc,
            TimeZoneId = _coachingTimeZone.Id,
            DurationMinutes = model.DurationMinutes,
            ZoomMeetingId = meeting.MeetingId,
            JoinUrl = meeting.JoinUrl,
            CreatedUtc = DateTime.UtcNow
        };
        _db.ScheduledClasses.Add(scheduledClass);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "Could not save scheduled class for batch {BatchId}; attempting to remove Zoom meeting {MeetingId}.",
                batch.Id,
                meeting.MeetingId);
            _db.Entry(scheduledClass).State = EntityState.Detached;
            await TryDeleteZoomMeetingAsync(meeting.MeetingId);
            ModelState.AddModelError(string.Empty, "Zoom created the meeting, but saving the class failed. Please try again.");
            model.BatchName = batch.Name;
            model.CourseName = batch.Course.Name;
            model.TimeZoneId = _coachingTimeZone.Id;
            await PopulateScheduledClassesAsync(model);
            return View(model);
        }

        TempData["Message"] = "Zoom class created and added to enrolled students' dashboards.";
        return RedirectToAction(nameof(Index));
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

    private async Task<StaffBatchLearningViewModel> BuildBatchLearningViewModelAsync(Batch batch)
    {
        return new StaffBatchLearningViewModel
        {
            BatchId = batch.Id,
            BatchName = batch.Name,
            CourseName = batch.Course.Name,
            Materials = await _db.StudyMaterials
                .Where(material => material.BatchId == batch.Id)
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
                .ToListAsync(),
            Assignments = await _db.Assignments
                .Where(assignment => assignment.BatchId == batch.Id)
                .OrderByDescending(assignment => assignment.CreatedUtc)
                .Select(assignment => new AssignmentSummaryViewModel
                {
                    Id = assignment.Id,
                    Title = assignment.Title,
                    Description = assignment.Description,
                    MaxScore = assignment.MaxScore,
                    CreatedUtc = assignment.CreatedUtc,
                    SubmissionCount = assignment.Submissions.Count
                })
                .ToListAsync()
        };
    }

    private async Task TryDeleteStoredFileAsync(string storedFileName)
    {
        try
        {
            await _fileStorage.DeleteAsync(storedFileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not remove orphaned upload {StoredFileName}.", storedFileName);
        }
    }

    private async Task PopulateScheduledClassesAsync(CreateScheduledClassViewModel model)
    {
        var now = DateTime.UtcNow;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var scheduledClasses = await _db.ScheduledClasses
            .Where(scheduledClass =>
                scheduledClass.BatchId == model.BatchId &&
                scheduledClass.StartTimeUtc.AddMinutes(scheduledClass.DurationMinutes) > now)
            .OrderBy(scheduledClass => scheduledClass.StartTimeUtc)
            .Select(scheduledClass => new ScheduledClassSummaryViewModel
            {
                Id = scheduledClass.Id,
                Title = scheduledClass.Title,
                BatchName = scheduledClass.Batch.Name,
                StartTimeLocal = scheduledClass.StartTimeUtc,
                DurationMinutes = scheduledClass.DurationMinutes,
                ZoomMeetingId = scheduledClass.ZoomMeetingId,
                IsUpcoming = scheduledClass.StartTimeUtc >= now,
                IsInProgress = scheduledClass.StartTimeUtc < now,
                CanDelete = scheduledClass.CreatedByStaffUserId == userId
            })
            .ToListAsync();

        foreach (var scheduledClass in scheduledClasses)
        {
            scheduledClass.StartTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(scheduledClass.StartTimeLocal, DateTimeKind.Utc),
                _coachingTimeZone);
        }
        model.ScheduledClasses = scheduledClasses;
    }

    private async Task TryDeleteZoomMeetingAsync(string meetingId)
    {
        try
        {
            await _zoomMeetingService.DeleteMeetingAsync(meetingId, CancellationToken.None);
        }
        catch (Exception exception) when (exception is ZoomMeetingException or HttpRequestException or OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Could not clean up orphaned Zoom meeting {MeetingId}.",
                meetingId);
        }
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
