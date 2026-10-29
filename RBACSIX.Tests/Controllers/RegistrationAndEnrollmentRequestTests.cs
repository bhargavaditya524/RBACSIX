using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
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
        var controller = CreateAccountController(null, userManager, db);

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
        var controller = CreateAccountController(new StubSignInManager(userManager), userManager, db);

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
    public async Task ForgotPassword_UsesGenericResponseAndEmailsExistingUnconfirmedAccount()
    {
        await using var db = CreateDbContext();
        var userManager = CreateUserManager(db);
        var user = new Users
        {
            UserName = "student@example.test",
            Email = "student@example.test",
            EmailConfirmed = false
        };
        Assert.True((await userManager.CreateAsync(user, "OldStrongPass1!")).Succeeded);
        var emailSender = new StubEmailSender();
        var controller = CreateAccountController(null, userManager, db, emailSender);

        var result = await controller.ForgotPassword(new ForgotPasswordViewModel
        {
            Email = "student@example.test"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.ForgotPasswordConfirmation), redirect.ActionName);
        var sentEmail = Assert.Single(emailSender.SentEmails);
        Assert.Equal("student@example.test", sentEmail.Email);
        Assert.Contains("ResetPassword", sentEmail.Body);
        Assert.Contains("https://portal.example.test", sentEmail.Body);
        Assert.DoesNotContain("attacker.example", sentEmail.Body);
    }

    [Fact]
    public async Task ForgotPassword_UnknownAccountGetsSameGenericConfirmationWithoutEmail()
    {
        await using var db = CreateDbContext();
        var emailSender = new StubEmailSender();
        var controller = CreateAccountController(null, CreateUserManager(db), db, emailSender);

        var result = await controller.ForgotPassword(new ForgotPasswordViewModel
        {
            Email = "missing@example.test"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.ForgotPasswordConfirmation), redirect.ActionName);
        Assert.Empty(emailSender.SentEmails);
    }

    [Fact]
    public async Task ForgotPassword_MissingPublicBaseUrlKeepsGenericResponseAndSkipsEmail()
    {
        await using var db = CreateDbContext();
        var userManager = CreateUserManager(db);
        var user = new Users
        {
            UserName = "student@example.test",
            Email = "student@example.test"
        };
        Assert.True((await userManager.CreateAsync(user, "OldStrongPass1!")).Succeeded);
        var emailSender = new StubEmailSender();
        var controller = CreateAccountController(
            null,
            userManager,
            db,
            emailSender,
            publicBaseUrl: null);

        var result = await controller.ForgotPassword(new ForgotPasswordViewModel
        {
            Email = "student@example.test"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.ForgotPasswordConfirmation), redirect.ActionName);
        Assert.Empty(emailSender.SentEmails);
    }

    [Fact]
    public void PasswordResetOptions_BindPublicBaseUrlFromConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PasswordReset:PublicBaseUrl"] = "https://localhost:7155"
            })
            .Build();

        var options = configuration.GetSection("PasswordReset").Get<PasswordResetOptions>();

        Assert.Equal("https://localhost:7155", options?.PublicBaseUrl);
    }

    [Fact]
    public async Task ForgotPassword_DoesNotExposeResetLinkOutsideDevelopment()
    {
        await using var db = CreateDbContext();
        var userManager = CreateUserManager(db);
        var user = new Users
        {
            UserName = "student@example.test",
            Email = "student@example.test"
        };
        Assert.True((await userManager.CreateAsync(user, "OldStrongPass1!")).Succeeded);
        var controller = CreateAccountController(
            null,
            userManager,
            db,
            environmentName: "Production");

        await controller.ForgotPassword(new ForgotPasswordViewModel
        {
            Email = "student@example.test"
        });
        controller.ForgotPasswordConfirmation();

        Assert.Null(controller.ViewData["DevelopmentResetUrl"]);
    }

    [Fact]
    public async Task ResetPassword_ValidTokenChangesPassword()
    {
        await using var db = CreateDbContext();
        var userManager = CreateUserManager(db);
        var user = new Users
        {
            UserName = "student@example.test",
            Email = "student@example.test"
        };
        Assert.True((await userManager.CreateAsync(user, "OldStrongPass1!")).Succeeded);
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var controller = CreateAccountController(null, userManager, db);

        var result = await controller.ResetPassword(new ResetPasswordViewModel
        {
            UserId = user.Id,
            Email = user.Email,
            Token = token,
            Password = "NewStrongPass2!",
            ConfirmPassword = "NewStrongPass2!"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.ResetPasswordConfirmation), redirect.ActionName);
        Assert.True((await userManager.CheckPasswordAsync(user, "NewStrongPass2!")));
    }

    [Fact]
    public async Task ResetPassword_InvalidTokenDoesNotChangePassword()
    {
        await using var db = CreateDbContext();
        var userManager = CreateUserManager(db);
        var user = new Users
        {
            UserName = "student@example.test",
            Email = "student@example.test"
        };
        Assert.True((await userManager.CreateAsync(user, "OldStrongPass1!")).Succeeded);
        var controller = CreateAccountController(null, userManager, db);

        var result = await controller.ResetPassword(new ResetPasswordViewModel
        {
            UserId = user.Id,
            Email = user.Email,
            Token = "not-a-valid-token",
            Password = "NewStrongPass2!",
            ConfirmPassword = "NewStrongPass2!"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.NotEmpty(view.ViewData.ModelState);
        Assert.True(await userManager.CheckPasswordAsync(user, "OldStrongPass1!"));
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
        var identityOptions = new IdentityOptions();
        identityOptions.Tokens.ProviderMap.Add(
            TokenOptions.DefaultProvider,
            new TokenProviderDescriptor(typeof(DataProtectorTokenProvider<Users>)));
        identityOptions.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
        var tokenServiceCollection = new ServiceCollection();
        tokenServiceCollection.AddDataProtection();
        tokenServiceCollection.AddLogging();
        tokenServiceCollection.AddOptions();
        tokenServiceCollection.AddTransient<DataProtectorTokenProvider<Users>>();
        var tokenServices = tokenServiceCollection.BuildServiceProvider();
        return new UserManager<Users>(
            new UserStore<Users, IdentityRole, AppDbContext>(db),
            Options.Create(identityOptions),
            new PasswordHasher<Users>(),
            [new UserValidator<Users>()],
            [new PasswordValidator<Users>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            tokenServices,
            NullLogger<UserManager<Users>>.Instance);
    }

    private static AccountController CreateAccountController(
        SignInManager<Users>? signInManager,
        UserManager<Users> userManager,
        AppDbContext db,
        StubEmailSender? emailSender = null,
        string environmentName = "Development",
        string? publicBaseUrl = "https://portal.example.test")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("attacker.example");
        var controller = new AccountController(
            signInManager!,
            userManager,
            db,
            emailSender ?? new StubEmailSender(),
            Options.Create(new PasswordResetOptions { PublicBaseUrl = publicBaseUrl }),
            new StubWebHostEnvironment(environmentName),
            NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor()
            },
            Url = new StubUrlHelper(),
            TempData = new TempDataDictionary(httpContext, new StubTempDataProvider())
        };
        return controller;
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

        public Task<string> GetMeetingHostStartUrlAsync(string meetingId, CancellationToken cancellationToken = default) =>
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

    private sealed class StubEmailSender : IPasswordResetEmailSender
    {
        public List<(string Email, string Subject, string Body)> SentEmails { get; } = [];

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            SentEmails.Add((email, subject, htmlMessage));
            return Task.CompletedTask;
        }
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } =
            new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

        public string? Action(UrlActionContext actionContext) =>
            $"{actionContext.Protocol}://{actionContext.Host}/Account/ResetPassword";

        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => url?.StartsWith('/') == true;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "RBACSIX.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
