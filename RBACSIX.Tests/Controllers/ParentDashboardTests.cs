using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Controllers;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;

namespace RBACSIX.Tests.Controllers;

public class ParentDashboardTests
{
    [Fact]
    public async Task Dashboard_limits_data_to_linked_children_and_excludes_approved_leave_attendance()
    {
        await using var db = CreateDbContext();
        var parentUser = new Users { Id = "parent-1", UserName = "parent@test.local" };
        var linkedStudentUser = new Users { Id = "student-1", UserName = "student@test.local" };
        var unlinkedStudentUser = new Users { Id = "student-2", UserName = "other@test.local" };
        var parent = new ParentProfile { UserId = parentUser.Id, User = parentUser, FullName = "A Parent" };
        var linkedStudent = new StudentProfile
        {
            UserId = linkedStudentUser.Id,
            User = linkedStudentUser,
            FullName = "Linked Student"
        };
        var unlinkedStudent = new StudentProfile
        {
            UserId = unlinkedStudentUser.Id,
            User = unlinkedStudentUser,
            FullName = "Unlinked Student"
        };
        var batch = new Batch
        {
            Name = "Batch A",
            Course = new Course { Name = "Course A", DefaultFee = 500m },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31)
        };
        var enrollment = new Enrollment { Student = linkedStudent, Batch = batch };
        parent.StudentLinks.Add(new ParentStudentLink { Parent = parent, Student = linkedStudent });
        db.Users.AddRange(parentUser, linkedStudentUser, unlinkedStudentUser);
        db.ParentProfiles.Add(parent);
        db.StudentProfiles.AddRange(linkedStudent, unlinkedStudent);
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync();

        var attendedDate = new DateTime(2026, 2, 1);
        var leaveDate = new DateTime(2026, 2, 2);
        db.AttendanceRecords.AddRange(
            new AttendanceRecord
            {
                EnrollmentId = enrollment.Id,
                Date = attendedDate,
                IsPresent = true,
                MarkedByUserId = linkedStudentUser.Id
            },
            new AttendanceRecord
            {
                EnrollmentId = enrollment.Id,
                Date = leaveDate,
                IsPresent = false,
                MarkedByUserId = linkedStudentUser.Id
            });
        db.LeaveRequests.Add(new LeaveRequest
        {
            EnrollmentId = enrollment.Id,
            RequestedByUserId = parentUser.Id,
            StartDate = leaveDate,
            EndDate = leaveDate,
            Reason = "Medical appointment",
            Status = "Approved"
        });
        db.EnrollmentCharges.Add(new EnrollmentCharge
        {
            EnrollmentId = enrollment.Id,
            Description = "Books",
            Amount = 50m,
            CreatedByAdminUserId = "admin-1"
        });
        db.EnrollmentPayments.Add(new EnrollmentPayment
        {
            EnrollmentId = enrollment.Id,
            Amount = 100m,
            ReceivedByAdminUserId = "admin-1"
        });
        await db.SaveChangesAsync();

        var controller = new ParentController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, parentUser.Id) },
                        "test"))
                }
            }
        };

        var result = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<ParentDashboardViewModel>(result.Model);
        var child = Assert.Single(model.Children);
        Assert.Equal(linkedStudent.Id, child.StudentProfileId);
        var studentBatch = Assert.Single(child.Batches);
        Assert.Equal(1, studentBatch.PresentCount);
        Assert.Equal(1, studentBatch.AttendanceCount);
        Assert.Equal(500m, studentBatch.TuitionFee);
        Assert.Equal(50m, studentBatch.MiscellaneousCharges);
        Assert.Equal(100m, studentBatch.Payments);
        Assert.Equal(450m, studentBatch.Balance);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }
}
