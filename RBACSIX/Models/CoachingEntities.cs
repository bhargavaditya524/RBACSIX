using System.ComponentModel.DataAnnotations;

namespace RBACSIX.Models;

public class StudentProfile
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public Users User { get; set; } = null!;

    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(120)]
    public string? GuardianName { get; set; }

    public DateTime EnrolledOn { get; set; } = DateTime.Today;
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public class StaffProfile
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public Users User { get; set; } = null!;

    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(120)]
    public string? SubjectSpecialty { get; set; }
}

public class Course
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
    public ICollection<Batch> Batches { get; set; } = new List<Batch>();
}

public class Batch
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public string? StaffUserId { get; set; }
    public Users? StaffUser { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    [StringLength(240)]
    public string? ScheduleDescription { get; set; }

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public class Enrollment
{
    public int Id { get; set; }

    [Required]
    public int StudentProfileId { get; set; }
    public StudentProfile Student { get; set; } = null!;

    [Required]
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    public DateTime EnrolledOn { get; set; } = DateTime.Today;
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<ExamResult> Results { get; set; } = new List<ExamResult>();
}

public class AttendanceRecord
{
    public int Id { get; set; }

    [Required]
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    [Required]
    public DateTime Date { get; set; }

    public bool IsPresent { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Required]
    public string MarkedByUserId { get; set; } = string.Empty;
    public Users MarkedByUser { get; set; } = null!;
}

public class ExamResult
{
    public int Id { get; set; }

    [Required]
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    [Required, StringLength(120)]
    public string ExamName { get; set; } = string.Empty;

    [Range(0, 100000)]
    public decimal MarksObtained { get; set; }

    [Range(0.01, 100000)]
    public decimal MaxMarks { get; set; }

    [Required]
    public DateTime ExamDate { get; set; }

    [Required]
    public string RecordedByUserId { get; set; } = string.Empty;
    public Users RecordedByUser { get; set; } = null!;
}
