using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RBACSIX.Models.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public class FirstAdminSetupViewModel
{
    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Phone]
    public string? PhoneNumber { get; set; }

    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class CreatePersonViewModel
{
    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Phone]
    public string? PhoneNumber { get; set; }

    [StringLength(120)]
    public string? GuardianName { get; set; }

    [StringLength(120)]
    public string? SubjectSpecialty { get; set; }
}

public class AdminDashboardViewModel
{
    public int StudentCount { get; set; }
    public int StaffCount { get; set; }
    public int CourseCount { get; set; }
    public int BatchCount { get; set; }
}

public class PersonSummaryViewModel
{
    public int ProfileId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Details { get; set; }
}

public class CourseViewModel
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}

public class BatchViewModel
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int CourseId { get; set; }

    public string? StaffUserId { get; set; }

    [Required, DataType(DataType.Date)]
    public DateTime StartDate { get; set; } = DateTime.Today;

    [Required, DataType(DataType.Date)]
    public DateTime EndDate { get; set; } = DateTime.Today.AddMonths(3);

    [StringLength(240)]
    public string? ScheduleDescription { get; set; }

    public List<SelectListItem> Courses { get; set; } = new();
    public List<SelectListItem> StaffMembers { get; set; } = new();
}

public class BatchSummaryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string? StaffName { get; set; }
    public string ScheduleDescription { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int StudentCount { get; set; }
}

public class EnrollStudentViewModel
{
    public int StudentProfileId { get; set; }
    public string StudentName { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int BatchId { get; set; }

    public List<SelectListItem> AvailableBatches { get; set; } = new();
    public List<EnrolledBatchViewModel> EnrolledBatches { get; set; } = new();
}

public class EnrolledBatchViewModel
{
    public int EnrollmentId { get; set; }
    public int BatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
}

public class MarkAttendanceViewModel
{
    public int BatchId { get; set; }

    [Required, DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    public string BatchName { get; set; } = string.Empty;
    public List<StudentAttendanceViewModel> Students { get; set; } = new();
}

public class StudentAttendanceViewModel
{
    public int EnrollmentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public bool IsPresent { get; set; }
    public string? Notes { get; set; }
}

public class RecordResultsViewModel
{
    public int BatchId { get; set; }

    [Required, StringLength(120)]
    public string ExamName { get; set; } = string.Empty;

    [Required, DataType(DataType.Date)]
    public DateTime ExamDate { get; set; } = DateTime.Today;

    [Range(0.01, 100000)]
    public decimal MaxMarks { get; set; } = 100;

    public string BatchName { get; set; } = string.Empty;
    public List<StudentResultEntryViewModel> Students { get; set; } = new();
}

public class StudentResultEntryViewModel
{
    public int EnrollmentId { get; set; }
    public string StudentName { get; set; } = string.Empty;

    [Range(0, 100000)]
    public decimal MarksObtained { get; set; }
}

public class StudentDashboardViewModel
{
    public string StudentName { get; set; } = string.Empty;
    public List<StudentBatchViewModel> Batches { get; set; } = new();
}

public class StudentBatchViewModel
{
    public string BatchName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string? StaffName { get; set; }
    public string? ScheduleDescription { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int PresentCount { get; set; }
    public int AttendanceCount { get; set; }
    public List<StudentResultSummaryViewModel> Results { get; set; } = new();
}

public class StudentResultSummaryViewModel
{
    public string ExamName { get; set; } = string.Empty;
    public DateTime ExamDate { get; set; }
    public decimal MarksObtained { get; set; }
    public decimal MaxMarks { get; set; }
}
