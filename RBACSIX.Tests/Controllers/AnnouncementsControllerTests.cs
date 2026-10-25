using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Controllers;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;

namespace RBACSIX.Tests.Controllers;

public class AnnouncementsControllerTests
{
    [Fact]
    public async Task Student_SeesGlobalAndEnrolledBatchAnnouncementsOnly()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedAnnouncementsAsync(db);
        var controller = CreateController(db, "student-a", "Student");

        var result = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<AnnouncementListViewModel>(result.Model);

        Assert.Equal(
            new[] { "Batch A notice", "Global notice" },
            model.Announcements.Select(item => item.Title).OrderBy(title => title).ToArray());
        Assert.False(model.CanCreateGlobal);
        Assert.DoesNotContain(model.Announcements, item => item.Id == seeded.BatchBAnnouncementId);
    }

    [Fact]
    public async Task Staff_SeesGlobalAndAssignedBatchAnnouncementsOnly()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedAnnouncementsAsync(db);
        var controller = CreateController(db, "staff-a", "Staff");

        var result = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<AnnouncementListViewModel>(result.Model);

        Assert.Equal(2, model.Announcements.Count);
        Assert.Contains(model.Announcements, item => item.Id == seeded.GlobalAnnouncementId);
        Assert.Contains(model.Announcements, item => item.Id == seeded.BatchAAnnouncementId);
        Assert.DoesNotContain(model.Announcements, item => item.Id == seeded.BatchBAnnouncementId);
    }

    [Fact]
    public async Task Admin_SeesAllAnnouncements()
    {
        await using var db = CreateDbContext();
        await SeedAnnouncementsAsync(db);
        var controller = CreateController(db, "admin", "Admin");

        var result = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<AnnouncementListViewModel>(result.Model);

        Assert.Equal(3, model.Announcements.Count);
        Assert.True(model.CanCreateGlobal);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static async Task<(int GlobalAnnouncementId, int BatchAAnnouncementId, int BatchBAnnouncementId)>
        SeedAnnouncementsAsync(AppDbContext db)
    {
        var staffA = new Users { Id = "staff-a", UserName = "staff-a@example.test" };
        var staffB = new Users { Id = "staff-b", UserName = "staff-b@example.test" };
        var studentUser = new Users { Id = "student-a", UserName = "student@example.test" };
        var student = new StudentProfile { UserId = studentUser.Id, User = studentUser, FullName = "Student A" };
        var course = new Course { Name = "Course" };
        var batchA = new Batch
        {
            Name = "Batch A", Course = course, StaffUserId = staffA.Id, StaffUser = staffA,
            StartDate = DateTime.Today, EndDate = DateTime.Today.AddMonths(1)
        };
        var batchB = new Batch
        {
            Name = "Batch B", Course = course, StaffUserId = staffB.Id, StaffUser = staffB,
            StartDate = DateTime.Today, EndDate = DateTime.Today.AddMonths(1)
        };
        db.Enrollments.Add(new Enrollment
        {
            Student = student,
            Batch = batchA,
            EnrolledOn = DateTime.Today
        });
        db.Announcements.AddRange(
            new Announcement { Title = "Global notice", Body = "All users", CreatedByUserId = "admin" },
            new Announcement { Title = "Batch A notice", Body = "Batch A users", CreatedByUserId = staffA.Id, Batch = batchA },
            new Announcement { Title = "Batch B notice", Body = "Batch B users", CreatedByUserId = staffB.Id, Batch = batchB });
        await db.SaveChangesAsync();
        return (
            await db.Announcements.Where(item => item.BatchId == null).Select(item => item.Id).SingleAsync(),
            await db.Announcements.Where(item => item.BatchId == batchA.Id).Select(item => item.Id).SingleAsync(),
            await db.Announcements.Where(item => item.BatchId == batchB.Id).Select(item => item.Id).SingleAsync());
    }

    private static AnnouncementsController CreateController(AppDbContext db, string userId, string role)
    {
        return new AnnouncementsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.NameIdentifier, userId),
                            new Claim(ClaimTypes.Role, role)
                        ],
                        "Test"))
                }
            }
        };
    }
}
