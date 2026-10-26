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
    public ICollection<ParentStudentLink> ParentLinks { get; set; } = new List<ParentStudentLink>();
}

public class ParentProfile
{
    [Required]
    public string UserId { get; set; } = string.Empty;
    public Users User { get; set; } = null!;

    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    public ICollection<ParentStudentLink> StudentLinks { get; set; } = new List<ParentStudentLink>();
}

public class ParentStudentLink
{
    [Required]
    public string ParentUserId { get; set; } = string.Empty;
    public ParentProfile Parent { get; set; } = null!;

    [Required]
    public int StudentProfileId { get; set; }
    public StudentProfile Student { get; set; } = null!;
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

    public bool IsApproved { get; set; } = true;
}

public class Course
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Range(0, 10000000)]
    public decimal DefaultFee { get; set; }

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

    [Range(1, int.MaxValue)]
    public int? Capacity { get; set; }

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<ScheduledClass> ScheduledClasses { get; set; } = new List<ScheduledClass>();
    public ICollection<StudyMaterial> StudyMaterials { get; set; } = new List<StudyMaterial>();
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    public ICollection<Announcement> Announcements { get; set; } = new List<Announcement>();
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
    [Range(0, 10000000)]
    public decimal? FeeOverride { get; set; }
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<ExamResult> Results { get; set; } = new List<ExamResult>();
    public ICollection<EnrollmentCharge> Charges { get; set; } = new List<EnrollmentCharge>();
    public ICollection<EnrollmentPayment> Payments { get; set; } = new List<EnrollmentPayment>();
    public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
}

public class EnrollmentRequest
{
    public int Id { get; set; }

    [Required]
    public int StudentProfileId { get; set; }
    public StudentProfile Student { get; set; } = null!;

    [Required]
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    [StringLength(2000)]
    public string? Message { get; set; }

    [Required, StringLength(20)]
    public string Status { get; set; } = "Pending";

    public DateTime RequestedUtc { get; set; } = DateTime.UtcNow;

    public string? ReviewedByAdminUserId { get; set; }
    public DateTime? ReviewedUtc { get; set; }

    [StringLength(1000)]
    public string? ReviewNote { get; set; }
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

public class ScheduledClass
{
    public int Id { get; set; }

    [Required]
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    [Required]
    public string CreatedByStaffUserId { get; set; } = string.Empty;
    public Users CreatedByStaffUser { get; set; } = null!;

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public DateTime StartTimeUtc { get; set; }

    [Required, StringLength(100)]
    public string TimeZoneId { get; set; } = string.Empty;

    [Range(15, 480)]
    public int DurationMinutes { get; set; }

    [Required, StringLength(32)]
    public string ZoomMeetingId { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string JoinUrl { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class StudyMaterial
{
    public int Id { get; set; }

    [Required]
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    [Required]
    public string CreatedByStaffUserId { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required, StringLength(20)]
    public string ResourceType { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? ExternalUrl { get; set; }

    [StringLength(100)]
    public string? StoredFileName { get; set; }

    [StringLength(255)]
    public string? OriginalFileName { get; set; }

    [StringLength(100)]
    public string? ContentType { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class Assignment
{
    public int Id { get; set; }

    [Required]
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    [Required]
    public string CreatedByStaffUserId { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 100000)]
    public decimal MaxScore { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<AssignmentSubmission> Submissions { get; set; } = new List<AssignmentSubmission>();
}

public class AssignmentSubmission
{
    public int Id { get; set; }

    [Required]
    public int AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;

    [Required]
    public int StudentProfileId { get; set; }
    public StudentProfile Student { get; set; } = null!;

    [Required, StringLength(100)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Comments { get; set; }

    public DateTime SubmittedUtc { get; set; } = DateTime.UtcNow;

    [Range(0, 100000)]
    public decimal? Score { get; set; }

    [StringLength(4000)]
    public string? Feedback { get; set; }

    public string? GradedByStaffUserId { get; set; }
    public DateTime? GradedUtc { get; set; }
}

public class Announcement
{
    public int Id { get; set; }

    public int? BatchId { get; set; }
    public Batch? Batch { get; set; }

    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class EnrollmentCharge
{
    public int Id { get; set; }

    [Required]
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    [Required, StringLength(160)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 10000000)]
    public decimal Amount { get; set; }

    public DateTime ChargedUtc { get; set; } = DateTime.UtcNow;

    [Required]
    public string CreatedByAdminUserId { get; set; } = string.Empty;
}

public class EnrollmentPayment
{
    public int Id { get; set; }

    [Required]
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    [Range(0.01, 10000000)]
    public decimal Amount { get; set; }

    [Required]
    public DateTime PaymentDate { get; set; } = DateTime.Today;

    [StringLength(500)]
    public string? Notes { get; set; }

    [Required]
    public string ReceivedByAdminUserId { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class LeaveRequest
{
    public int Id { get; set; }

    [Required]
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    [Required]
    public string RequestedByUserId { get; set; } = string.Empty;

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string Status { get; set; } = "Pending";

    public string? ReviewedByStaffUserId { get; set; }
    public DateTime? ReviewedUtc { get; set; }

    [StringLength(1000)]
    public string? ReviewNote { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
