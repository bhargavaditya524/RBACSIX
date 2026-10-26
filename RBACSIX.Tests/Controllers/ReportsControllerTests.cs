using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Controllers;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;

namespace RBACSIX.Tests.Controllers;

public class ReportsControllerTests
{
    [Fact]
    public async Task StudentReportContainsOnlyTheSignedInStudentsBatchesAndFees()
    {
        await using var db = CreateDbContext();
        var (student, _, _, otherStudent) = await SeedReportsAsync(db);
        var controller = CreateController(db, student.UserId, "Student");

        var result = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<ReportsViewModel>(result.Model);

        Assert.Equal("Student", model.RoleName);
        Assert.Contains(model.Metrics, metric =>
            metric.Label == "Outstanding balance" && metric.Value.Contains("450", StringComparison.Ordinal));
        var attendance = Assert.Single(model.Charts, chart => chart.Title == "Attendance by batch");
        Assert.Equal("Student batch", Assert.Single(attendance.Points).Label);
        Assert.DoesNotContain(model.Charts.SelectMany(chart => chart.Points), point =>
            point.Label.Contains("Other", StringComparison.Ordinal));
        Assert.NotEqual(student.Id, otherStudent.Id);
    }

    [Fact]
    public async Task StaffReportIsRestrictedToAssignedBatches()
    {
        await using var db = CreateDbContext();
        var (_, staff, otherStaff, _) = await SeedReportsAsync(db);
        var controller = CreateController(db, staff.UserId, "Staff");

        var result = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<ReportsViewModel>(result.Model);

        Assert.Equal("Staff", model.RoleName);
        var attendance = Assert.Single(model.Charts, chart => chart.Title == "Attendance rate by assigned batch");
        Assert.Equal("Student batch", Assert.Single(attendance.Points).Label);
        Assert.DoesNotContain(attendance.Points, point =>
            point.Label.Contains(otherStaff.FullName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdminReportIncludesEnrollmentOverviewAcrossCourses()
    {
        await using var db = CreateDbContext();
        await SeedReportsAsync(db);
        var controller = CreateController(db, "admin-user", "Admin");

        var result = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<ReportsViewModel>(result.Model);

        Assert.Equal("Admin", model.RoleName);
        Assert.Contains(model.Metrics, metric => metric.Label == "Enrollments" && metric.Value == "2");
        var chart = Assert.Single(model.Charts, item => item.Title == "Enrollments by course");
        Assert.Equal(2m, chart.Points.Sum(point => point.Value));
    }

    [Fact]
    public async Task PendingStaffCannotOpenReports()
    {
        await using var db = CreateDbContext();
        var user = new Users { Id = "pending-staff", UserName = "pending@example.test" };
        db.StaffProfiles.Add(new StaffProfile
        {
            UserId = user.Id,
            User = user,
            FullName = "Pending Staff",
            IsApproved = false
        });
        await db.SaveChangesAsync();

        var result = await CreateController(db, user.Id, "Staff").Index();

        Assert.IsType<ForbidResult>(result);
    }

    private static async Task<(StudentProfile Student, StaffProfile Staff, StaffProfile OtherStaff, StudentProfile OtherStudent)> SeedReportsAsync(AppDbContext db)
    {
        var staffUser = new Users { Id = "staff-1", UserName = "staff@example.test" };
        var otherStaffUser = new Users { Id = "staff-2", UserName = "other-staff@example.test" };
        var studentUser = new Users { Id = "student-1", UserName = "student@example.test" };
        var otherStudentUser = new Users { Id = "student-2", UserName = "other-student@example.test" };
        var staff = new StaffProfile { UserId = staffUser.Id, User = staffUser, FullName = "Staff One", IsApproved = true };
        var otherStaff = new StaffProfile { UserId = otherStaffUser.Id, User = otherStaffUser, FullName = "Staff Two", IsApproved = true };
        var student = new StudentProfile { UserId = studentUser.Id, User = studentUser, FullName = "Student One" };
        var otherStudent = new StudentProfile { UserId = otherStudentUser.Id, User = otherStudentUser, FullName = "Student Two" };
        var course = new Course { Name = "Course", DefaultFee = 500m };
        var studentBatch = new Batch
        {
            Name = "Student batch",
            Course = course,
            StaffUserId = staff.UserId,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };
        var otherBatch = new Batch
        {
            Name = "Other batch",
            Course = new Course { Name = "Other course", DefaultFee = 900m },
            StaffUserId = otherStaff.UserId,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };
        var enrollment = new Enrollment { Student = student, Batch = studentBatch };
        var otherEnrollment = new Enrollment { Student = otherStudent, Batch = otherBatch };
        db.StaffProfiles.AddRange(staff, otherStaff);
        db.Enrollments.AddRange(enrollment, otherEnrollment);
        await db.SaveChangesAsync();
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EnrollmentId = enrollment.Id,
            Date = DateTime.Today.AddDays(-1),
            IsPresent = true,
            MarkedByUserId = staff.UserId
        });
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EnrollmentId = otherEnrollment.Id,
            Date = DateTime.Today.AddDays(-1),
            IsPresent = false,
            MarkedByUserId = otherStaff.UserId
        });
        db.EnrollmentCharges.Add(new EnrollmentCharge
        {
            EnrollmentId = enrollment.Id,
            Description = "Books",
            Amount = 50m,
            CreatedByAdminUserId = "admin-user"
        });
        db.EnrollmentPayments.Add(new EnrollmentPayment
        {
            EnrollmentId = enrollment.Id,
            Amount = 100m,
            ReceivedByAdminUserId = "admin-user"
        });
        db.ExamResults.Add(new ExamResult
        {
            EnrollmentId = enrollment.Id,
            ExamName = "Test",
            MarksObtained = 8,
            MaxMarks = 10,
            ExamDate = DateTime.Today,
            RecordedByUserId = staff.UserId
        });
        await db.SaveChangesAsync();
        return (student, staff, otherStaff, otherStudent);
    }

    private static ReportsController CreateController(AppDbContext db, string userId, string role)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            ], "test"))
        };
        return new ReportsController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }
}
