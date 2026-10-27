using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RBACSIX.Controllers;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;
using RBACSIX.Services;

namespace RBACSIX.Tests.Controllers;

public class QuizAndDoubtWorkflowTests
{
    [Fact]
    public async Task StartQuiz_OnlyAllowsEnrolledStudentAndReusesTheirSingleAttempt()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        var controller = CreateStudentController(db, seed.Student.UserId);

        var firstResult = await controller.StartQuiz(seed.Quiz.Id);
        var firstAttempt = await db.QuizAttempts.SingleAsync();
        var secondResult = await controller.StartQuiz(seed.Quiz.Id);

        Assert.IsType<RedirectToActionResult>(firstResult);
        Assert.IsType<RedirectToActionResult>(secondResult);
        Assert.Equal("TakeQuiz", ((RedirectToActionResult)firstResult).ActionName);
        Assert.Equal(firstAttempt.Id, ((RedirectToActionResult)secondResult).RouteValues!["attemptId"]);
        Assert.Equal(1, await db.QuizAttempts.CountAsync());

        var outsider = new Users { Id = "student-outsider", UserName = "outsider@example.test" };
        db.StudentProfiles.Add(new StudentProfile
        {
            UserId = outsider.Id,
            User = outsider,
            FullName = "Outsider"
        });
        await db.SaveChangesAsync();
        var denied = await CreateStudentController(db, outsider.Id).StartQuiz(seed.Quiz.Id);

