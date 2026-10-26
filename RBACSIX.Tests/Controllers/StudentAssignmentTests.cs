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

public class StudentAssignmentTests
{
    [Fact]
    public async Task SubmitAssignment_AllowsReplacementUntilGradedThenLocksIt()
    {
        await using var db = CreateDbContext();
        var (student, assignment) = await SeedAssignmentAsync(db);
        var storage = new StubPrivateFileStorage();
        var controller = CreateController(db, storage, student.UserId);

        var firstResult = await controller.SubmitAssignment(new SubmitAssignmentViewModel
        {
            AssignmentId = assignment.Id,
            Comments = "First attempt",
            File = CreateFile("first.pdf")
        });
        Assert.IsType<RedirectToActionResult>(firstResult);

        var submission = await db.AssignmentSubmissions.SingleAsync();
        Assert.Equal("stored-1.pdf", submission.StoredFileName);
        Assert.Equal("First attempt", submission.Comments);

        var replacementResult = await controller.SubmitAssignment(new SubmitAssignmentViewModel
        {
            AssignmentId = assignment.Id,
            Comments = "Updated attempt",
            File = CreateFile("updated.pdf")
        });
        Assert.IsType<RedirectToActionResult>(replacementResult);
        submission = await db.AssignmentSubmissions.SingleAsync();
        Assert.Equal("stored-2.pdf", submission.StoredFileName);
        Assert.Equal("Updated attempt", submission.Comments);
        Assert.Contains("stored-1.pdf", storage.DeletedFiles);

        submission.Score = 8;
        submission.GradedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var savesBeforeLock = storage.SaveCount;
        var lockedResult = await controller.SubmitAssignment(new SubmitAssignmentViewModel
        {
            AssignmentId = assignment.Id,
            Comments = "Attempt after grading",
            File = CreateFile("latest.pdf")
        });

        Assert.IsType<RedirectToActionResult>(lockedResult);
        Assert.Equal(savesBeforeLock, storage.SaveCount);
        submission = await db.AssignmentSubmissions.SingleAsync();
        Assert.Equal("stored-2.pdf", submission.StoredFileName);
        Assert.Equal(8, submission.Score);
    }

    [Fact]
    public async Task SubmitAssignment_RejectsStudentNotEnrolledInTheAssignmentBatch()
    {
        await using var db = CreateDbContext();
        var (_, assignment) = await SeedAssignmentAsync(db);
        var otherUser = new Users { Id = "other-student", UserName = "other@example.test" };
        db.StudentProfiles.Add(new StudentProfile
        {
            UserId = otherUser.Id,
            User = otherUser,
            FullName = "Other Student"
        });
        await db.SaveChangesAsync();
        var storage = new StubPrivateFileStorage();
        var controller = CreateController(db, storage, otherUser.Id);

        var result = await controller.SubmitAssignment(new SubmitAssignmentViewModel
        {
            AssignmentId = assignment.Id,
            File = CreateFile("answer.pdf")
        });

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, storage.SaveCount);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
    }

    private static async Task<(StudentProfile Student, Assignment Assignment)> SeedAssignmentAsync(AppDbContext db)
    {
        var user = new Users { Id = "student-user", UserName = "student@example.test" };
        var student = new StudentProfile { UserId = user.Id, User = user, FullName = "Student" };
        var batch = new Batch
        {
            Name = "Batch A",
            Course = new Course { Name = "Course" },
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };
        var assignment = new Assignment
        {
            Batch = batch,
            CreatedByStaffUserId = "staff",
            Title = "Exercise",
            Description = "Submit the exercise.",
            MaxScore = 10
        };
        db.Enrollments.Add(new Enrollment
        {
            Student = student,
            Batch = batch,
            EnrolledOn = DateTime.Today
        });
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();
        return (student, assignment);
    }

    private static StudentController CreateController(AppDbContext db, StubPrivateFileStorage storage, string userId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, "Student")],
                "Test"))
        };
        return new StudentController(
            db,
            CreateUserManager(db),
            TimeZoneInfo.Utc,
            storage,
            NullLogger<StudentController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new StubTempDataProvider())
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

    private static IFormFile CreateFile(string name)
    {
        var stream = new MemoryStream("%PDF-1.7"u8.ToArray());
        return new FormFile(stream, 0, stream.Length, "File", name);
    }

    private sealed class StubPrivateFileStorage : IPrivateFileStorage
    {
        public int SaveCount { get; private set; }
        public List<string> DeletedFiles { get; } = new();

        public Task<StoredUpload> SavePdfMaterialAsync(IFormFile file, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredUpload> SaveSubmissionAsync(IFormFile file, CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.FromResult(new StoredUpload(
                $"stored-{SaveCount}.pdf",
                Path.GetFileName(file.FileName),
                "application/pdf"));
        }

        public FileStream OpenRead(string storedFileName) => throw new NotSupportedException();

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
        {
            DeletedFiles.Add(storedFileName);
            return Task.CompletedTask;
        }
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
