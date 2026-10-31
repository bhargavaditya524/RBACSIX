using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Services;

namespace RBACSIX.Tests.Services;

public class CoachingAssistantContextTests
{
    [Fact]
    public async Task Staff_context_only_contains_students_from_assigned_batches()
    {
        await using var db = CreateDbContext();
        var staff = new Users { Id = "staff-1", UserName = "staff@test.local" };
        var otherStaff = new Users { Id = "staff-2", UserName = "other@test.local" };
        db.Users.AddRange(staff, otherStaff);
        db.StaffProfiles.AddRange(
            new StaffProfile { UserId = staff.Id, User = staff, FullName = "Assigned Coach" },
            new StaffProfile { UserId = otherStaff.Id, User = otherStaff, FullName = "Other Coach" });

        var assignedBatch = CreateBatch("Assigned batch", staff.Id);
        var otherBatch = CreateBatch("Private batch", otherStaff.Id);
        var assignedStudent = CreateStudent("Alice Example", "student-1");
        var privateStudent = CreateStudent("Secret Student", "student-2");
        var assignedEnrollment = new Enrollment
        {
            Student = assignedStudent,
            Batch = assignedBatch
        };
        var privateEnrollment = new Enrollment
        {
            Student = privateStudent,
            Batch = otherBatch
        };
        db.Enrollments.AddRange(assignedEnrollment, privateEnrollment);
        await db.SaveChangesAsync();
        db.ExamResults.AddRange(
            new ExamResult
            {
                EnrollmentId = assignedEnrollment.Id,
                ExamName = "Midterm",
                ExamDate = DateTime.Today,
                MarksObtained = 8,
                MaxMarks = 10,
                RecordedByUserId = staff.Id
            },
            new ExamResult
            {
                EnrollmentId = privateEnrollment.Id,
                ExamName = "Private exam",
                ExamDate = DateTime.Today,
                MarksObtained = 1,
                MaxMarks = 10,
                RecordedByUserId = otherStaff.Id
            });
        await db.SaveChangesAsync();

        var evidence = await CreateService(db).BuildAsync(
            CreatePrincipal(staff.Id, "Staff"),
            "What score did Alice Example get?",
            CancellationToken.None);
        var context = string.Join('\n', evidence.Select(item => item.Content));

        Assert.Contains("Alice Example", context);
        Assert.Contains("Midterm", context);
        Assert.DoesNotContain("Secret Student", context);
        Assert.DoesNotContain("Private exam", context);
    }

    [Fact]
    public async Task Student_context_contains_only_the_authenticated_students_enrollments()
    {
        await using var db = CreateDbContext();
        var studentUser = new Users { Id = "student-1", UserName = "student@test.local" };
        var otherUser = new Users { Id = "student-2", UserName = "other@test.local" };
        db.Users.AddRange(studentUser, otherUser);
        var student = CreateStudent("Current Student", studentUser.Id, studentUser);
        var otherStudent = CreateStudent("Other Student", otherUser.Id, otherUser);
        db.Enrollments.AddRange(
            new Enrollment { Student = student, Batch = CreateBatch("My batch") },
            new Enrollment { Student = otherStudent, Batch = CreateBatch("Other batch") });
        await db.SaveChangesAsync();

        var evidence = await CreateService(db).BuildAsync(
            CreatePrincipal(studentUser.Id, "Student"),
            "What are my batches?",
            CancellationToken.None);
        var context = string.Join('\n', evidence.Select(item => item.Content));

        Assert.Contains("My batch", context);
        Assert.DoesNotContain("Other Student", context);
        Assert.DoesNotContain("Other batch", context);
    }

    [Fact]
    public async Task Parent_context_contains_only_linked_children()
    {
        await using var db = CreateDbContext();
        var parentUser = new Users { Id = "parent-1", UserName = "parent@test.local" };
        var linkedUser = new Users { Id = "student-1", UserName = "linked@test.local" };
        var unrelatedUser = new Users { Id = "student-2", UserName = "unrelated@test.local" };
        db.Users.AddRange(parentUser, linkedUser, unrelatedUser);
        var parent = new ParentProfile
        {
            UserId = parentUser.Id,
            User = parentUser,
            FullName = "A Parent"
        };
        var linkedStudent = CreateStudent("Linked Child", linkedUser.Id, linkedUser);
        var unrelatedStudent = CreateStudent("Unrelated Child", unrelatedUser.Id, unrelatedUser);
        parent.StudentLinks.Add(new ParentStudentLink { Parent = parent, Student = linkedStudent });
        db.ParentProfiles.Add(parent);
        db.Enrollments.AddRange(
            new Enrollment { Student = linkedStudent, Batch = CreateBatch("Linked batch") },
            new Enrollment { Student = unrelatedStudent, Batch = CreateBatch("Unrelated batch") });
        await db.SaveChangesAsync();

        var evidence = await CreateService(db).BuildAsync(
            CreatePrincipal(parentUser.Id, "Parent"),
            "Show my children's results and fees",
            CancellationToken.None);
        var context = string.Join('\n', evidence.Select(item => item.Content));

        Assert.Contains("Linked Child", context);
        Assert.Contains("Linked batch", context);
        Assert.DoesNotContain("Unrelated Child", context);
        Assert.DoesNotContain("Unrelated batch", context);
    }

    [Fact]
    public async Task Admin_context_can_read_portal_leave_requests()
    {
        await using var db = CreateDbContext();
        var studentUser = new Users { Id = "student-1", UserName = "student@test.local" };
        db.Users.Add(studentUser);
        var student = CreateStudent("A Student", studentUser.Id, studentUser);
        var enrollment = new Enrollment
        {
            Student = student,
            Batch = CreateBatch("Morning batch")
        };
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync();
        db.LeaveRequests.Add(new LeaveRequest
        {
            EnrollmentId = enrollment.Id,
            RequestedByUserId = studentUser.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today,
            Reason = "Medical appointment",
            Status = "Approved"
        });
        await db.SaveChangesAsync();

        var evidence = await CreateService(db).BuildAsync(
            CreatePrincipal("admin-1", "Admin"),
            "Show leave requests",
            CancellationToken.None);
        var context = string.Join('\n', evidence.Select(item => item.Content));

        Assert.Contains("Medical appointment", context);
        Assert.Contains("Approved", context);
    }

    private static CoachingAssistantContextService CreateService(AppDbContext db) =>
        new(db);

    private static ClaimsPrincipal CreatePrincipal(string userId, string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        ], "test"));

    private static Batch CreateBatch(string name, string? staffUserId = null) =>
        new()
        {
            Name = name,
            StaffUserId = staffUserId,
            Course = new Course { Name = $"{name} course", DefaultFee = 100 },
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddMonths(1)
        };

    private static StudentProfile CreateStudent(
        string name,
        string userId,
        Users? user = null) =>
        new()
        {
            UserId = userId,
            User = user ?? new Users { Id = userId, UserName = $"{userId}@test.local" },
            FullName = name
        };

    private static AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
