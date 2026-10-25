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

public class StaffLeaveRequestTests
{
    [Fact]
    public async Task ReviewLeaveRequest_ApprovesRequestForAssignedBatch()
    {
        await using var db = CreateDbContext();
        var staffUser = new Users { Id = "staff-1", UserName = "staff@test.local" };
        var studentUser = new Users { Id = "student-1", UserName = "student@test.local" };
        db.Users.AddRange(staffUser, studentUser);
        var batch = new Batch
        {
            Name = "Assigned batch",
            Course = new Course { Name = "Course" },
            StaffUserId = staffUser.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };
        var enrollment = new Enrollment
        {
            Student = new StudentProfile
            {
                UserId = studentUser.Id,
                User = studentUser,
                FullName = "Student"
            },
            Batch = batch
        };
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync();
        var request = new LeaveRequest
        {
            EnrollmentId = enrollment.Id,
            RequestedByUserId = studentUser.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(1),
            Reason = "Medical leave"
        };
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var controller = CreateController(db, staffUser.Id);
        var result = await controller.ReviewLeaveRequest(new ReviewLeaveRequestViewModel
        {
            LeaveRequestId = request.Id,
            Decision = "Approved",
            ReviewNote = "Approved"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Approved", (await db.LeaveRequests.SingleAsync()).Status);
        Assert.Equal(staffUser.Id, request.ReviewedByStaffUserId);
        Assert.NotNull(request.ReviewedUtc);
    }

    [Fact]
    public async Task ReviewLeaveRequest_CannotReviewAnotherStaffMembersBatch()
    {
        await using var db = CreateDbContext();
        var anotherStaffUser = new Users { Id = "staff-2", UserName = "other-staff@test.local" };
        var currentStaffUser = new Users { Id = "staff-1", UserName = "staff@test.local" };
        var studentUser = new Users { Id = "student-1", UserName = "student@test.local" };
        db.Users.AddRange(anotherStaffUser, currentStaffUser, studentUser);
        var batch = new Batch
        {
            Name = "Other batch",
            Course = new Course { Name = "Course" },
            StaffUserId = anotherStaffUser.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };
        var enrollment = new Enrollment
        {
            Student = new StudentProfile
            {
                UserId = studentUser.Id,
                User = studentUser,
                FullName = "Student"
            },
            Batch = batch
        };
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync();
        var request = new LeaveRequest
        {
            EnrollmentId = enrollment.Id,
            RequestedByUserId = studentUser.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today,
            Reason = "Medical leave"
        };
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var controller = CreateController(db, currentStaffUser.Id);
        var result = await controller.ReviewLeaveRequest(new ReviewLeaveRequestViewModel
        {
            LeaveRequestId = request.Id,
            Decision = "Approved"
        });

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal("Pending", request.Status);
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

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
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
