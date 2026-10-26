using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RBACSIX.Authorization;
using RBACSIX.Controllers;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;
using RBACSIX.Services;

namespace RBACSIX.Tests.Controllers;

public class RegistrationAndEnrollmentRequestTests
{
    [Fact]
    public async Task StaffSelfRegistration_CreatesPendingProfileAndCannotSatisfyApprovedStaffPolicy()
    {
        await using var db = CreateDbContext();
        db.Roles.Add(new IdentityRole("Staff") { NormalizedName = "STAFF" });
        await db.SaveChangesAsync();
        var userManager = CreateUserManager(db);
        var controller = new AccountController(null!, userManager, db);

        var result = await controller.RegisterStaff(new StaffRegistrationViewModel
        {
            FullName = "New Staff",
            Email = "new-staff@example.test",
            Password = "StrongPass1!",
            ConfirmPassword = "StrongPass1!"
        });

        Assert.IsType<ViewResult>(result);
        var profile = await db.StaffProfiles.SingleAsync();
        Assert.False(profile.IsApproved);
        var user = await userManager.FindByIdAsync(profile.UserId);
        Assert.NotNull(user);
        Assert.Contains("Staff", await userManager.GetRolesAsync(user));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, profile.UserId),
            new Claim(ClaimTypes.Role, "Staff")
        ], "test"));
        var authContext = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext(
            [new ApprovedStaffRequirement()],
            principal,
            null);
        await new ApprovedStaffHandler(db).HandleAsync(authContext);
        Assert.False(authContext.HasSucceeded);

        var admin = CreateAdminController(db);
        Assert.IsType<RedirectToActionResult>(await admin.ApproveStaff(profile.UserId));
        Assert.True(profile.IsApproved);
        var approvedContext = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext(
            [new ApprovedStaffRequirement()],
            principal,
            null);
        await new ApprovedStaffHandler(db).HandleAsync(approvedContext);
        Assert.True(approvedContext.HasSucceeded);
    }

    [Fact]
    public async Task AdminCanCreateAdditionalAdminAccount()
    {
        await using var db = CreateDbContext();
        db.Roles.Add(new IdentityRole("Admin") { NormalizedName = "ADMIN" });
        await db.SaveChangesAsync();
        var userManager = CreateUserManager(db);
        var controller = CreateAdminController(db, userManager);

        var result = await controller.CreateAdmin(new CreateAdminViewModel
        {
            Email = "second-admin@example.test",
            Password = "StrongPass1!",
            ConfirmPassword = "StrongPass1!"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var user = await userManager.FindByEmailAsync("second-admin@example.test");
        Assert.NotNull(user);
        Assert.Contains("Admin", await userManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task StudentSelfRegistration_AssignsOnlyStudentRoleAndSignsIn()
    {
        await using var db = CreateDbContext();
        db.Roles.Add(new IdentityRole("Student") { NormalizedName = "STUDENT" });
        await db.SaveChangesAsync();
        var userManager = CreateUserManager(db);
        var controller = new AccountController(new StubSignInManager(userManager), userManager, db);

        var result = await controller.RegisterStudent(new StudentRegistrationViewModel
        {
            FullName = "New Student",
            Email = "new-student@example.test",
            Password = "StrongPass1!",
            ConfirmPassword = "StrongPass1!"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Student", redirect.ControllerName);
        var profile = await db.StudentProfiles.SingleAsync();
        var user = await userManager.FindByIdAsync(profile.UserId);
        Assert.NotNull(user);
        Assert.Equal(["Student"], await userManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task StudentCanCreateAndImmediatelyLinkParentAccount()
    {
        await using var db = CreateDbContext();
        db.Roles.Add(new IdentityRole("Parent") { NormalizedName = "PARENT" });
        var studentUser = new Users { Id = "student-user", UserName = "student@example.test" };
        var student = new StudentProfile
        {
            UserId = studentUser.Id,
            User = studentUser,
            FullName = "Student"
        };
        db.StudentProfiles.Add(student);
        await db.SaveChangesAsync();
        var controller = CreateStudentController(db, student.UserId);

        var result = await controller.RegisterParent(new RegisterParentViewModel
        {
            FullName = "Student Parent",
            Email = "parent@example.test",
            Password = "StrongPass1!",
            ConfirmPassword = "StrongPass1!"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var parent = await db.ParentProfiles.SingleAsync();
        var link = await db.ParentStudentLinks.SingleAsync();
        Assert.Equal(parent.UserId, link.ParentUserId);
        Assert.Equal(student.Id, link.StudentProfileId);
        var user = await CreateUserManager(db).FindByIdAsync(parent.UserId);
        Assert.NotNull(user);
        Assert.Equal(["Parent"], await CreateUserManager(db).GetRolesAsync(user));
    }

    [Fact]
    public async Task StudentCanRequestActiveBatchAndAdminRejectsRequestWhenCapacityIsFull()
    {
        await using var db = CreateDbContext();
        var student = new StudentProfile
        {
            User = new Users { Id = "student-user", UserName = "student@example.test" },
            UserId = "student-user",
            FullName = "Requesting Student"
        };
        var anotherStudent = new StudentProfile
        {
            User = new Users { Id = "another-user", UserName = "another@example.test" },
            UserId = "another-user",
            FullName = "Another Student"
        };
        var batch = new Batch
        {
            Name = "Full batch",
            Course = new Course { Name = "Active course", IsActive = true },
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(2),
            Capacity = 1
        };
        db.StudentProfiles.AddRange(student, anotherStudent);
        db.Enrollments.Add(new Enrollment { Student = anotherStudent, Batch = batch });
        await db.SaveChangesAsync();

        var studentController = CreateStudentController(db, student.UserId);
        var submitted = await studentController.RequestEnrollment(new CreateEnrollmentRequestViewModel
        {
            BatchId = batch.Id,
            Message = "Please add me."
        });
        Assert.IsType<RedirectToActionResult>(submitted);
        var request = await db.EnrollmentRequests.SingleAsync();
        Assert.Equal("Pending", request.Status);
        Assert.Equal(student.Id, request.StudentProfileId);

        var adminController = CreateAdminController(db);
        var reviewResult = await adminController.ReviewEnrollmentRequest(new ReviewEnrollmentRequestViewModel
        {
            RequestId = request.Id,
            Decision = "Approved"
        });

        Assert.IsType<RedirectToActionResult>(reviewResult);
        Assert.Equal("Rejected", request.Status);
        Assert.Contains("full", request.ReviewNote, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.Enrollments.CountAsync(enrollment => enrollment.BatchId == batch.Id));
    }

    [Fact]
    public async Task AdminApprovalCreatesEnrollmentWhenCapacityIsAvailable()
    {
        await using var db = CreateDbContext();
        var studentUser = new Users { Id = "student-user", UserName = "student@example.test" };
        var student = new StudentProfile { UserId = studentUser.Id, User = studentUser, FullName = "Student" };
        var batch = new Batch
        {
            Name = "Available batch",
            Course = new Course { Name = "Course", IsActive = true },
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1),
            Capacity = 2
        };
        db.StudentProfiles.Add(student);
        db.Batches.Add(batch);
        await db.SaveChangesAsync();
        var request = new EnrollmentRequest
        {
            StudentProfileId = student.Id,
            BatchId = batch.Id,
            Status = "Pending"
        };
        db.EnrollmentRequests.Add(request);
        await db.SaveChangesAsync();
        var controller = CreateAdminController(db);

        var result = await controller.ReviewEnrollmentRequest(new ReviewEnrollmentRequestViewModel
        {
            RequestId = request.Id,
            Decision = "Approved",
            ReviewNote = "Approved"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Approved", request.Status);
        Assert.True(await db.Enrollments.AnyAsync(enrollment =>
            enrollment.StudentProfileId == student.Id && enrollment.BatchId == batch.Id));
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
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

    private static StudentController CreateStudentController(AppDbContext db, string userId)
    {
        var httpContext = CreateHttpContext(userId, "Student");
        return new StudentController(
            db,
            CreateUserManager(db),
            TimeZoneInfo.Utc,
            new StubPrivateFileStorage(),
            NullLogger<StudentController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new StubTempDataProvider())
        };
    }

    private static AdminController CreateAdminController(AppDbContext db)
    {
        return CreateAdminController(db, CreateUserManager(db));
    }

    private static AdminController CreateAdminController(AppDbContext db, UserManager<Users> userManager)
    {
        var roleManager = new RoleManager<IdentityRole>(
            new RoleStore<IdentityRole, AppDbContext, string>(db),
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);
        var httpContext = CreateHttpContext("admin-user", "Admin");
        return new AdminController(
            db,
            userManager,
            roleManager,
            new StubZoomMeetingService(),
            NullLogger<AdminController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new StubTempDataProvider())
        };
    }

    private static DefaultHttpContext CreateHttpContext(string userId, string role)
    {
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            ], "test"))
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
    }

    private sealed class StubSignInManager : SignInManager<Users>
    {
        public StubSignInManager(UserManager<Users> userManager)
            : base(
                userManager,
                new HttpContextAccessor(),
                new StubClaimsPrincipalFactory(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<Users>>.Instance,
                new AuthenticationSchemeProvider(
                    Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())),
                new DefaultUserConfirmation<Users>())
        {
        }

        public override Task SignInAsync(Users user, bool isPersistent, string? authenticationMethod = null) =>
            Task.CompletedTask;
    }

    private sealed class StubClaimsPrincipalFactory : IUserClaimsPrincipalFactory<Users>
    {
        public Task<ClaimsPrincipal> CreateAsync(Users user) =>
            Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    private sealed class StubPrivateFileStorage : IPrivateFileStorage
    {
        public Task<StoredUpload> SavePdfMaterialAsync(IFormFile file, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<StoredUpload> SaveSubmissionAsync(IFormFile file, CancellationToken cancellationToken) =>
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
