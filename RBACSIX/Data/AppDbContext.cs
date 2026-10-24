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
        public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
        public DbSet<ExamResult> ExamResults => Set<ExamResult>();
        public DbSet<ScheduledClass> ScheduledClasses => Set<ScheduledClass>();

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

            builder.Entity<Enrollment>()
                .HasIndex(enrollment => new { enrollment.StudentProfileId, enrollment.BatchId })
                .IsUnique();

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
        }
    }
}
