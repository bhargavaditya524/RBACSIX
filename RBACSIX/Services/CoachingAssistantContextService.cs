using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;

namespace RBACSIX.Services;

public sealed class CoachingAssistantContextService(AppDbContext db)
{
    private static readonly HashSet<string> SearchStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "attendance", "balance", "batch", "batches", "course", "courses", "fee", "fees",
        "find", "how", "much", "please", "result", "results", "show", "student", "students", "tell",
        "what", "when", "where", "which", "who", "with", "for", "the", "from", "does", "have",
        "their", "his", "her", "is", "are", "was", "were", "and", "can", "my", "me"
    };

    public async Task<IReadOnlyList<AssistantEvidence>> BuildAsync(
        ClaimsPrincipal principal,
        string question,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException("The signed-in user identity is unavailable.");

        if (principal.IsInRole("Admin"))
            return await BuildAdminEvidenceAsync(question, cancellationToken);
        if (principal.IsInRole("Staff"))
            return await BuildStaffEvidenceAsync(userId, question, cancellationToken);
        if (principal.IsInRole("Student"))
            return await BuildStudentEvidenceAsync(userId, question, cancellationToken);
        if (principal.IsInRole("Parent"))
            return await BuildParentEvidenceAsync(userId, question, cancellationToken);

        throw new InvalidOperationException("The signed-in user's role is not supported by the assistant.");
    }

    private async Task<IReadOnlyList<AssistantEvidence>> BuildAdminEvidenceAsync(
        string question,
        CancellationToken cancellationToken)
    {
        var evidence = new List<AssistantEvidence>();
        var includeFees = Mentions(question, "fee", "payment", "paid", "balance", "charge", "tuition", "due");
        var includeAttendance = Mentions(question, "attendance", "attend", "absent", "absence", "present");
        var includeResults = Mentions(question, "result", "exam", "mark", "score", "grade");
        var includeAssignments = Mentions(question, "assignment", "homework", "submission");
        var includeQuizzes = Mentions(question, "quiz", "test", "question");
        var includeLearning = includeAssignments || includeQuizzes;
        var includePrograms = Mentions(
            question, "course", "batch", "staff", "teacher", "schedule", "class", "student", "enroll");
        var includeAnnouncements = Mentions(question, "announcement", "news", "update");
        var includeLeave = Mentions(question, "leave", "absence request");
        var includeDoubts = Mentions(question, "doubt", "solution");
        var includeSchedule = Mentions(question, "schedule", "class", "meeting", "zoom", "timetable");
        var includeMaterials = Mentions(question, "material", "resource", "notes", "pdf", "link");
        var includeEnrollmentRequests = Mentions(question, "enrollment request", "course request", "batch request");
        var summary = new
        {
            Students = await db.StudentProfiles.CountAsync(cancellationToken),
            Staff = await db.StaffProfiles.CountAsync(cancellationToken),
            Courses = await db.Courses.CountAsync(cancellationToken),
            Batches = await db.Batches.CountAsync(cancellationToken),
            PendingStaffApprovals = await db.StaffProfiles.CountAsync(
                profile => !profile.IsApproved,
                cancellationToken),
            PendingEnrollmentRequests = await db.EnrollmentRequests.CountAsync(
                request => request.Status == "Pending",
                cancellationToken)
        };
        evidence.Add(JsonEvidence("Portal summary", summary));

        if (includePrograms)
        {
            var courses = await db.Courses
                .OrderBy(course => course.Name)
                .Take(100)
                .Select(course => new
                {
                    course.Name,
                    course.Description,
                    course.DefaultFee,
                    course.IsActive,
                    BatchCount = course.Batches.Count
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Courses", courses));

            var batches = await db.Batches
                .OrderByDescending(batch => batch.StartDate)
                .Take(50)
                .Select(batch => new
                {
                    Course = batch.Course.Name,
                    Batch = batch.Name,
                    batch.StartDate,
                    batch.EndDate,
                    batch.ScheduleDescription,
                    Staff = batch.StaffUser == null ? null : batch.StaffUser.StaffProfile!.FullName,
                    Students = batch.Enrollments.Count,
                    Capacity = batch.Capacity
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Batches", batches));
        }

        if (includeAssignments)
        {
            var assignments = await db.Assignments
                .OrderByDescending(assignment => assignment.CreatedUtc)
                .Take(100)
                .Select(assignment => new
                {
                    Course = assignment.Batch.Course.Name,
                    Batch = assignment.Batch.Name,
                    assignment.Title,
                    assignment.Description,
                    assignment.MaxScore,
                    assignment.CreatedUtc
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Assignments", assignments));
        }
        if (includeQuizzes)
        {
            var quizzes = await db.Quizzes
                .OrderByDescending(quiz => quiz.CreatedUtc)
                .Take(100)
                .Select(quiz => new
                {
                    Course = quiz.Batch.Course.Name,
                    Batch = quiz.Batch.Name,
                    quiz.Title,
                    quiz.DurationMinutes,
                    quiz.IsPublished,
                    QuestionCount = quiz.Questions.Count
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Quizzes", quizzes));
        }
        if (includeAnnouncements)
        {
            var announcements = await db.Announcements
                .OrderByDescending(announcement => announcement.CreatedUtc)
                .Take(50)
                .Select(announcement => new
                {
                    announcement.Title,
                    announcement.Body,
                    announcement.CreatedUtc,
                    Batch = announcement.Batch == null ? "Global" : announcement.Batch.Name
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Announcements", announcements));
        }
        if (includeEnrollmentRequests)
        {
            var requests = await db.EnrollmentRequests
                .OrderByDescending(request => request.RequestedUtc)
                .Take(100)
                .Select(request => new
                {
                    Student = request.Student.FullName,
                    Course = request.Batch.Course.Name,
                    Batch = request.Batch.Name,
                    request.Status,
                    request.RequestedUtc,
                    request.ReviewNote
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Enrollment requests", requests));
        }
        if (includeLeave)
        {
            var leaveRequests = await db.LeaveRequests
                .OrderByDescending(request => request.CreatedUtc)
                .Take(100)
                .Select(request => new
                {
                    Student = request.Enrollment.Student.FullName,
                    Batch = request.Enrollment.Batch.Name,
                    request.StartDate,
                    request.EndDate,
                    request.Reason,
                    request.Status,
                    request.ReviewNote
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Leave requests", leaveRequests));
        }
        if (includeDoubts)
        {
            var doubts = await db.StudentDoubts
                .OrderByDescending(doubt => doubt.CreatedUtc)
                .Take(100)
                .Select(doubt => new
                {
                    Student = doubt.Student.FullName,
                    Batch = doubt.Batch.Name,
                    doubt.Question,
                    doubt.Solution,
                    doubt.CreatedUtc,
                    doubt.AnsweredUtc
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Student doubts", doubts));
        }
        if (includeSchedule || includeMaterials)
        {
            var batchIds = await db.Batches.Select(batch => batch.Id).ToArrayAsync(cancellationToken);
            evidence.AddRange(await BuildBatchResourceEvidenceAsync(
                batchIds, includeSchedule, includeMaterials, cancellationToken));
        }

        var matchingStudentIds = await FindMatchingStudentIdsAsync(question, cancellationToken);
        foreach (var profileId in matchingStudentIds)
        {
            var profile = await db.StudentProfiles
                .Where(item => item.Id == profileId)
                .Select(item => new { item.Id, item.FullName })
                .SingleAsync(cancellationToken);
            var enrollments = await LoadEnrollmentsAsync(
                enrollment => enrollment.StudentProfileId == profileId,
                cancellationToken,
                includeStudentLearning: includeLearning);
            evidence.Add(JsonEvidence($"Student {profile.FullName}", new
            {
                profile.FullName,
                Enrollments = enrollments.Select(enrollment => new
                {
                    Course = enrollment.Batch.Course.Name,
                    Batch = enrollment.Batch.Name,
                    Fee = FeesSummary(enrollment, includeFees),
                    Attendance = AttendanceSummaryIfRequested(enrollment, includeAttendance),
                    Results = ResultsSummary(enrollment, includeResults),
                    Learning = includeLearning ? LearningSummary(enrollment, profileId) : null
                }).ToArray()
            }));
        }

        return evidence;
    }

    private async Task<IReadOnlyList<AssistantEvidence>> BuildStaffEvidenceAsync(
        string userId,
        string question,
        CancellationToken cancellationToken)
    {
        var includeFees = Mentions(question, "fee", "payment", "paid", "balance", "charge", "tuition", "due");
        var includeAttendance = Mentions(question, "attendance", "attend", "absent", "absence", "present");
        var includeResults = Mentions(question, "result", "exam", "mark", "score", "grade");
        var includeAssignments = Mentions(question, "assignment", "homework", "submission");
        var includeQuizzes = Mentions(question, "quiz", "test", "question");
        var includeLearning = includeAssignments || includeQuizzes;
        var includeAnnouncements = Mentions(question, "announcement", "news", "update");
        var includeLeave = Mentions(question, "leave", "absence request");
        var includeDoubts = Mentions(question, "doubt", "solution");
        var includeSchedule = Mentions(question, "schedule", "class", "meeting", "zoom", "timetable");
        var includeMaterials = Mentions(question, "material", "resource", "notes", "pdf", "link");
        var batches = await db.Batches
            .Where(batch => batch.StaffUserId == userId)
            .OrderBy(batch => batch.StartDate)
            .Take(30)
            .Select(batch => new
            {
                batch.Id,
                Course = batch.Course.Name,
                Batch = batch.Name,
                batch.StartDate,
                batch.EndDate,
                batch.ScheduleDescription,
                Students = batch.Enrollments.Count
            })
            .ToListAsync(cancellationToken);

        var evidence = new List<AssistantEvidence>
        {
            JsonEvidence("Your assigned batches", batches)
        };
        var batchIds = batches.Select(batch => batch.Id).ToArray();
        if (batchIds.Length == 0)
            return evidence;

        if (includeAnnouncements)
        {
            var announcements = await db.Announcements
                .Where(announcement => announcement.BatchId == null ||
                                       batchIds.Contains(announcement.BatchId.Value))
                .OrderByDescending(announcement => announcement.CreatedUtc)
                .Take(30)
                .Select(announcement => new
                {
                    announcement.Title,
                    announcement.Body,
                    announcement.CreatedUtc,
                    Batch = announcement.Batch == null ? "Global" : announcement.Batch.Name
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Announcements visible to you", announcements));
        }

        if (includeAssignments)
        {
            var assignments = await db.Assignments
                .Where(assignment => batchIds.Contains(assignment.BatchId))
                .OrderByDescending(assignment => assignment.CreatedUtc)
                .Take(100)
                .Select(assignment => new
                {
                    Course = assignment.Batch.Course.Name,
                    Batch = assignment.Batch.Name,
                    assignment.Title,
                    assignment.Description,
                    assignment.MaxScore,
                    assignment.CreatedUtc
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Assignments in your batches", assignments));
        }

        if (includeQuizzes)
        {
            var quizzes = await db.Quizzes
                .Where(quiz => batchIds.Contains(quiz.BatchId))
                .OrderByDescending(quiz => quiz.CreatedUtc)
                .Take(100)
                .Select(quiz => new
                {
                    Course = quiz.Batch.Course.Name,
                    Batch = quiz.Batch.Name,
                    quiz.Title,
                    quiz.Instructions,
                    quiz.DurationMinutes,
                    quiz.IsPublished,
                    QuestionCount = quiz.Questions.Count
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Quizzes in your batches", quizzes));
        }

        if (includeLeave)
        {
            var leaveRequests = await db.LeaveRequests
                .Where(request => request.Enrollment.Batch.StaffUserId == userId)
                .OrderByDescending(request => request.CreatedUtc)
                .Take(100)
                .Select(request => new
                {
                    Student = request.Enrollment.Student.FullName,
                    Batch = request.Enrollment.Batch.Name,
                    request.StartDate,
                    request.EndDate,
                    request.Reason,
                    request.Status,
                    request.ReviewNote
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Leave requests in your batches", leaveRequests));
        }
        if (includeDoubts)
        {
            var doubts = await db.StudentDoubts
                .Where(doubt => doubt.Batch.StaffUserId == userId)
                .OrderByDescending(doubt => doubt.CreatedUtc)
                .Take(100)
                .Select(doubt => new
                {
                    Student = doubt.Student.FullName,
                    Batch = doubt.Batch.Name,
                    doubt.Question,
                    doubt.Solution,
                    doubt.CreatedUtc,
                    doubt.AnsweredUtc
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Doubts in your batches", doubts));
        }
        if (includeSchedule || includeMaterials)
            evidence.AddRange(await BuildBatchResourceEvidenceAsync(
                batchIds, includeSchedule, includeMaterials, cancellationToken));

        var terms = GetSearchTerms(question);
        if (terms.Length == 0)
            return evidence;

        var authorizedStudents = await db.Enrollments
            .Where(enrollment => batchIds.Contains(enrollment.BatchId))
            .Select(enrollment => new { enrollment.StudentProfileId, enrollment.Student.FullName })
            .Distinct()
            .ToListAsync(cancellationToken);
        var matchingStudentIds = authorizedStudents
            .Where(student => terms.Any(term =>
                student.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(student => student.StudentProfileId)
            .Distinct()
            .Take(5)
            .ToArray();
        if (matchingStudentIds.Length == 0)
            return evidence;

        var enrollments = await LoadEnrollmentsAsync(
            enrollment => batchIds.Contains(enrollment.BatchId) &&
                          matchingStudentIds.Contains(enrollment.StudentProfileId),
            cancellationToken,
            includeStudentLearning: includeLearning);
        foreach (var enrollment in enrollments)
        {
            evidence.Add(JsonEvidence(
                $"Attendance and results for {enrollment.Student.FullName} in {enrollment.Batch.Name}",
                new
                {
                    Student = enrollment.Student.FullName,
                    Course = enrollment.Batch.Course.Name,
                    Batch = enrollment.Batch.Name,
                    Attendance = AttendanceSummaryIfRequested(enrollment, includeAttendance),
                    Results = ResultsSummary(enrollment, includeResults),
                    Fee = FeesSummary(enrollment, includeFees),
                    Learning = includeLearning
                        ? LearningSummary(enrollment, enrollment.StudentProfileId)
                        : null
                }));
        }
        return evidence;
    }

    private async Task<IReadOnlyList<AssistantEvidence>> BuildStudentEvidenceAsync(
        string userId,
        string question,
        CancellationToken cancellationToken)
    {
        var includeFees = Mentions(question, "fee", "payment", "paid", "balance", "charge", "tuition", "due");
        var includeAttendance = Mentions(question, "attendance", "attend", "absent", "absence", "present");
        var includeResults = Mentions(question, "result", "exam", "mark", "score", "grade");
        var includeAssignments = Mentions(question, "assignment", "homework", "submission");
        var includeQuizzes = Mentions(question, "quiz", "test", "question");
        var includeLearning = includeAssignments || includeQuizzes;
        var includeAnnouncements = Mentions(question, "announcement", "news", "update");
        var includeLeave = Mentions(question, "leave", "absence request");
        var includeEnrollmentRequests = Mentions(question, "enrollment request", "course request", "batch request");
        var includeDoubts = Mentions(question, "doubt", "solution");
        var includeSchedule = Mentions(question, "schedule", "class", "meeting", "zoom", "timetable");
        var includeMaterials = Mentions(question, "material", "resource", "notes", "pdf", "link");
        var includeEnrollments = Mentions(question, "course", "batch", "enroll", "teacher", "staff");
        var profile = await db.StudentProfiles
            .Where(item => item.UserId == userId)
            .Select(item => new { item.Id, item.FullName })
            .SingleOrDefaultAsync(cancellationToken);
        if (profile is null)
            return [new AssistantEvidence("Student profile", "No student profile is linked to your account.")];

        if (!includeFees && !includeAttendance && !includeResults && !includeLearning &&
            !includeAnnouncements && !includeLeave && !includeEnrollmentRequests &&
            !includeDoubts && !includeSchedule && !includeMaterials && !includeEnrollments)
            return [];

        var enrollments = await LoadEnrollmentsAsync(
            enrollment => enrollment.StudentProfileId == profile.Id,
            cancellationToken,
            includeStudentLearning: includeLearning,
            submissionsForStudentId: includeLearning ? profile.Id : null);
        var evidence = new List<AssistantEvidence>();
        foreach (var enrollment in enrollments)
        {
            evidence.Add(JsonEvidence(
                $"Your enrollment in {enrollment.Batch.Name}",
                new
                {
                    Course = enrollment.Batch.Course.Name,
                    Batch = enrollment.Batch.Name,
                    enrollment.Batch.StartDate,
                    enrollment.Batch.EndDate,
                    Schedule = includeSchedule ? enrollment.Batch.ScheduleDescription : null,
                    Fee = FeesSummary(enrollment, includeFees),
                    Attendance = AttendanceSummaryIfRequested(enrollment, includeAttendance),
                    Results = ResultsSummary(enrollment, includeResults),
                    Learning = includeLearning ? LearningSummary(enrollment, profile.Id) : null,
                    LeaveRequests = includeLeave
                        ? enrollment.LeaveRequests.Select(LeaveRequestSummary).ToArray()
                        : null
                }));
        }

        var batchIds = enrollments.Select(enrollment => enrollment.BatchId).ToArray();
        if (includeAnnouncements)
            evidence.AddRange(await BuildVisibleAnnouncementsAsync(batchIds, cancellationToken));
        if (includeSchedule || includeMaterials)
            evidence.AddRange(await BuildBatchResourceEvidenceAsync(
                batchIds, includeSchedule, includeMaterials, cancellationToken));
        if (includeEnrollmentRequests)
        {
            var requests = await db.EnrollmentRequests
                .Where(request => request.StudentProfileId == profile.Id)
                .OrderByDescending(request => request.RequestedUtc)
                .Take(30)
                .Select(request => new
                {
                    Course = request.Batch.Course.Name,
                    Batch = request.Batch.Name,
                    request.Status,
                    request.RequestedUtc,
                    request.ReviewNote
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Your enrollment requests", requests));
        }
        if (includeDoubts && batchIds.Length > 0)
        {
            var doubts = await db.StudentDoubts
                .Where(doubt => doubt.StudentProfileId == profile.Id && batchIds.Contains(doubt.BatchId))
                .OrderByDescending(doubt => doubt.CreatedUtc)
                .Take(30)
                .Select(doubt => new
                {
                    Batch = doubt.Batch.Name,
                    doubt.Question,
                    doubt.Solution,
                    doubt.CreatedUtc,
                    doubt.AnsweredUtc
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Your doubts and their solutions", doubts));
        }
        return evidence;
    }

    private async Task<IReadOnlyList<AssistantEvidence>> BuildParentEvidenceAsync(
        string userId,
        string question,
        CancellationToken cancellationToken)
    {
        var includeFees = Mentions(question, "fee", "payment", "paid", "balance", "charge", "tuition", "due");
        var includeAttendance = Mentions(question, "attendance", "attend", "absent", "absence", "present");
        var includeResults = Mentions(question, "result", "exam", "mark", "score", "grade");
        var includeLeave = Mentions(question, "leave", "absence request");
        var includeAnnouncements = Mentions(question, "announcement", "news", "update");
        var includeEnrollments = Mentions(question, "course", "batch", "enroll", "teacher", "staff");
        if (!includeFees && !includeAttendance && !includeResults && !includeLeave &&
            !includeAnnouncements && !includeEnrollments)
            return [];

        var children = await db.ParentStudentLinks
            .Where(link => link.ParentUserId == userId)
            .OrderBy(link => link.Student.FullName)
            .Select(link => new { link.StudentProfileId, link.Student.FullName })
            .ToListAsync(cancellationToken);

        var evidence = new List<AssistantEvidence>();
        foreach (var child in children)
        {
            var enrollments = await LoadEnrollmentsAsync(
                enrollment => enrollment.StudentProfileId == child.StudentProfileId,
                cancellationToken);
            evidence.Add(JsonEvidence(
                $"Linked student {child.FullName}",
                new
                {
                    Student = child.FullName,
                    Enrollments = enrollments.Select(enrollment => new
                    {
                        Course = enrollment.Batch.Course.Name,
                        Batch = enrollment.Batch.Name,
                        Fee = FeesSummary(enrollment, includeFees),
                        Attendance = AttendanceSummaryIfRequested(enrollment, includeAttendance),
                        Results = ResultsSummary(enrollment, includeResults),
                        Enrollment = includeEnrollments
                            ? new { Course = enrollment.Batch.Course.Name, Batch = enrollment.Batch.Name }
                            : null,
                        LeaveRequests = includeLeave
                            ? enrollment.LeaveRequests.Select(LeaveRequestSummary).ToArray()
                            : null
                    })
                }));
        }

        if (children.Count == 0)
            evidence.Add(new AssistantEvidence("Linked students", "No students are linked to your account."));
        if (includeAnnouncements)
            evidence.AddRange(await BuildVisibleAnnouncementsAsync(
                [], cancellationToken, includeOnlyGlobal: true));
        return evidence;
    }

    private async Task<int[]> FindMatchingStudentIdsAsync(
        string question,
        CancellationToken cancellationToken)
    {
        var terms = GetSearchTerms(question);
        if (terms.Length == 0)
            return [];

        var candidates = await db.StudentProfiles
            .Select(profile => new { profile.Id, profile.FullName })
            .ToListAsync(cancellationToken);
        return candidates
            .Where(profile => terms.Any(term =>
                profile.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(profile => profile.FullName)
            .Take(5)
            .Select(profile => profile.Id)
            .ToArray();
    }

    private static string[] GetSearchTerms(string question) =>
        Regex.Split(question, @"[^\p{L}\p{N}]+")
            .Where(term => term.Length >= 3 && !SearchStopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

    private static bool Mentions(string question, params string[] terms) =>
        terms.Any(term => question.Contains(term, StringComparison.OrdinalIgnoreCase));

    private Task<List<Enrollment>> LoadEnrollmentsAsync(
        System.Linq.Expressions.Expression<Func<Enrollment, bool>> predicate,
        CancellationToken cancellationToken,
        bool includeStudentLearning = false,
        int? submissionsForStudentId = null)
    {
        IQueryable<Enrollment> query = db.Enrollments
            .Where(predicate)
            .Include(enrollment => enrollment.Student)
            .Include(enrollment => enrollment.Batch)
                .ThenInclude(batch => batch.Course)
            .Include(enrollment => enrollment.AttendanceRecords)
            .Include(enrollment => enrollment.LeaveRequests)
            .Include(enrollment => enrollment.Results)
            .Include(enrollment => enrollment.Charges)
            .Include(enrollment => enrollment.Payments);

        if (includeStudentLearning)
        {
            var assignmentQuery = query.Include(enrollment => enrollment.Batch.Assignments);
            query = submissionsForStudentId.HasValue
                ? assignmentQuery.ThenInclude(assignment => assignment.Submissions.Where(
                    submission => submission.StudentProfileId == submissionsForStudentId.Value))
                : assignmentQuery.ThenInclude(assignment => assignment.Submissions);
            query = query
                .Include(enrollment => enrollment.Batch.Quizzes)
                    .ThenInclude(quiz => quiz.Questions);
        }

        return query
            .AsSplitQuery()
            .OrderBy(enrollment => enrollment.Batch.StartDate)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AssistantEvidence>> BuildVisibleAnnouncementsAsync(
        int[] batchIds,
        CancellationToken cancellationToken,
        bool includeOnlyGlobal = false)
    {
        var announcements = await db.Announcements
            .Where(announcement => announcement.BatchId == null ||
                (!includeOnlyGlobal && announcement.BatchId.HasValue &&
                 batchIds.Contains(announcement.BatchId.Value)))
            .OrderByDescending(announcement => announcement.CreatedUtc)
            .Take(30)
            .Select(announcement => new
            {
                announcement.Title,
                announcement.Body,
                announcement.CreatedUtc,
                Batch = announcement.Batch == null ? "Global" : announcement.Batch.Name
            })
            .ToListAsync(cancellationToken);
        return [JsonEvidence("Announcements visible to you", announcements)];
    }

    private async Task<IReadOnlyList<AssistantEvidence>> BuildBatchResourceEvidenceAsync(
        int[] batchIds,
        bool includeSchedule,
        bool includeMaterials,
        CancellationToken cancellationToken)
    {
        var evidence = new List<AssistantEvidence>();
        if (batchIds.Length == 0)
            return evidence;

        if (includeSchedule)
        {
            var classes = await db.ScheduledClasses
                .Where(scheduledClass => batchIds.Contains(scheduledClass.BatchId))
                .OrderBy(scheduledClass => scheduledClass.StartTimeUtc)
                .Take(100)
                .Select(scheduledClass => new
                {
                    Batch = scheduledClass.Batch.Name,
                    scheduledClass.Title,
                    scheduledClass.StartTimeUtc,
                    scheduledClass.DurationMinutes,
                    scheduledClass.TimeZoneId
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Scheduled classes", classes));
        }

        if (includeMaterials)
        {
            var materials = await db.StudyMaterials
                .Where(material => batchIds.Contains(material.BatchId))
                .OrderByDescending(material => material.CreatedUtc)
                .Take(100)
                .Select(material => new
                {
                    Batch = material.Batch.Name,
                    material.Title,
                    material.Description,
                    material.ResourceType,
                    material.ExternalUrl,
                    material.OriginalFileName,
                    material.CreatedUtc
                })
                .ToListAsync(cancellationToken);
            evidence.Add(JsonEvidence("Study materials", materials));
        }

        return evidence;
    }

    private static object EnrollmentSummary(Enrollment enrollment)
    {
        var tuition = enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee;
        var charges = enrollment.Charges.Sum(charge => charge.Amount);
        var payments = enrollment.Payments.Sum(payment => payment.Amount);
        return new
        {
            Tuition = tuition,
            MiscellaneousCharges = charges,
            Paid = payments,
            Balance = tuition + charges - payments
        };
    }

    private static object? FeesSummary(Enrollment enrollment, bool include) =>
        include ? EnrollmentSummary(enrollment) : null;

    private static object? AttendanceSummaryIfRequested(Enrollment enrollment, bool include) =>
        include ? AttendanceSummary(enrollment) : null;

    private static object? ResultsSummary(Enrollment enrollment, bool include) =>
        include
            ? enrollment.Results
                .OrderByDescending(result => result.ExamDate)
                .Take(20)
                .Select(result => new
                {
                    result.ExamName,
                    result.ExamDate,
                    result.MarksObtained,
                    result.MaxMarks
                })
                .ToArray()
            : null;

    private static object LeaveRequestSummary(LeaveRequest request) =>
        new
        {
            request.StartDate,
            request.EndDate,
            request.Reason,
            request.Status,
            request.ReviewNote
        };

    private static object LearningSummary(Enrollment enrollment, int studentProfileId)
    {
        return new
        {
            Assignments = enrollment.Batch.Assignments
                .OrderByDescending(assignment => assignment.CreatedUtc)
                .Take(20)
                .Select(assignment =>
                {
                    var submission = assignment.Submissions
                        .SingleOrDefault(item => item.StudentProfileId == studentProfileId);
                    return new
                    {
                        assignment.Title,
                        assignment.Description,
                        assignment.MaxScore,
                        Submitted = submission is not null,
                        Score = submission?.Score,
                        submission?.Feedback
                    };
                }),
            PublishedQuizzes = enrollment.Batch.Quizzes
                .Where(quiz => quiz.IsPublished)
                .OrderByDescending(quiz => quiz.CreatedUtc)
                .Take(20)
                .Select(quiz => new
                {
                    quiz.Title,
                    quiz.DurationMinutes,
                    QuestionCount = quiz.Questions.Count
                })
        };
    }

    private static object AttendanceSummary(Enrollment enrollment)
    {
        var validRecords = enrollment.AttendanceRecords
            .Where(record => !enrollment.LeaveRequests.Any(request =>
                request.Status == "Approved" &&
                request.StartDate.Date <= record.Date.Date &&
                request.EndDate.Date >= record.Date.Date))
            .ToArray();
        return new
        {
            Present = validRecords.Count(record => record.IsPresent),
            Classes = validRecords.Length,
            Percentage = validRecords.Length == 0
                ? 0
                : Math.Round(validRecords.Count(record => record.IsPresent) * 100d / validRecords.Length, 1)
        };
    }

    private static AssistantEvidence JsonEvidence(string source, object content)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(content);
        return new AssistantEvidence(source, json);
    }
}
