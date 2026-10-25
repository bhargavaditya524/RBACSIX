using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
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

    [Range(0, 10000000)]
    public decimal DefaultFee { get; set; }

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

    [Range(1, int.MaxValue)]
    public int? Capacity { get; set; }

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
    public int ScheduledClassCount { get; set; }
    public int? Capacity { get; set; }
    public List<ScheduledClassSummaryViewModel> ScheduledClasses { get; set; } = new();
}

public class CreateScheduledClassViewModel
{
    public int BatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, DataType(DataType.Date)]
    public DateTime? ClassDate { get; set; } = DateTime.Today;

    [Required, DataType(DataType.Time)]
    public TimeSpan? ClassTime { get; set; }

    [Range(15, 480)]
    public int DurationMinutes { get; set; } = 60;

    public string TimeZoneId { get; set; } = string.Empty;
    public List<ScheduledClassSummaryViewModel> ScheduledClasses { get; set; } = new();
    public List<SelectListItem> AssignedBatches { get; set; } = new();
}

public class ScheduledClassSummaryViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public DateTime StartTimeLocal { get; set; }
    public int DurationMinutes { get; set; }
    public string ZoomMeetingId { get; set; } = string.Empty;
    public string JoinUrl { get; set; } = string.Empty;
    public bool IsUpcoming { get; set; }
    public bool IsInProgress { get; set; }
    public bool CanDelete { get; set; }
}

public class EnrollStudentViewModel
{
    public int StudentProfileId { get; set; }
    public string StudentName { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int BatchId { get; set; }

    public List<SelectListItem> AvailableBatches { get; set; } = new();
    public List<EnrolledBatchViewModel> EnrolledBatches { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class EnrolledBatchViewModel
{
    public int EnrollmentId { get; set; }
    public int BatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public decimal TuitionFee { get; set; }
    public decimal MiscellaneousCharges { get; set; }
    public decimal Payments { get; set; }
    public decimal Balance { get; set; }
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
    public string TimeZoneId { get; set; } = string.Empty;
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
    public decimal TuitionFee { get; set; }
    public decimal MiscellaneousCharges { get; set; }
    public decimal Payments { get; set; }
    public decimal Balance { get; set; }
    public List<StudentResultSummaryViewModel> Results { get; set; } = new();
    public List<StudentClassViewModel> ScheduledClasses { get; set; } = new();
}

public class StudentClassViewModel
{
    public string Title { get; set; } = string.Empty;
    public DateTime StartTimeLocal { get; set; }
    public int DurationMinutes { get; set; }
    public string JoinUrl { get; set; } = string.Empty;
    public bool CanJoin { get; set; }
}

public class StudentResultSummaryViewModel
{
    public string ExamName { get; set; } = string.Empty;
    public DateTime ExamDate { get; set; }
    public decimal MarksObtained { get; set; }
    public decimal MaxMarks { get; set; }
}

public class StaffBatchLearningViewModel
{
    public int BatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public List<StudyMaterialSummaryViewModel> Materials { get; set; } = new();
    public List<AssignmentSummaryViewModel> Assignments { get; set; } = new();
}

public class CreateStudyMaterialViewModel
{
    [Range(1, int.MaxValue)]
    public int BatchId { get; set; }

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required]
    public string ResourceType { get; set; } = "Pdf";

    [Url, StringLength(2000)]
    public string? ExternalUrl { get; set; }

    public IFormFile? File { get; set; }
}

public class CreateAssignmentViewModel
{
    [Range(1, int.MaxValue)]
    public int BatchId { get; set; }

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 100000)]
    public decimal MaxScore { get; set; } = 100;
}

public class CreateBatchAnnouncementViewModel
{
    [Range(1, int.MaxValue)]
    public int BatchId { get; set; }

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Body { get; set; } = string.Empty;
}

public class StudyMaterialSummaryViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string? ExternalUrl { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class AssignmentSummaryViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal MaxScore { get; set; }
    public DateTime CreatedUtc { get; set; }
    public int SubmissionCount { get; set; }
}

public class GradeSubmissionsViewModel
{
    public int AssignmentId { get; set; }
    public string AssignmentTitle { get; set; } = string.Empty;
    public decimal MaxScore { get; set; }
    public List<GradeSubmissionEntryViewModel> Submissions { get; set; } = new();
}

public class GradeSubmissionEntryViewModel
{
    public int Id { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public DateTime SubmittedUtc { get; set; }
    public decimal? Score { get; set; }
    public string? Feedback { get; set; }
    public bool IsGraded { get; set; }
}

public class GradeAssignmentSubmissionViewModel
{
    public int SubmissionId { get; set; }

    [Required, Range(0, 100000)]
    public decimal? Score { get; set; }

