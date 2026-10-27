using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RBACSIX.Controllers;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Services;

namespace RBACSIX.Tests.Services;

public class ScheduledClassCleanupServiceTests
{
    [Fact]
    public async Task CleanupAsync_DeletesExpiredClassesAndRetainsActiveClasses()
    {
        using var provider = CreateServiceProvider(out var databaseName, out var zoomService);
        await SeedClassesAsync(provider);
        using var cleanupService = CreateCleanupService(provider);

        await cleanupService.StartAsync(CancellationToken.None);
        var deletedMeetingId = await zoomService.DeleteRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return !await db.ScheduledClasses.AnyAsync(item => item.ZoomMeetingId == "expired-meeting");
        });
        await cleanupService.StopAsync(CancellationToken.None);

        Assert.Equal("expired-meeting", deletedMeetingId);
        await using var finalScope = provider.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await finalDb.ScheduledClasses.AnyAsync(item => item.ZoomMeetingId == "active-meeting"));
    }

    [Fact]
    public async Task CleanupAsync_RetainsExpiredClassWhenZoomCancellationFails()
    {
        using var provider = CreateServiceProvider(out var databaseName, out var zoomService);
        zoomService.FailDeletion = true;
        await SeedClassesAsync(provider);
        using var cleanupService = CreateCleanupService(provider);

        await cleanupService.StartAsync(CancellationToken.None);
        var attemptedMeetingId = await zoomService.DeleteRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cleanupService.StopAsync(CancellationToken.None);

        Assert.Equal("expired-meeting", attemptedMeetingId);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.ScheduledClasses.AnyAsync(item => item.ZoomMeetingId == "expired-meeting"));
    }

    [Fact]
    public void ExpirationFilter_TranslatesToSqlServerDateAdd()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=RBACSIXQueryTranslation;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new AppDbContext(options);
        var now = DateTime.UtcNow;

        var cleanupSql = db.ScheduledClasses
            .Where(item => item.StartTimeUtc.AddMinutes(item.DurationMinutes) <= now)
            .ToQueryString();
        var dashboardSql = db.Batches
            .Include(batch => batch.ScheduledClasses
                .Where(item => item.StartTimeUtc.AddMinutes(item.DurationMinutes) > now))
            .ToQueryString();

        Assert.Contains("DATEADD", cleanupSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DATEADD", dashboardSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAction_DoesNotCancelAnotherStaffMembersClass()
    {
        using var provider = CreateServiceProvider(out _, out var zoomService);
        await SeedClassesAsync(provider);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var classId = await db.ScheduledClasses
            .Where(item => item.ZoomMeetingId == "expired-meeting")
            .Select(item => item.Id)
            .SingleAsync();
        var controller = new StaffController(
            db,
            zoomService,
            new StubPrivateFileStorage(),
            TimeZoneInfo.Utc,
            NullLogger<StaffController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "different-staff-user")],
                        "Test"))
                }
            }
        };

        var result = await controller.DeleteScheduledClass(classId);

        Assert.IsType<NotFoundResult>(result);
        Assert.False(zoomService.DeleteRequested.Task.IsCompleted);
        Assert.True(await db.ScheduledClasses.AnyAsync(item => item.ZoomMeetingId == "expired-meeting"));
    }

    [Fact]
    public async Task DeleteAction_CancelsAndRemovesClassCreatedByCurrentStaff()
    {
        using var provider = CreateServiceProvider(out _, out var zoomService);
        await SeedClassesAsync(provider);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var classId = await db.ScheduledClasses
            .Where(item => item.ZoomMeetingId == "expired-meeting")
            .Select(item => item.Id)
            .SingleAsync();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "staff-user")],
                "Test"))
        };
        var controller = new StaffController(
            db,
            zoomService,
            new StubPrivateFileStorage(),
            TimeZoneInfo.Utc,
            NullLogger<StaffController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new StubTempDataProvider())
        };

        var result = await controller.DeleteScheduledClass(classId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("expired-meeting", await zoomService.DeleteRequested.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(await db.ScheduledClasses.AnyAsync(item => item.ZoomMeetingId == "expired-meeting"));
    }

    private static ServiceProvider CreateServiceProvider(
        out string databaseName,
        out StubZoomMeetingService zoomService)
    {
        databaseName = Guid.NewGuid().ToString();
        zoomService = new StubZoomMeetingService();
        var configuredDatabaseName = databaseName;
        var configuredZoomService = zoomService;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(configuredDatabaseName));
        services.AddScoped<IZoomMeetingService>(_ => configuredZoomService);
        return services.BuildServiceProvider();
    }

    private static ScheduledClassCleanupService CreateCleanupService(IServiceProvider provider)
    {
        return new ScheduledClassCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScheduledClassCleanupService>.Instance);
    }

    private static async Task SeedClassesAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var staff = new Users
        {
            Id = "staff-user",
            UserName = "staff@example.test",
            NormalizedUserName = "STAFF@EXAMPLE.TEST"
        };
        var course = new Course { Name = "Mathematics" };
        var batch = new Batch
        {
            Name = "Batch A",
            Course = course,
            StaffUser = staff,
            StaffUserId = staff.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };
        db.ScheduledClasses.AddRange(
            new ScheduledClass
            {
                Batch = batch,
                CreatedByStaffUser = staff,
                Title = "Expired class",
                StartTimeUtc = DateTime.UtcNow.AddHours(-1),
                DurationMinutes = 30,
                TimeZoneId = "UTC",
                ZoomMeetingId = "expired-meeting",
                JoinUrl = "https://zoom.us/j/expired"
            },
            new ScheduledClass
            {
                Batch = batch,
                CreatedByStaffUser = staff,
                Title = "Active class",
                StartTimeUtc = DateTime.UtcNow.AddMinutes(-10),
                DurationMinutes = 60,
                TimeZoneId = "UTC",
                ZoomMeetingId = "active-meeting",
                JoinUrl = "https://zoom.us/j/active"
            });
        await db.SaveChangesAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;

            await Task.Delay(25);
        }

        Assert.Fail("The scheduled class cleanup did not complete in time.");
    }

    private sealed class StubZoomMeetingService : IZoomMeetingService
    {
        public TaskCompletionSource<string> DeleteRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailDeletion { get; set; }

        public Task<ZoomMeeting> CreateMeetingAsync(
            string title,
            DateTime startTimeUtc,
            int durationMinutes,
            string timeZoneId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteMeetingAsync(string meetingId, CancellationToken cancellationToken = default)
        {
            DeleteRequested.TrySetResult(meetingId);
            if (FailDeletion)
                throw new ZoomMeetingException("Test Zoom deletion failure.");

            return Task.CompletedTask;
        }
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
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
}
