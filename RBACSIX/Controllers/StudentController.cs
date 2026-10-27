using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
                        BatchId = enrollment.BatchId,
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
                            .ToList(),
                        Quizzes = enrollment.Batch.Quizzes
                            .Where(quiz => quiz.IsPublished)
                            .OrderByDescending(quiz => quiz.CreatedUtc)
                            .Select(quiz => new StudentQuizSummaryViewModel
                            {
                                Id = quiz.Id,
                                Title = quiz.Title,
                                DurationMinutes = quiz.DurationMinutes,
                                QuestionCount = quiz.Questions.Count,
                                TotalMarks = quiz.Questions.Sum(question => (decimal?)question.Marks) ?? 0,
                                AttemptId = quiz.Attempts
                                    .Where(attempt => attempt.StudentProfileId == profile.Id)
                                    .Select(attempt => (int?)attempt.Id)
                                    .SingleOrDefault(),
                                IsSubmitted = quiz.Attempts.Any(attempt =>
                                    attempt.StudentProfileId == profile.Id &&
                                    attempt.SubmittedUtc != null),
                                ExpiresUtc = quiz.Attempts
                                    .Where(attempt => attempt.StudentProfileId == profile.Id)
                                    .Select(attempt => (DateTime?)attempt.ExpiresUtc)
                                    .SingleOrDefault(),
                                TotalScore = quiz.Attempts
                                    .Where(attempt => attempt.StudentProfileId == profile.Id)
                                    .Select(attempt => attempt.TotalScore)
                                    .SingleOrDefault(),
                                GradedUtc = quiz.Attempts
                                    .Where(attempt => attempt.StudentProfileId == profile.Id)
                                    .Select(attempt => attempt.GradedUtc)
                                    .SingleOrDefault()
                            })
                            .ToList(),
                        Doubts = enrollment.Batch.Doubts
                            .Where(doubt => doubt.StudentProfileId == profile.Id)
                            .OrderByDescending(doubt => doubt.CreatedUtc)
                            .Select(doubt => new StudentDoubtSummaryViewModel
                            {
                                Id = doubt.Id,
                                Question = doubt.Question,
                                CreatedUtc = doubt.CreatedUtc,
                                Solution = doubt.Solution,
                                AnsweredUtc = doubt.AnsweredUtc
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
            .Include(batch => batch.Quizzes.Where(quiz => quiz.IsPublished))
                .ThenInclude(quiz => quiz.Questions)
                    .ThenInclude(question => question.Options)
            .Include(batch => batch.Quizzes.Where(quiz => quiz.IsPublished))
                .ThenInclude(quiz => quiz.Attempts
                    .Where(attempt => attempt.StudentProfileId == profile.Id))
            .Include(batch => batch.Doubts
                .Where(doubt => doubt.StudentProfileId == profile.Id))
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
                    .ToList(),
                Quizzes = batch.Quizzes
                    .OrderByDescending(quiz => quiz.CreatedUtc)
                    .Select(quiz =>
                    {
                        var attempt = quiz.Attempts.SingleOrDefault();
                        return new StudentQuizSummaryViewModel
                        {
                            Id = quiz.Id,
                            Title = quiz.Title,
                            DurationMinutes = quiz.DurationMinutes,
                            QuestionCount = quiz.Questions.Count,
                            TotalMarks = quiz.Questions.Sum(question => question.Marks),
                            AttemptId = attempt?.Id,
                            IsSubmitted = attempt?.SubmittedUtc.HasValue == true,
                            ExpiresUtc = attempt?.ExpiresUtc,
                            TotalScore = attempt?.TotalScore,
                            GradedUtc = attempt?.GradedUtc
                        };
                    })
                    .ToList(),
                Doubts = batch.Doubts
                    .OrderByDescending(doubt => doubt.CreatedUtc)
                    .Select(doubt => new StudentDoubtSummaryViewModel
                    {
                        Id = doubt.Id,
                        Question = doubt.Question,
                        CreatedUtc = doubt.CreatedUtc,
                        Solution = doubt.Solution,
                        AnsweredUtc = doubt.AnsweredUtc
                    })
                    .ToList()
            }).ToList()
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartQuiz(int quizId)
    {
        var studentId = await GetStudentProfileIdAsync();
        if (studentId is null)
            return NotFound();

        var quiz = await _db.Quizzes
            .Where(item =>
                item.Id == quizId &&
                item.IsPublished &&
                item.Batch.Enrollments.Any(enrollment => enrollment.StudentProfileId == studentId))
            .Select(item => new
            {
                item.Id,
                item.DurationMinutes,
                QuestionCount = item.Questions.Count
            })
            .SingleOrDefaultAsync();
        if (quiz is null || quiz.QuestionCount == 0)
            return NotFound();

        var existingAttempt = await _db.QuizAttempts
            .SingleOrDefaultAsync(attempt =>
                attempt.QuizId == quiz.Id &&
                attempt.StudentProfileId == studentId);
        if (existingAttempt is not null)
        {
            return existingAttempt.SubmittedUtc.HasValue
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(TakeQuiz), new { attemptId = existingAttempt.Id });
        }

        var startedUtc = DateTime.UtcNow;
        var attempt = new QuizAttempt
        {
            QuizId = quiz.Id,
            StudentProfileId = studentId.Value,
            StartedUtc = startedUtc,
            LastSavedUtc = startedUtc,
            ExpiresUtc = startedUtc.AddMinutes(quiz.DurationMinutes)
        };
        _db.QuizAttempts.Add(attempt);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            _db.Entry(attempt).State = EntityState.Detached;
            var concurrentAttempt = await _db.QuizAttempts
                .SingleOrDefaultAsync(item =>
                    item.QuizId == quiz.Id &&
                    item.StudentProfileId == studentId);
            if (concurrentAttempt is not null)
            {
                return concurrentAttempt.SubmittedUtc.HasValue
                    ? RedirectToAction(nameof(Index))
                    : RedirectToAction(nameof(TakeQuiz), new { attemptId = concurrentAttempt.Id });
            }

            _logger.LogError(exception, "Could not start quiz {QuizId} for student {StudentProfileId}.", quiz.Id, studentId);
            return Problem("The quiz attempt could not be started.");
        }

        return RedirectToAction(nameof(TakeQuiz), new { attemptId = attempt.Id });
    }

    [HttpGet]
    public async Task<IActionResult> TakeQuiz(int attemptId)
    {
        var studentId = await GetStudentProfileIdAsync();
        if (studentId is null)
            return NotFound();

        var attempt = await _db.QuizAttempts
            .Include(item => item.Quiz)
                .ThenInclude(quiz => quiz.Questions)
                    .ThenInclude(question => question.Options)
            .Include(item => item.Answers)
            .SingleOrDefaultAsync(item =>
                item.Id == attemptId &&
                item.StudentProfileId == studentId &&
                item.Quiz.Batch.Enrollments.Any(enrollment => enrollment.StudentProfileId == studentId));
        if (attempt is null)
            return NotFound();
        if (attempt.SubmittedUtc.HasValue)
            return RedirectToAction(nameof(Index));
        if (attempt.ExpiresUtc <= DateTime.UtcNow)
        {
            QuizAttemptProcessing.Submit(attempt, attempt.ExpiresUtc);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["Message"] = "The quiz timer expired and your saved answers were submitted.";
                return RedirectToAction(nameof(Index));
            }
            TempData["Message"] = "The quiz timer expired. Your saved answers were submitted.";
            return RedirectToAction(nameof(Index));
        }

        var answersByQuestion = attempt.Answers.ToDictionary(answer => answer.QuizQuestionId);
        return View(new TakeQuizViewModel
        {
            AttemptId = attempt.Id,
            Title = attempt.Quiz.Title,
            Instructions = attempt.Quiz.Instructions,
            ExpiresUtc = DateTime.SpecifyKind(attempt.ExpiresUtc, DateTimeKind.Utc),
            CurrentUtc = DateTime.UtcNow,
            Questions = attempt.Quiz.Questions
                .OrderBy(question => question.Order)
                .Select(question =>
                {
                    answersByQuestion.TryGetValue(question.Id, out var answer);
                    return new TakeQuizQuestionViewModel
                    {
                        Id = question.Id,
                        AnswerId = answer?.Id,
                        Prompt = question.Prompt,
                        QuestionType = question.QuestionType,
                        Marks = question.Marks,
                        Options = question.Options
                            .OrderBy(option => option.Order)
                            .Select(option => new QuizOptionViewModel
                            {
                                Id = option.Id,
                                Text = option.Text
                            })
                            .ToList(),
                        SelectedOptionId = answer?.SelectedOptionId,
                        WrittenResponse = answer?.WrittenResponse,
                        OriginalFileName = answer?.OriginalFileName
                    };
                })
                .ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadQuizAnswer(int answerId)
    {
        var studentId = await GetStudentProfileIdAsync();
        if (studentId is null)
            return NotFound();

        var answer = await _db.QuizAnswers
            .Where(item =>
                item.Id == answerId &&
                item.Attempt.StudentProfileId == studentId &&
                item.Question.Quiz.Batch.Enrollments.Any(enrollment =>
                    enrollment.StudentProfileId == studentId))
            .Select(item => new { item.StoredFileName, item.OriginalFileName, item.ContentType })
            .SingleOrDefaultAsync();
        if (answer?.StoredFileName is null)
            return NotFound();

        try
        {
            return File(_fileStorage.OpenRead(answer.StoredFileName), answer.ContentType ?? "application/octet-stream",
                answer.OriginalFileName ?? "quiz-answer");
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not read quiz answer {AnswerId}.", answerId);
            return Problem("The quiz answer could not be downloaded.");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<IActionResult> SaveQuizAnswer(SaveQuizAnswerViewModel model)
    {
        if (!ModelState.IsValid)
            return QuizAnswerError(model.AttemptId, "The answer is invalid or too long.");

        var studentId = await GetStudentProfileIdAsync();
        if (studentId is null)
            return NotFound();

        var attempt = await _db.QuizAttempts
            .Include(item => item.Quiz)
                .ThenInclude(quiz => quiz.Questions)
                    .ThenInclude(question => question.Options)
            .Include(item => item.Answers)
            .SingleOrDefaultAsync(item =>
                item.Id == model.AttemptId &&
                item.StudentProfileId == studentId &&
                item.Quiz.Batch.Enrollments.Any(enrollment => enrollment.StudentProfileId == studentId));
        if (attempt is null)
            return NotFound();
        if (attempt.SubmittedUtc.HasValue)
            return QuizAnswerError(
                model.AttemptId,
                "This quiz attempt has already been submitted.",
                completed: true,
                statusCode: StatusCodes.Status409Conflict);

        var now = DateTime.UtcNow;
        if (attempt.ExpiresUtc <= now)
        {
            QuizAttemptProcessing.Submit(attempt, attempt.ExpiresUtc);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return QuizAnswerError(
                    model.AttemptId,
                    "The quiz timer expired. Your saved answers were submitted.",
                    completed: true,
                    statusCode: StatusCodes.Status409Conflict);
            }
            return QuizAnswerError(
                model.AttemptId,
                "The quiz timer expired. Your saved answers were submitted.",
                completed: true,
                statusCode: StatusCodes.Status409Conflict);
        }

        var question = attempt.Quiz.Questions.SingleOrDefault(item => item.Id == model.QuestionId);
        if (question is null)
            return NotFound();
        if (question.QuestionType == QuizQuestionTypes.MultipleChoice &&
            (model.File is not null ||
             !string.IsNullOrWhiteSpace(model.WrittenResponse) ||
             model.SelectedOptionId.HasValue &&
             !question.Options.Any(option => option.Id == model.SelectedOptionId.Value)))
            return QuizAnswerError(model.AttemptId, "Choose one of this question's options.");
        if (question.QuestionType == QuizQuestionTypes.Written &&
            (model.File is not null || model.SelectedOptionId.HasValue))
            return QuizAnswerError(model.AttemptId, "This question expects a written answer.");
        if (question.QuestionType == QuizQuestionTypes.FileUpload &&
            (model.SelectedOptionId.HasValue || !string.IsNullOrWhiteSpace(model.WrittenResponse)))
            return QuizAnswerError(model.AttemptId, "This question expects a PDF or image upload.");

        var answer = attempt.Answers.SingleOrDefault(item => item.QuizQuestionId == question.Id);
        if (question.QuestionType == QuizQuestionTypes.FileUpload &&
            model.File is null &&
            answer?.StoredFileName is null)
            return QuizAnswerError(model.AttemptId, "Select a PDF or image file to save this answer.");

        StoredUpload? upload = null;
        if (question.QuestionType == QuizQuestionTypes.FileUpload && model.File is not null)
        {
            try
            {
                upload = await _fileStorage.SaveQuizAnswerAsync(model.File, HttpContext.RequestAborted);
            }
            catch (InvalidUploadException exception)
            {
                return QuizAnswerError(model.AttemptId, exception.Message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(exception, "Could not store quiz answer for question {QuestionId}.", question.Id);
                return Problem("The quiz answer could not be saved.");
            }
        }

        var savedAtUtc = DateTime.UtcNow;
        if (attempt.ExpiresUtc <= savedAtUtc)
        {
            QuizAttemptProcessing.Submit(attempt, attempt.ExpiresUtc);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (upload is not null)
                    await TryDeleteStoredFileAsync(upload.StoredFileName);
                return QuizAnswerError(
                    model.AttemptId,
                    "The quiz timer expired. Your saved answers were submitted.",
                    completed: true,
                    statusCode: StatusCodes.Status409Conflict);
            }
            if (upload is not null)
                await TryDeleteStoredFileAsync(upload.StoredFileName);
            return QuizAnswerError(
                model.AttemptId,
                "The quiz timer expired. Your saved answers were submitted.",
                completed: true,
                statusCode: StatusCodes.Status409Conflict);
        }

        var oldFileName = answer?.StoredFileName;
        if (answer is null)
        {
            answer = new QuizAnswer
            {
                QuizAttemptId = attempt.Id,
                QuizQuestionId = question.Id
            };
            _db.QuizAnswers.Add(answer);
        }

        if (question.QuestionType == QuizQuestionTypes.MultipleChoice)
        {
            answer.SelectedOptionId = model.SelectedOptionId;
        }
        else if (question.QuestionType == QuizQuestionTypes.Written)
        {
            answer.WrittenResponse = model.WrittenResponse?.Trim();
        }
        else if (upload is not null)
        {
            answer.StoredFileName = upload.StoredFileName;
            answer.OriginalFileName = upload.OriginalFileName;
            answer.ContentType = upload.ContentType;
        }
        answer.AnsweredUtc = savedAtUtc;
        attempt.LastSavedUtc = savedAtUtc;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (upload is not null)
                await TryDeleteStoredFileAsync(upload.StoredFileName);
            return QuizAnswerError(
                model.AttemptId,
                "The quiz attempt has already been submitted.",
                completed: true,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception, "Could not save quiz answer for attempt {AttemptId}.", attempt.Id);
            if (upload is not null)
                await TryDeleteStoredFileAsync(upload.StoredFileName);
            return Problem("The quiz answer could not be saved.");
        }

        if (upload is not null &&
            oldFileName is not null &&
            !string.Equals(oldFileName, upload.StoredFileName, StringComparison.Ordinal) &&
            !await TryDeleteStoredFileAsync(oldFileName))
        {
            const string cleanupWarning =
                "Your new answer was saved, but the previous file could not be removed. Contact the coaching center.";
            if (IsAjaxRequest())
                return Ok(new { saved = true, warning = cleanupWarning });
            TempData["ErrorMessage"] = cleanupWarning;
            return RedirectToAction(nameof(TakeQuiz), new { attemptId = model.AttemptId });
        }

        if (IsAjaxRequest())
            return Ok(new { saved = true });
        TempData["Message"] = "Answer saved.";
        return RedirectToAction(nameof(TakeQuiz), new { attemptId = model.AttemptId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitQuiz(int attemptId)
    {
        var studentId = await GetStudentProfileIdAsync();
        if (studentId is null)
            return NotFound();

        var attempt = await _db.QuizAttempts
            .Include(item => item.Quiz)
                .ThenInclude(quiz => quiz.Questions)
                    .ThenInclude(question => question.Options)
            .Include(item => item.Answers)
            .SingleOrDefaultAsync(item =>
                item.Id == attemptId &&
                item.StudentProfileId == studentId &&
                item.Quiz.Batch.Enrollments.Any(enrollment => enrollment.StudentProfileId == studentId));
        if (attempt is null)
            return NotFound();
        if (attempt.SubmittedUtc.HasValue)
        {
            TempData["Message"] = "Your quiz was already submitted.";
            return RedirectToAction(nameof(Index));
        }

        var submittedUtc = DateTime.UtcNow;
        var timerExpired = attempt.ExpiresUtc <= submittedUtc;
        QuizAttemptProcessing.Submit(
            attempt,
            timerExpired ? attempt.ExpiresUtc : submittedUtc);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Message"] = "Your quiz was already submitted.";
            return RedirectToAction(nameof(Index));
        }
        TempData["Message"] = timerExpired
            ? "The quiz timer expired. Your saved answers were submitted."
            : "Quiz submitted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AskDoubt(SubmitStudentDoubtViewModel model)
    {
        var studentId = await GetStudentProfileIdAsync();
        if (studentId is null)
            return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Enter your question using 4,000 characters or fewer.";
            return RedirectToAction(nameof(Index));
        }

        var isEnrolled = await _db.Enrollments.AnyAsync(enrollment =>
            enrollment.BatchId == model.BatchId &&
            enrollment.StudentProfileId == studentId);
        if (!isEnrolled)
            return NotFound();

        _db.StudentDoubts.Add(new StudentDoubt
        {
            BatchId = model.BatchId,
            StudentProfileId = studentId.Value,
            Question = model.Question.Trim(),
            CreatedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Your doubt was sent to the staff member for this batch.";
        return RedirectToAction(nameof(Index));
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

    private IActionResult QuizAnswerError(
        int attemptId,
        string message,
        bool completed = false,
        int statusCode = StatusCodes.Status400BadRequest)
    {
        if (IsAjaxRequest())
            return statusCode == StatusCodes.Status400BadRequest
                ? BadRequest(new { message })
                : StatusCode(statusCode, new { message });
        TempData["ErrorMessage"] = message;
        return completed
            ? RedirectToAction(nameof(Index))
            : RedirectToAction(nameof(TakeQuiz), new { attemptId });
    }

    private bool IsAjaxRequest()
    {
        return string.Equals(
            Request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
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
