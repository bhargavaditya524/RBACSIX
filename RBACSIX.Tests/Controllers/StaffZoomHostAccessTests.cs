using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RBACSIX.Controllers;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Services;

namespace RBACSIX.Tests.Controllers;

public class StaffZoomHostAccessTests
{
    [Fact]
    public async Task JoinScheduledClass_RetrievesAndRedirectsToZoomHostLinkForCreator()
    {
        await using var db = CreateDbContext();
        var scheduledClass = await SeedScheduledClassAsync(db);
        var zoom = new StubZoomMeetingService("https://us02web.zoom.us/s/123456789?zak=host");
        var controller = CreateController(db, zoom, "staff-owner");

        var result = Assert.IsType<RedirectResult>(
            await controller.JoinScheduledClass(scheduledClass.Id));

        Assert.Equal("https://us02web.zoom.us/s/123456789?zak=host", result.Url);
        Assert.Equal("123456789", zoom.RequestedMeetingId);
    }

    [Fact]
    public async Task JoinScheduledClass_DeniesOtherStaffEvenWhenTheyAreAssignedToBatch()
    {
        await using var db = CreateDbContext();
        var scheduledClass = await SeedScheduledClassAsync(db);
        scheduledClass.Batch.StaffUserId = "staff-other";
        await db.SaveChangesAsync();
        var zoom = new StubZoomMeetingService("https://zoom.us/s/123456789?zak=host");

        var result = await CreateController(db, zoom, "staff-other")
            .JoinScheduledClass(scheduledClass.Id);

        Assert.IsType<NotFoundResult>(result);
        Assert.Null(zoom.RequestedMeetingId);
    }

    [Fact]
    public async Task JoinScheduledClass_RejectsExpiredMeetingsAndNonZoomRedirectTargets()
    {
        await using var db = CreateDbContext();
        var scheduledClass = await SeedScheduledClassAsync(db);
        scheduledClass.StartTimeUtc = DateTime.UtcNow.AddHours(-2);
        scheduledClass.DurationMinutes = 60;
        await db.SaveChangesAsync();
        var zoom = new StubZoomMeetingService("https://zoom.us/s/123456789?zak=host");

        var expiredResult = await CreateController(db, zoom, "staff-owner")
            .JoinScheduledClass(scheduledClass.Id);
        Assert.IsType<NotFoundResult>(expiredResult);
        Assert.Null(zoom.RequestedMeetingId);

        scheduledClass.StartTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        scheduledClass.DurationMinutes = 60;
        await db.SaveChangesAsync();
        zoom.HostStartUrl = "https://evil.example/redirect";

        var invalidUrlResult = await CreateController(db, zoom, "staff-owner")
            .JoinScheduledClass(scheduledClass.Id);
        Assert.IsType<ObjectResult>(invalidUrlResult);
        Assert.Equal(500, ((ObjectResult)invalidUrlResult).StatusCode);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static async Task<ScheduledClass> SeedScheduledClassAsync(AppDbContext db)
    {
        var staff = new Users { Id = "staff-owner", UserName = "staff@example.test" };
        var batch = new Batch
        {
            Name = "Batch",
            Course = new Course { Name = "Course" },
            StaffUser = staff,
            StaffUserId = staff.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };
        var scheduledClass = new ScheduledClass
        {
            Batch = batch,
            CreatedByStaffUserId = staff.Id,
            Title = "Zoom class",
            StartTimeUtc = DateTime.UtcNow.AddMinutes(5),
            TimeZoneId = "UTC",
            DurationMinutes = 60,
            ZoomMeetingId = "123456789",
            JoinUrl = "https://zoom.us/j/123456789"
        };
        db.ScheduledClasses.Add(scheduledClass);
        await db.SaveChangesAsync();
        return scheduledClass;
    }

    private static StaffController CreateController(AppDbContext db, StubZoomMeetingService zoom, string userId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, "Staff")],
                "Test"))
        };
        return new StaffController(
            db,
            zoom,
            new StubPrivateFileStorage(),
            TimeZoneInfo.Utc,
            NullLogger<StaffController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new StubTempDataProvider())
        };
    }

    private sealed class StubZoomMeetingService(string hostStartUrl) : IZoomMeetingService
    {
        public string HostStartUrl { get; set; } = hostStartUrl;
        public string? RequestedMeetingId { get; private set; }

        public Task<ZoomMeeting> CreateMeetingAsync(
            string title,
            DateTime startTimeUtc,
            int durationMinutes,
            string timeZoneId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteMeetingAsync(string meetingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GetMeetingHostStartUrlAsync(
            string meetingId,
            CancellationToken cancellationToken = default)
        {
            RequestedMeetingId = meetingId;
            return Task.FromResult(HostStartUrl);
        }
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