        Assert.IsType<NotFoundResult>(denied);
        Assert.Equal(1, await db.QuizAttempts.CountAsync());
    }

    [Fact]
    public async Task SaveAndSubmitQuizAnswer_AutoGradesMultipleChoice()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        var controller = CreateStudentController(db, seed.Student.UserId);
        await controller.StartQuiz(seed.Quiz.Id);
        var attempt = await db.QuizAttempts.SingleAsync();
        var question = seed.Quiz.Questions.Single();
        var correctOption = question.Options.Single(option => option.IsCorrect);

        var saveResult = await controller.SaveQuizAnswer(new SaveQuizAnswerViewModel
        {
            AttemptId = attempt.Id,
            QuestionId = question.Id,
            SelectedOptionId = correctOption.Id
        });
        var submitResult = await controller.SubmitQuiz(attempt.Id);

        Assert.IsType<OkObjectResult>(saveResult);
        Assert.IsType<RedirectToActionResult>(submitResult);
        var savedAttempt = await db.QuizAttempts
            .Include(item => item.Answers)
            .SingleAsync();
        Assert.NotNull(savedAttempt.SubmittedUtc);
        Assert.Equal(2m, savedAttempt.Answers.Single().Score);
        Assert.Equal(2m, savedAttempt.TotalScore);
        Assert.NotNull(savedAttempt.GradedUtc);
    }

    [Fact]
    public async Task SaveQuizAnswer_UsesRedirectForNonAjaxForms()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        var controller = CreateStudentController(db, seed.Student.UserId);
        controller.Request.Headers.Remove("X-Requested-With");
        await controller.StartQuiz(seed.Quiz.Id);
        var attempt = await db.QuizAttempts.SingleAsync();
        var question = seed.Quiz.Questions.Single();

        var result = await controller.SaveQuizAnswer(new SaveQuizAnswerViewModel
        {
            AttemptId = attempt.Id,
            QuestionId = question.Id,
            SelectedOptionId = question.Options.First().Id
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("TakeQuiz", redirect.ActionName);
        Assert.Single(db.QuizAnswers);
    }

    [Fact]
    public async Task TakeQuiz_ReportsDatabaseExpiryAsUtcForBrowserCountdown()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        var controller = CreateStudentController(db, seed.Student.UserId);
        await controller.StartQuiz(seed.Quiz.Id);
        var attempt = await db.QuizAttempts.SingleAsync();
        attempt.ExpiresUtc = DateTime.SpecifyKind(attempt.ExpiresUtc, DateTimeKind.Unspecified);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = Assert.IsType<ViewResult>(await controller.TakeQuiz(attempt.Id));
        var model = Assert.IsType<TakeQuizViewModel>(result.Model);

        Assert.Equal(DateTimeKind.Utc, model.ExpiresUtc.Kind);
        Assert.True(model.ExpiresUtc > model.CurrentUtc);
        Assert.Equal(DateTimeKind.Utc, model.CurrentUtc.Kind);
    }

    [Fact]
    public async Task SaveQuizAnswer_RejectsOptionFromAnotherQuestion()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db, includeSecondQuestion: true);
        var controller = CreateStudentController(db, seed.Student.UserId);
        await controller.StartQuiz(seed.Quiz.Id);
        var attempt = await db.QuizAttempts.SingleAsync();
        var questions = seed.Quiz.Questions.OrderBy(question => question.Order).ToArray();
        var optionFromOtherQuestion = questions[1].Options.First();

        var result = await controller.SaveQuizAnswer(new SaveQuizAnswerViewModel
        {
            AttemptId = attempt.Id,
            QuestionId = questions[0].Id,
            SelectedOptionId = optionFromOtherQuestion.Id
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.QuizAnswers);
    }

    [Fact]
    public async Task AssignedStaffCanCreateQuestionsAndPublishQuiz()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        var staff = CreateStaffController(db, "staff-assigned");

        var createResult = await staff.CreateQuiz(new CreateQuizViewModel
        {
            BatchId = seed.Batch.Id,
            Title = "Draft quiz",
            DurationMinutes = 15
        });
        var draft = await db.Quizzes.SingleAsync(quiz => quiz.Title == "Draft quiz");
        var questionResult = await staff.AddQuizQuestion(new CreateQuizQuestionViewModel
        {
            QuizId = draft.Id,
            Prompt = "Choose one",
            QuestionType = QuizQuestionTypes.MultipleChoice,
            Marks = 3,
            OptionsPerLine = "First\nSecond",
            CorrectOptionNumber = 2
        });
        var publishResult = await staff.PublishQuiz(draft.Id);

        Assert.IsType<RedirectToActionResult>(createResult);
        Assert.IsType<RedirectToActionResult>(questionResult);
        Assert.IsType<RedirectToActionResult>(publishResult);
        var published = await db.Quizzes
            .Include(quiz => quiz.Questions)
                .ThenInclude(question => question.Options)
            .SingleAsync(quiz => quiz.Id == draft.Id);
        var question = Assert.Single(published.Questions);
        Assert.Equal(3m, question.Marks);
        Assert.Equal("Second", question.Options.Single(option => option.IsCorrect).Text);
        Assert.True(published.IsPublished);

        var otherStaff = await CreateStaffController(db, "staff-other")
            .CreateQuiz(new CreateQuizViewModel
            {
                BatchId = seed.Batch.Id,
                Title = "Unauthorized quiz",
                DurationMinutes = 10
            });
        Assert.IsType<NotFoundResult>(otherStaff);
    }

    [Fact]
    public async Task AssignedStaffCanGradeWrittenAnswerAndFinalizeScore()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        var writtenQuestion = new QuizQuestion
        {
            QuizId = seed.Quiz.Id,
            Prompt = "Explain your answer",
            QuestionType = QuizQuestionTypes.Written,
            Marks = 5,
            Order = 1
        };
        var attempt = new QuizAttempt
        {
            QuizId = seed.Quiz.Id,
            StudentProfileId = seed.Student.Id,
            StartedUtc = DateTime.UtcNow.AddMinutes(-5),
            ExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
            SubmittedUtc = DateTime.UtcNow,
            Answers =
            [
                new QuizAnswer
                {
                    Question = writtenQuestion,
                    WrittenResponse = "An explanation."
                }
            ]
        };
        db.QuizAttempts.Add(attempt);
        await db.SaveChangesAsync();
        var answer = await db.QuizAnswers.SingleAsync();

        var result = await CreateStaffController(db, "staff-assigned")
            .GradeQuizAnswer(new GradeQuizAnswerViewModel
            {
                AnswerId = answer.Id,
                Score = 4,
                Feedback = "Clear explanation."
            });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(4m, answer.Score);
        Assert.Equal("Clear explanation.", answer.Feedback);
        Assert.Equal(4m, attempt.TotalScore);
        Assert.NotNull(attempt.GradedUtc);

        var unauthorized = await CreateStaffController(db, "staff-other")
            .GradeQuizAnswer(new GradeQuizAnswerViewModel
            {
                AnswerId = answer.Id,
                Score = 4
            });
        Assert.IsType<NotFoundResult>(unauthorized);
    }

    [Fact]
    public async Task ExpiredAttempt_SubmitsSavedAnswersAtTheDeadline()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        var controller = CreateStudentController(db, seed.Student.UserId);
        await controller.StartQuiz(seed.Quiz.Id);
        var attempt = await db.QuizAttempts.SingleAsync();
        var question = seed.Quiz.Questions.Single();
        var correctOption = question.Options.Single(option => option.IsCorrect);
        db.QuizAnswers.Add(new QuizAnswer
        {
            QuizAttemptId = attempt.Id,
            QuizQuestionId = question.Id,
            SelectedOptionId = correctOption.Id
        });
        attempt.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var result = await controller.TakeQuiz(attempt.Id);
        var finalized = await db.QuizAttempts.SingleAsync();

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(attempt.ExpiresUtc, finalized.SubmittedUtc);
        Assert.Equal(2m, finalized.TotalScore);
    }

    [Fact]
    public async Task ExpirationProcessor_SubmitsExpiredAttemptsWithoutAStudentRequest()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        var controller = CreateStudentController(db, seed.Student.UserId);
        await controller.StartQuiz(seed.Quiz.Id);
        var attempt = await db.QuizAttempts.SingleAsync();
        var question = seed.Quiz.Questions.Single();
        var correctOption = question.Options.Single(option => option.IsCorrect);
        db.QuizAnswers.Add(new QuizAnswer
        {
            QuizAttemptId = attempt.Id,
            QuizQuestionId = question.Id,
            SelectedOptionId = correctOption.Id
        });
        attempt.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var finalizedCount = await QuizAttemptProcessing.FinalizeExpiredAttemptsAsync(db, DateTime.UtcNow);

        Assert.Equal(1, finalizedCount);
        Assert.NotNull(attempt.SubmittedUtc);
        Assert.Equal(2m, attempt.TotalScore);
    }

    [Fact]
    public async Task StudentDashboard_ShowsOnlyPublishedQuizzesAndTheStudentsOwnDoubts()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        db.StudentDoubts.Add(new StudentDoubt
        {
            BatchId = seed.Batch.Id,
            StudentProfileId = seed.Student.Id,
            Question = "My doubt",
            Solution = "My solution",
            AnsweredUtc = DateTime.UtcNow
        });
        var otherUser = new Users { Id = "another-student", UserName = "another@example.test" };
        var otherStudent = new StudentProfile
        {
            UserId = otherUser.Id,
            User = otherUser,
            FullName = "Another Student"
        };
        db.Enrollments.Add(new Enrollment
        {
            Student = otherStudent,
            Batch = seed.Batch,
            EnrolledOn = DateTime.Today
        });
        db.StudentDoubts.Add(new StudentDoubt
        {
            BatchId = seed.Batch.Id,
            Student = otherStudent,
            Question = "Another student's private doubt"
        });
        db.Quizzes.Add(new Quiz
        {
            BatchId = seed.Batch.Id,
            CreatedByStaffUserId = "staff-assigned",
            Title = "Draft quiz",
            DurationMinutes = 10,
            IsPublished = false
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var controller = CreateStudentController(db, seed.Student.UserId);
        var dashboard = Assert.IsType<ViewResult>(await controller.Index());
        var dashboardModel = Assert.IsType<StudentDashboardViewModel>(dashboard.Model);
        var batch = Assert.Single(dashboardModel.Batches);
        Assert.Equal("Topic quiz", Assert.Single(batch.Quizzes).Title);
        Assert.Equal("My doubt", Assert.Single(batch.Doubts).Question);

        var learning = Assert.IsType<ViewResult>(await controller.Learning());
        var learningModel = Assert.IsType<StudentLearningViewModel>(learning.Model);
        var learningBatch = Assert.Single(learningModel.Batches);
        Assert.Equal("Topic quiz", Assert.Single(learningBatch.Quizzes).Title);
        Assert.Equal("My doubt", Assert.Single(learningBatch.Doubts).Question);
    }

    [Fact]
    public async Task AskDoubt_RequiresEnrollmentAndStaffCanOnlyAnswerAssignedBatchDoubts()
    {
        await using var db = CreateDbContext();
        var seed = await SeedQuizAsync(db);
        var studentController = CreateStudentController(db, seed.Student.UserId);
        var submitResult = await studentController.AskDoubt(new SubmitStudentDoubtViewModel
        {
            BatchId = seed.Batch.Id,
            Question = "How does this topic work?"
        });
        var doubt = await db.StudentDoubts.SingleAsync();

        Assert.IsType<RedirectToActionResult>(submitResult);
        var staffController = CreateStaffController(db, "staff-assigned");
        var answerResult = await staffController.AnswerDoubt(new AnswerStudentDoubtViewModel
        {
            DoubtId = doubt.Id,
            Solution = "Start with the first principle."
        });
        Assert.IsType<RedirectToActionResult>(answerResult);
        Assert.Equal("Start with the first principle.", doubt.Solution);
        Assert.Equal("staff-assigned", doubt.AnsweredByStaffUserId);

        var otherStaffResult = await CreateStaffController(db, "staff-other")
            .AnswerDoubt(new AnswerStudentDoubtViewModel
            {
                DoubtId = doubt.Id,
                Solution = "Unauthorized answer"
            });
        Assert.IsType<NotFoundResult>(otherStaffResult);
        Assert.Equal("Start with the first principle.", doubt.Solution);

        var outsider = new Users { Id = "student-outsider", UserName = "outsider@example.test" };
        db.StudentProfiles.Add(new StudentProfile
        {
            UserId = outsider.Id,
            User = outsider,
            FullName = "Outsider"
        });
        await db.SaveChangesAsync();
        var denied = await CreateStudentController(db, outsider.Id).AskDoubt(new SubmitStudentDoubtViewModel
        {
            BatchId = seed.Batch.Id,
            Question = "Question from outside the batch"
        });
        Assert.IsType<NotFoundResult>(denied);
        Assert.Single(db.StudentDoubts);
    }

    [Fact]
    public void Submit_GradesChoiceQuestionsAndWaitsForWrittenAnswerGrading()
    {
        var choice = new QuizQuestion
        {
            Id = 1,
            QuizId = 1,
            Prompt = "Choose one",
            QuestionType = QuizQuestionTypes.MultipleChoice,
            Marks = 2,
            Options =
            [
                new QuizQuestionOption { Id = 11, Text = "Right", IsCorrect = true },
                new QuizQuestionOption { Id = 12, Text = "Wrong", IsCorrect = false }
            ]
        };
        var written = new QuizQuestion
        {
            Id = 2,
            QuizId = 1,
            Prompt = "Explain",
            QuestionType = QuizQuestionTypes.Written,
            Marks = 5
        };
        var attempt = new QuizAttempt
        {
            Quiz = new Quiz { Questions = [choice, written] },
            Answers =
            [
                new QuizAnswer { QuizQuestionId = choice.Id, SelectedOptionId = 11 },
                new QuizAnswer { QuizQuestionId = written.Id, WrittenResponse = "An explanation." }
            ]
        };

        QuizAttemptProcessing.Submit(attempt, DateTime.UtcNow);

        Assert.Equal(2m, attempt.Answers.Single(answer => answer.QuizQuestionId == choice.Id).Score);
        Assert.Null(attempt.TotalScore);
        Assert.Null(attempt.GradedUtc);

        attempt.Answers.Single(answer => answer.QuizQuestionId == written.Id).Score = 4;
        var gradedUtc = DateTime.UtcNow;
        QuizAttemptProcessing.RecalculateScore(attempt, gradedUtc);

        Assert.Equal(6m, attempt.TotalScore);
        Assert.Equal(gradedUtc, attempt.GradedUtc);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
    }

    private static async Task<QuizSeed> SeedQuizAsync(AppDbContext db, bool includeSecondQuestion = false)
    {
        var staff = new Users { Id = "staff-assigned", UserName = "staff@example.test" };
        var studentUser = new Users { Id = "student-user", UserName = "student@example.test" };
        var student = new StudentProfile
        {
            UserId = studentUser.Id,
            User = studentUser,
            FullName = "Student"
        };
        var batch = new Batch
        {
            Name = "Batch A",
            Course = new Course { Name = "Course A" },
            StaffUser = staff,
            StaffUserId = staff.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };
        var quiz = new Quiz
        {
            Batch = batch,
            CreatedByStaffUserId = staff.Id,
            Title = "Topic quiz",
            DurationMinutes = 20,
            IsPublished = true,
            Questions =
            [
                new QuizQuestion
                {
                    Prompt = "Select the correct answer",
                    QuestionType = QuizQuestionTypes.MultipleChoice,
                    Marks = 2,
                    Order = 0,
                    Options =
                    [
                        new QuizQuestionOption { Text = "Correct", Order = 0, IsCorrect = true },
                        new QuizQuestionOption { Text = "Incorrect", Order = 1, IsCorrect = false }
                    ]
                }
            ]
        };
        if (includeSecondQuestion)
        {
            quiz.Questions.Add(new QuizQuestion
            {
                Prompt = "Another choice",
                QuestionType = QuizQuestionTypes.MultipleChoice,
                Marks = 1,
                Order = 1,
                Options =
                [
                    new QuizQuestionOption { Text = "Option A", Order = 0 },
                    new QuizQuestionOption { Text = "Option B", Order = 1, IsCorrect = true }
                ]
            });
        }

        db.Enrollments.Add(new Enrollment
        {
            Student = student,
            Batch = batch,
            EnrolledOn = DateTime.Today
        });
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync();
        return new QuizSeed(student, batch, quiz);
    }

    private static StudentController CreateStudentController(AppDbContext db, string userId)
    {
        var context = CreateHttpContext(userId, "Student");
        context.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        return new StudentController(
            db,
            CreateUserManager(db),
            TimeZoneInfo.Utc,
            new StubPrivateFileStorage(),
            NullLogger<StudentController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new StubTempDataProvider())
        };
    }

    private static StaffController CreateStaffController(AppDbContext db, string userId)
    {
        var context = CreateHttpContext(userId, "Staff");
        return new StaffController(
            db,
            new StubZoomMeetingService(),
            new StubPrivateFileStorage(),
            TimeZoneInfo.Utc,
            NullLogger<StaffController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new StubTempDataProvider())
        };
    }

    private static DefaultHttpContext CreateHttpContext(string userId, string role)
    {
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)],
                "Test"))
        };
    }

    private static UserManager<Users> CreateUserManager(AppDbContext db)
    {
        return new UserManager<Users>(
            new UserStore<Users, IdentityRole, AppDbContext>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<Users>(),
            [new UserValidator<Users>()],
            [new PasswordValidator<Users>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<Users>>.Instance);
    }

    private sealed record QuizSeed(StudentProfile Student, Batch Batch, Quiz Quiz);

    private sealed class StubZoomMeetingService : IZoomMeetingService
    {
        public Task<ZoomMeeting> CreateMeetingAsync(
            string title,
            DateTime startTimeUtc,
            int durationMinutes,
            string timeZoneId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteMeetingAsync(string meetingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubPrivateFileStorage : IPrivateFileStorage
    {
        public Task<StoredUpload> SavePdfMaterialAsync(IFormFile file, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredUpload> SaveSubmissionAsync(IFormFile file, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredUpload> SaveQuizAnswerAsync(IFormFile file, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public FileStream OpenRead(string storedFileName) => throw new NotSupportedException();

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