    [StringLength(4000)]
    public string? Feedback { get; set; }
}

public class StudentLearningViewModel
{
    public string StudentName { get; set; } = string.Empty;
    public List<StudentLearningBatchViewModel> Batches { get; set; } = new();
}

public class StudentLearningBatchViewModel
{
    public int BatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public List<StudyMaterialSummaryViewModel> Materials { get; set; } = new();
    public List<StudentAssignmentViewModel> Assignments { get; set; } = new();
}

public class StudentAssignmentViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal MaxScore { get; set; }
    public DateTime CreatedUtc { get; set; }
    public int? SubmissionId { get; set; }
    public string? OriginalFileName { get; set; }
    public string? Comments { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public decimal? Score { get; set; }
    public string? Feedback { get; set; }
    public bool IsGraded { get; set; }
}

public class SubmitAssignmentViewModel
{
    public int AssignmentId { get; set; }

    [StringLength(2000)]
    public string? Comments { get; set; }

    public IFormFile? File { get; set; }
}

public class AnnouncementSummaryViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? BatchName { get; set; }
    public bool IsGlobal { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class AnnouncementListViewModel
{
    public List<AnnouncementSummaryViewModel> Announcements { get; set; } = new();
    public bool CanCreateGlobal { get; set; }
}

public class CreateGlobalAnnouncementViewModel
{
    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Body { get; set; } = string.Empty;

    public List<AnnouncementSummaryViewModel> ExistingAnnouncements { get; set; } = new();
}

public class AdminStudentsViewModel
{
    public string? SearchName { get; set; }
    public List<PersonSummaryViewModel> Students { get; set; } = new();
}

public class EnrollmentFeeViewModel
{
    public int EnrollmentId { get; set; }
    public int StudentProfileId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public decimal DefaultFee { get; set; }
    public decimal? FeeOverride { get; set; }
    public decimal EffectiveTuition { get; set; }
    public decimal MiscellaneousCharges { get; set; }
    public decimal Payments { get; set; }
    public decimal Balance { get; set; }
    public List<EnrollmentChargeSummaryViewModel> Charges { get; set; } = new();
    public List<EnrollmentPaymentSummaryViewModel> PaymentHistory { get; set; } = new();
}

public class EditEnrollmentFeeViewModel
{
    public int EnrollmentId { get; set; }
    [Range(0, 10000000)]
    public decimal? FeeOverride { get; set; }
}

public class AddEnrollmentChargeViewModel
{
    public int EnrollmentId { get; set; }
    [Required, StringLength(160)]
    public string Description { get; set; } = string.Empty;
    [Range(0.01, 10000000)]
    public decimal Amount { get; set; }
}

public class RecordEnrollmentPaymentViewModel
{
    public int EnrollmentId { get; set; }
    [Range(0.01, 10000000)]
    public decimal Amount { get; set; }
    [Required, DataType(DataType.Date)]
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    [StringLength(500)]
    public string? Notes { get; set; }
}

public class EnrollmentChargeSummaryViewModel
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime ChargedUtc { get; set; }
}

public class EnrollmentPaymentSummaryViewModel
{
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? Notes { get; set; }
}

public class CreateParentViewModel
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
    public List<int> StudentProfileIds { get; set; } = new();
    public List<SelectListItem> Students { get; set; } = new();
}

public class ParentSummaryViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int LinkedStudentCount { get; set; }
}

public class ParentLinksViewModel
{
    public string ParentUserId { get; set; } = string.Empty;
    public string ParentName { get; set; } = string.Empty;
    public List<int> StudentProfileIds { get; set; } = new();
    public List<SelectListItem> Students { get; set; } = new();
}

public class ParentDashboardViewModel
{
    public string ParentName { get; set; } = string.Empty;
    public List<ParentChildDashboardViewModel> Children { get; set; } = new();
}

public class ParentChildDashboardViewModel
{
    public int StudentProfileId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public List<StudentBatchViewModel> Batches { get; set; } = new();
}

public class CreateLeaveRequestViewModel
{
    [Range(1, int.MaxValue)]
    public int EnrollmentId { get; set; }
    [Required, DataType(DataType.Date)]
    public DateTime StartDate { get; set; } = DateTime.Today;
    [Required, DataType(DataType.Date)]
    public DateTime EndDate { get; set; } = DateTime.Today;
    [Required, StringLength(2000)]
    public string Reason { get; set; } = string.Empty;
    public int? StudentProfileId { get; set; }
}

public class LeaveRequestItemViewModel
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public string? ReviewNote { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class LeaveRequestsViewModel
{
    public bool CanReview { get; set; }
    public string? PageTitle { get; set; }
    public List<SelectListItem> Enrollments { get; set; } = new();
    public int? SelectedStudentProfileId { get; set; }
    public List<LeaveRequestItemViewModel> Requests { get; set; } = new();
}

public class ReviewLeaveRequestViewModel
{
    public int LeaveRequestId { get; set; }
    [Required]
    public string Decision { get; set; } = string.Empty;
    [StringLength(1000)]
    public string? ReviewNote { get; set; }
}
