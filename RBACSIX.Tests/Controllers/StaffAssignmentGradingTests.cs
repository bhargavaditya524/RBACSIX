using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RBACSIX.Controllers;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;
using RBACSIX.Services;

namespace RBACSIX.Tests.Controllers;

public class StaffAssignmentGradingTests
{
    [Fact]
    public async Task GradeSubmission_RejectsScoresOverAssignmentMaximum()
    {
        await using var db = CreateDbContext();
        var (submission, _) = await SeedSubmissionAsync(db);
        var controller = CreateController(db, "staff-a");

        var result = await controller.GradeSubmission(new GradeAssignmentSubmissionViewModel
        {
            SubmissionId = submission.Id,
            Score = 11,
            Feedback = "Good effort"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null((await db.AssignmentSubmissions.SingleAsync()).Score);
        Assert.Null((await db.AssignmentSubmissions.SingleAsync()).GradedUtc);
    }

    [Fact]
    public async Task GradeSubmission_RejectsMissingScore()
    {
        await using var db = CreateDbContext();
        var (submission, _) = await SeedSubmissionAsync(db);
        var controller = CreateController(db, "staff-a");

        var result = await controller.GradeSubmission(new GradeAssignmentSubmissionViewModel
        {
            SubmissionId = submission.Id,
            Score = null
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null((await db.AssignmentSubmissions.SingleAsync()).Score);
    }

    [Fact]
    public async Task GradeSubmission_AllowsAssignedStaffAndPersistsScoreAndFeedback()
    {
        await using var db = CreateDbContext();
        var (submission, _) = await SeedSubmissionAsync(db);
        var controller = CreateController(db, "staff-a");

        var result = await controller.GradeSubmission(new GradeAssignmentSubmissionViewModel
        {
            SubmissionId = submission.Id,
            Score = 9,
            Feedback = "Clear explanation."
        });

        Assert.IsType<RedirectToActionResult>(result);
        var graded = await db.AssignmentSubmissions.SingleAsync();
        Assert.Equal(9, graded.Score);
        Assert.Equal("Clear explanation.", graded.Feedback);
        Assert.NotNull(graded.GradedUtc);
        Assert.Equal("staff-a", graded.GradedByStaffUserId);
    }

    [Fact]
    public async Task GradeSubmission_RejectsStaffNotAssignedToBatch()
    {
        await using var db = CreateDbContext();
        var (submission, _) = await SeedSubmissionAsync(db);
        var controller = CreateController(db, "staff-b");

        var result = await controller.GradeSubmission(new GradeAssignmentSubmissionViewModel
        {
            SubmissionId = submission.Id,
            Score = 9
        });

        Assert.IsType<NotFoundResult>(result);
        Assert.Null((await db.AssignmentSubmissions.SingleAsync()).Score);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static async Task<(AssignmentSubmission Submission, StudentProfile Student)> SeedSubmissionAsync(AppDbContext db)
    {
        var staff = new Users { Id = "staff-a", UserName = "staff-a@example.test" };
        var studentUser = new Users { Id = "student-a", UserName = "student-a@example.test" };
        var student = new StudentProfile { UserId = studentUser.Id, User = studentUser, FullName = "Student A" };
        var batch = new Batch
        {
            Name = "Batch A",
            Course = new Course { Name = "Course" },
            StaffUser = staff,
            StaffUserId = staff.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };
        var assignment = new Assignment
        {
            Batch = batch,
            CreatedByStaffUserId = staff.Id,
            Title = "Exercise",
            Description = "Instructions",
            MaxScore = 10
        };
        var submission = new AssignmentSubmission
        {
            Assignment = assignment,
            Student = student,
            StoredFileName = $"{Guid.NewGuid():N}.pdf",
            OriginalFileName = "answer.pdf",
            ContentType = "application/pdf"
        };
        db.Enrollments.Add(new Enrollment
        {
            Student = student,
            Batch = batch,
            EnrolledOn = DateTime.Today
        });
        db.AssignmentSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return (submission, student);
    }

    private static StaffController CreateController(AppDbContext db, string userId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, "Staff")],
                "Test"))
        };
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

        public Task<string> GetMeetingHostStartUrlAsync(string meetingId, CancellationToken cancellationToken = default) =>
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
