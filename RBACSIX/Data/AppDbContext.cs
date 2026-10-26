using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Models;

namespace RBACSIX.Data
{
    public class AppDbContext : IdentityDbContext<Users, IdentityRole, string>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
        public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<Batch> Batches => Set<Batch>();
        public DbSet<Enrollment> Enrollments => Set<Enrollment>();
        public DbSet<EnrollmentRequest> EnrollmentRequests => Set<EnrollmentRequest>();
        public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
        public DbSet<ExamResult> ExamResults => Set<ExamResult>();
        public DbSet<ScheduledClass> ScheduledClasses => Set<ScheduledClass>();
        public DbSet<StudyMaterial> StudyMaterials => Set<StudyMaterial>();
        public DbSet<Assignment> Assignments => Set<Assignment>();
        public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
        public DbSet<Announcement> Announcements => Set<Announcement>();
        public DbSet<ParentProfile> ParentProfiles => Set<ParentProfile>();
        public DbSet<ParentStudentLink> ParentStudentLinks => Set<ParentStudentLink>();
        public DbSet<EnrollmentCharge> EnrollmentCharges => Set<EnrollmentCharge>();
        public DbSet<EnrollmentPayment> EnrollmentPayments => Set<EnrollmentPayment>();
        public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<StudentProfile>()
                .HasOne(profile => profile.User)
                .WithOne(user => user.StudentProfile)
                .HasForeignKey<StudentProfile>(profile => profile.UserId);

            builder.Entity<StaffProfile>()
                .HasOne(profile => profile.User)
                .WithOne(user => user.StaffProfile)
                .HasForeignKey<StaffProfile>(profile => profile.UserId);

            builder.Entity<StaffProfile>()
                .Property(profile => profile.IsApproved)
                .HasDefaultValue(true);

            builder.Entity<ParentProfile>()
                .HasKey(profile => profile.UserId);

            builder.Entity<ParentProfile>()
                .HasOne(profile => profile.User)
                .WithOne(user => user.ParentProfile)
                .HasForeignKey<ParentProfile>(profile => profile.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ParentStudentLink>()
                .HasKey(link => new { link.ParentUserId, link.StudentProfileId });

            builder.Entity<ParentStudentLink>()
                .HasOne(link => link.Parent)
                .WithMany(parent => parent.StudentLinks)
                .HasForeignKey(link => link.ParentUserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ParentStudentLink>()
                .HasOne(link => link.Student)
                .WithMany(student => student.ParentLinks)
                .HasForeignKey(link => link.StudentProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Enrollment>()
                .HasIndex(enrollment => new { enrollment.StudentProfileId, enrollment.BatchId })
                .IsUnique();

            builder.Entity<Enrollment>()
                .HasIndex(enrollment => enrollment.BatchId);

            builder.Entity<EnrollmentRequest>()
                .HasIndex(request => new { request.StudentProfileId, request.BatchId, request.Status });

            builder.Entity<EnrollmentRequest>()
                .HasOne(request => request.Student)
                .WithMany()
                .HasForeignKey(request => request.StudentProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<EnrollmentRequest>()
                .HasOne(request => request.Batch)
                .WithMany()
                .HasForeignKey(request => request.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Course>()
                .Property(course => course.DefaultFee)
                .HasPrecision(18, 2);

            builder.Entity<Enrollment>()
                .Property(enrollment => enrollment.FeeOverride)
                .HasPrecision(18, 2);

            builder.Entity<AttendanceRecord>()
                .HasIndex(record => new { record.EnrollmentId, record.Date })
                .IsUnique();

            builder.Entity<Batch>()
                .HasOne(batch => batch.StaffUser)
                .WithMany()
                .HasForeignKey(batch => batch.StaffUserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<AttendanceRecord>()
                .HasOne(record => record.MarkedByUser)
                .WithMany()
                .HasForeignKey(record => record.MarkedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ExamResult>()
                .HasOne(result => result.RecordedByUser)
                .WithMany()
                .HasForeignKey(result => result.RecordedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ExamResult>()
                .Property(result => result.MarksObtained)
                .HasPrecision(18, 2);

            builder.Entity<ExamResult>()
                .Property(result => result.MaxMarks)
                .HasPrecision(18, 2);

            builder.Entity<ExamResult>()
                .HasIndex(result => new { result.EnrollmentId, result.ExamName, result.ExamDate })
                .IsUnique();

            builder.Entity<ScheduledClass>()
                .HasIndex(scheduledClass => scheduledClass.ZoomMeetingId)
                .IsUnique();

            builder.Entity<ScheduledClass>()
                .HasOne(scheduledClass => scheduledClass.CreatedByStaffUser)
                .WithMany()
                .HasForeignKey(scheduledClass => scheduledClass.CreatedByStaffUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<StudyMaterial>()
                .HasIndex(material => new { material.BatchId, material.CreatedUtc });

            builder.Entity<StudyMaterial>()
                .HasOne(material => material.Batch)
                .WithMany(batch => batch.StudyMaterials)
                .HasForeignKey(material => material.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Assignment>()
                .HasIndex(assignment => new { assignment.BatchId, assignment.CreatedUtc });

            builder.Entity<Assignment>()
                .HasOne(assignment => assignment.Batch)
                .WithMany(batch => batch.Assignments)
                .HasForeignKey(assignment => assignment.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Assignment>()
                .Property(assignment => assignment.MaxScore)
                .HasPrecision(18, 2);

            builder.Entity<AssignmentSubmission>()
                .HasIndex(submission => new { submission.AssignmentId, submission.StudentProfileId })
                .IsUnique();

            builder.Entity<AssignmentSubmission>()
                .HasOne(submission => submission.Student)
                .WithMany()
                .HasForeignKey(submission => submission.StudentProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AssignmentSubmission>()
                .Property(submission => submission.Score)
                .HasPrecision(18, 2);

            builder.Entity<Announcement>()
                .HasIndex(announcement => new { announcement.BatchId, announcement.CreatedUtc });

            builder.Entity<Announcement>()
                .HasOne(announcement => announcement.Batch)
                .WithMany(batch => batch.Announcements)
                .HasForeignKey(announcement => announcement.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<EnrollmentCharge>()
                .Property(charge => charge.Amount)
                .HasPrecision(18, 2);

            builder.Entity<EnrollmentCharge>()
                .HasOne(charge => charge.Enrollment)
                .WithMany(enrollment => enrollment.Charges)
                .HasForeignKey(charge => charge.EnrollmentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<EnrollmentPayment>()
                .Property(payment => payment.Amount)
                .HasPrecision(18, 2);

            builder.Entity<EnrollmentPayment>()
                .HasOne(payment => payment.Enrollment)
                .WithMany(enrollment => enrollment.Payments)
                .HasForeignKey(payment => payment.EnrollmentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<LeaveRequest>()
                .HasIndex(request => new { request.EnrollmentId, request.Status, request.StartDate, request.EndDate });

            builder.Entity<LeaveRequest>()
                .HasOne(request => request.Enrollment)
                .WithMany(enrollment => enrollment.LeaveRequests)
                .HasForeignKey(request => request.EnrollmentId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
