using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;
using RBACSIX.Services;
using System.Data;

namespace RBACSIX.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<Users> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IZoomMeetingService _zoomMeetingService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AppDbContext db,
        UserManager<Users> userManager,
        RoleManager<IdentityRole> roleManager,
        IZoomMeetingService zoomMeetingService,
        ILogger<AdminController> logger)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _zoomMeetingService = zoomMeetingService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        return View(new AdminDashboardViewModel
        {
            StudentCount = await _db.StudentProfiles.CountAsync(),
            StaffCount = await _db.StaffProfiles.CountAsync(),
            CourseCount = await _db.Courses.CountAsync(),
            BatchCount = await _db.Batches.CountAsync(),
            PendingStaffCount = await _db.StaffProfiles.CountAsync(profile => !profile.IsApproved),
            PendingEnrollmentRequestCount = await _db.EnrollmentRequests.CountAsync(request => request.Status == "Pending")
        });
    }

    [HttpGet]
    public IActionResult CreateAdmin() => View(new CreateAdminViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAdmin(CreateAdminViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);
        if (await _userManager.FindByEmailAsync(model.Email) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
            return View(model);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = new Users
        {
            UserName = model.Email,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            LockoutEnabled = true
        };
        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return View(model);
        }
        var roleResult = await _userManager.AddToRoleAsync(user, "Admin");
        if (!roleResult.Succeeded)
        {
            AddIdentityErrors(roleResult);
            return View(model);
        }
        await transaction.CommitAsync();
        TempData["Message"] = "Administrator account created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> StaffApprovals()
    {
        var staff = await _db.StaffProfiles
            .Include(profile => profile.User)
            .OrderBy(profile => profile.FullName)
            .Select(profile => new
            {
                profile.UserId,
                profile.FullName,
                Email = profile.User.Email ?? string.Empty,
                profile.User.PhoneNumber,
                profile.SubjectSpecialty,
                profile.IsApproved
            })
            .ToListAsync();
        return View(new StaffApprovalViewModel
        {
            PendingStaff = staff.Where(profile => !profile.IsApproved)
                .Select(profile => new PersonSummaryViewModel
                {
                    ProfileId = 0,
                    FullName = profile.FullName,
                    Email = profile.Email,
                    PhoneNumber = profile.PhoneNumber,
                    Details = profile.SubjectSpecialty,
                    UserId = profile.UserId
                }).ToList(),
            ApprovedStaff = staff.Where(profile => profile.IsApproved)
                .Select(profile => new PersonSummaryViewModel
                {
                    FullName = profile.FullName,
                    Email = profile.Email,
                    PhoneNumber = profile.PhoneNumber,
                    Details = profile.SubjectSpecialty
                }).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveStaff(string userId)
    {
        var profile = await _db.StaffProfiles.SingleOrDefaultAsync(item => item.UserId == userId);
        if (profile is null)
            return NotFound();
        profile.IsApproved = true;
        await _db.SaveChangesAsync();
        TempData["Message"] = $"Staff account for {profile.FullName} approved.";
        return RedirectToAction(nameof(StaffApprovals));
    }

    [HttpGet]
    public async Task<IActionResult> EnrollmentRequests()
    {
        return View(await BuildEnrollmentRequestsAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewEnrollmentRequest(ReviewEnrollmentRequestViewModel model)
    {
        if (model.Decision is not ("Approved" or "Rejected"))
        {
            TempData["ErrorMessage"] = "Choose approve or reject.";
            return RedirectToAction(nameof(EnrollmentRequests));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await _db.EnrollmentRequests
            .Include(item => item.Batch)
                .ThenInclude(batch => batch.Course)
            .SingleOrDefaultAsync(item => item.Id == model.RequestId);
        if (request is null)
            return NotFound();
        if (request.Status != "Pending")
        {
            TempData["ErrorMessage"] = "This request has already been reviewed.";
            return RedirectToAction(nameof(EnrollmentRequests));
        }

        if (model.Decision == "Approved")
        {
            var alreadyEnrolled = await _db.Enrollments.AnyAsync(enrollment =>
                enrollment.StudentProfileId == request.StudentProfileId &&
                enrollment.BatchId == request.BatchId);
            if (!request.Batch.Course.IsActive)
            {
                request.Status = "Rejected";
                request.ReviewNote = "This course is no longer active.";
            }
            else if (alreadyEnrolled)
            {
                request.Status = "Rejected";
                request.ReviewNote = "The student is already enrolled in this batch.";
            }
            else
            {
                var enrollmentCount = await _db.Enrollments.CountAsync(enrollment =>
                    enrollment.BatchId == request.BatchId);
                if (request.Batch.Capacity.HasValue && enrollmentCount >= request.Batch.Capacity.Value)
                {
                    request.Status = "Rejected";
                    request.ReviewNote = "The batch is full; the student was not enrolled.";
                }
                else
                {
                    _db.Enrollments.Add(new Enrollment
                    {
                        StudentProfileId = request.StudentProfileId,
                        BatchId = request.BatchId
                    });
                    request.Status = "Approved";
                    request.ReviewNote = model.ReviewNote?.Trim();
                }
            }
        }
        else
        {
            request.Status = "Rejected";
            request.ReviewNote = model.ReviewNote?.Trim();
        }

        request.ReviewedByAdminUserId = _userManager.GetUserId(User);
        request.ReviewedUtc = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(exception, "Could not review enrollment request {RequestId}.", request.Id);
            TempData["ErrorMessage"] = "The request could not be updated. Refresh the list and try again.";
            return RedirectToAction(nameof(EnrollmentRequests));
        }
        TempData["Message"] = $"Enrollment request {request.Status.ToLowerInvariant()}.";
        return RedirectToAction(nameof(EnrollmentRequests));
    }

    [HttpGet]
    public async Task<IActionResult> GlobalAnnouncements()
    {
        return View(new CreateGlobalAnnouncementViewModel
        {
            ExistingAnnouncements = await _db.Announcements
                .Where(announcement => announcement.BatchId == null)
                .OrderByDescending(announcement => announcement.CreatedUtc)
                .Select(announcement => new AnnouncementSummaryViewModel
                {
                    Id = announcement.Id,
                    Title = announcement.Title,
                    Body = announcement.Body,
                    IsGlobal = true,
                    CreatedUtc = announcement.CreatedUtc
                })
                .ToListAsync()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GlobalAnnouncements(CreateGlobalAnnouncementViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.ExistingAnnouncements = await _db.Announcements
                .Where(announcement => announcement.BatchId == null)
                .OrderByDescending(announcement => announcement.CreatedUtc)
                .Select(announcement => new AnnouncementSummaryViewModel
                {
                    Id = announcement.Id,
                    Title = announcement.Title,
                    Body = announcement.Body,
                    IsGlobal = true,
                    CreatedUtc = announcement.CreatedUtc
                })
                .ToListAsync();
            return View(model);
        }

        _db.Announcements.Add(new Announcement
        {
            CreatedByUserId = _userManager.GetUserId(User)!,
            Title = model.Title.Trim(),
            Body = model.Body.Trim(),
            CreatedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Global announcement published.";
        return RedirectToAction(nameof(GlobalAnnouncements));
    }

    public async Task<IActionResult> Students(string? searchName)
    {
        searchName = searchName?.Trim();
        var query = _db.StudentProfiles.AsQueryable();
        if (!string.IsNullOrWhiteSpace(searchName))
            query = query.Where(profile => profile.FullName.Contains(searchName));

        var students = await query
            .Include(profile => profile.User)
            .OrderBy(profile => profile.FullName)
            .Select(profile => new PersonSummaryViewModel
            {
                ProfileId = profile.Id,
                FullName = profile.FullName,
                Email = profile.User.Email ?? string.Empty,
                PhoneNumber = profile.User.PhoneNumber,
                Details = profile.GuardianName
            })
            .ToListAsync();
        return View(new AdminStudentsViewModel { SearchName = searchName, Students = students });
    }

    [HttpGet]
    public IActionResult CreateStudent() => View(new CreatePersonViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStudent(CreatePersonViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (await _userManager.FindByEmailAsync(model.Email) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
            return View(model);
        }

        var user = new Users
        {
            UserName = model.Email,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            LockoutEnabled = true
        };
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return View(model);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, "Student");
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            AddIdentityErrors(roleResult);
            return View(model);
        }

        _db.StudentProfiles.Add(new StudentProfile
        {
            UserId = user.Id,
            FullName = model.FullName,
            GuardianName = model.GuardianName
        });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Message"] = "Student account created.";
        return RedirectToAction(nameof(Students));
    }

    public async Task<IActionResult> Staff()
    {
        var staff = await _db.StaffProfiles
            .Include(profile => profile.User)
            .OrderBy(profile => profile.FullName)
            .Select(profile => new PersonSummaryViewModel
            {
                ProfileId = profile.Id,
                FullName = profile.FullName,
                Email = profile.User.Email ?? string.Empty,
                PhoneNumber = profile.User.PhoneNumber,
                Details = profile.SubjectSpecialty,
                IsApproved = profile.IsApproved
            })
            .ToListAsync();
        return View(staff);
    }

    [HttpGet]
    public IActionResult CreateStaff() => View(new CreatePersonViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStaff(CreatePersonViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (await _userManager.FindByEmailAsync(model.Email) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
            return View(model);
        }

        var user = new Users
        {
            UserName = model.Email,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            LockoutEnabled = true
        };
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return View(model);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, "Staff");
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            AddIdentityErrors(roleResult);
            return View(model);
        }

        _db.StaffProfiles.Add(new StaffProfile
        {
            UserId = user.Id,
            FullName = model.FullName,
            SubjectSpecialty = model.SubjectSpecialty
        });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Message"] = "Staff account created.";
        return RedirectToAction(nameof(Staff));
    }

    public async Task<IActionResult> Courses()
    {
        var courses = await _db.Courses
            .OrderBy(course => course.Name)
            .Select(course => new CourseViewModel
            {
                Id = course.Id,
                Name = course.Name,
                Description = course.Description,
                DefaultFee = course.DefaultFee,
                IsActive = course.IsActive
            })
            .ToListAsync();
        return View(courses);
    }

    [HttpGet]
    public IActionResult CreateCourse() => View(new CourseViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCourse(CourseViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        _db.Courses.Add(new Course
        {
            Name = model.Name.Trim(),
            Description = model.Description?.Trim(),
            DefaultFee = model.DefaultFee,
            IsActive = model.IsActive
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Course created.";
        return RedirectToAction(nameof(Courses));
    }

    [HttpGet]
    public async Task<IActionResult> EditCourse(int id)
    {
        var course = await _db.Courses.FindAsync(id);
        return course is null
            ? NotFound()
            : View(new CourseViewModel
            {
                Id = course.Id,
                Name = course.Name,
                Description = course.Description,
                DefaultFee = course.DefaultFee,
                IsActive = course.IsActive
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCourse(CourseViewModel model)
    {
        var course = await _db.Courses.FindAsync(model.Id);
        if (course is null)
            return NotFound();
        if (!ModelState.IsValid)
            return View(model);

        course.Name = model.Name.Trim();
        course.Description = model.Description?.Trim();
        course.DefaultFee = model.DefaultFee;
        course.IsActive = model.IsActive;
        await _db.SaveChangesAsync();
        TempData["Message"] = "Course updated. Enrollments without individual fee overrides use the new course fee.";
        return RedirectToAction(nameof(Courses));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCourse(int id)
    {
        var course = await _db.Courses
            .Include(item => item.Batches)
                .ThenInclude(batch => batch.ScheduledClasses)
            .SingleOrDefaultAsync(item => item.Id == id);
        if (course is null)
            return NotFound();
        if (!await CancelZoomMeetingsAsync(course.Batches.SelectMany(batch => batch.ScheduledClasses)
                .Select(scheduledClass => scheduledClass.ZoomMeetingId)))
            return RedirectToAction(nameof(Courses));

        _db.Courses.Remove(course);
        await _db.SaveChangesAsync();
        TempData["Message"] = "Course and all linked batch records were deleted.";
        return RedirectToAction(nameof(Courses));
    }

    public async Task<IActionResult> Batches()
    {
        var batches = await _db.Batches
            .Include(batch => batch.Course)
            .Include(batch => batch.StaffUser)
            .Include(batch => batch.Enrollments)
            .OrderBy(batch => batch.StartDate)
            .Select(batch => new BatchSummaryViewModel
            {
                Id = batch.Id,
                Name = batch.Name,
                CourseName = batch.Course.Name,
                StaffName = batch.StaffUser == null || batch.StaffUser.StaffProfile == null
                    ? null
                    : batch.StaffUser.StaffProfile.FullName,
                ScheduleDescription = batch.ScheduleDescription ?? string.Empty,
                StartDate = batch.StartDate,
                EndDate = batch.EndDate,
                StudentCount = batch.Enrollments.Count,
                Capacity = batch.Capacity
            })
            .ToListAsync();
        return View(batches);
    }

    [HttpGet]
    public async Task<IActionResult> CreateBatch()
    {
        var model = new BatchViewModel();
        await PopulateBatchOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBatch(BatchViewModel model)
    {
        await ValidateBatchModelAsync(model);

        if (!ModelState.IsValid)
        {
            await PopulateBatchOptionsAsync(model);
            return View(model);
        }

        _db.Batches.Add(new Batch
        {
            Name = model.Name.Trim(),
            CourseId = model.CourseId,
            StaffUserId = model.StaffUserId,
            StartDate = model.StartDate.Date,
            EndDate = model.EndDate.Date,
            ScheduleDescription = model.ScheduleDescription?.Trim(),
            Capacity = model.Capacity
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Batch created.";
        return RedirectToAction(nameof(Batches));
    }

    [HttpGet]
    public async Task<IActionResult> EditBatch(int id)
    {
        var batch = await _db.Batches.FindAsync(id);
        if (batch is null)
            return NotFound();

        var model = new BatchViewModel
        {
            Id = batch.Id,
            Name = batch.Name,
            CourseId = batch.CourseId,
            StaffUserId = batch.StaffUserId,
            StartDate = batch.StartDate,
            EndDate = batch.EndDate,
            ScheduleDescription = batch.ScheduleDescription,
            Capacity = batch.Capacity
        };
        await PopulateBatchOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBatch(BatchViewModel model)
    {
        var batch = await _db.Batches.FindAsync(model.Id);
        if (batch is null)
            return NotFound();
        await ValidateBatchModelAsync(model);
        if (model.Capacity.HasValue &&
            await _db.Enrollments.CountAsync(enrollment => enrollment.BatchId == batch.Id) > model.Capacity.Value)
            ModelState.AddModelError(nameof(model.Capacity), "Capacity cannot be lower than the current enrollment count.");

        if (!ModelState.IsValid)
        {
            await PopulateBatchOptionsAsync(model);
            return View(model);
        }

        batch.Name = model.Name.Trim();
        batch.CourseId = model.CourseId;
        batch.StaffUserId = model.StaffUserId;
        batch.StartDate = model.StartDate.Date;
        batch.EndDate = model.EndDate.Date;
        batch.ScheduleDescription = model.ScheduleDescription?.Trim();
        batch.Capacity = model.Capacity;
        await _db.SaveChangesAsync();
        TempData["Message"] = "Batch updated.";
        return RedirectToAction(nameof(Batches));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBatch(int id)
    {
        var batch = await _db.Batches
            .Include(item => item.ScheduledClasses)
            .SingleOrDefaultAsync(item => item.Id == id);
        if (batch is null)
            return NotFound();
        if (!await CancelZoomMeetingsAsync(batch.ScheduledClasses.Select(scheduledClass => scheduledClass.ZoomMeetingId)))
            return RedirectToAction(nameof(Batches));

        _db.Batches.Remove(batch);
        await _db.SaveChangesAsync();
        TempData["Message"] = "Batch and all linked records were deleted.";
        return RedirectToAction(nameof(Batches));
    }

    public async Task<IActionResult> Enrollments(int studentId)
    {
        var student = await _db.StudentProfiles
            .Include(profile => profile.Enrollments)
                .ThenInclude(enrollment => enrollment.Batch)
                    .ThenInclude(batch => batch.Course)
            .Include(profile => profile.Enrollments)
                .ThenInclude(enrollment => enrollment.Charges)
            .Include(profile => profile.Enrollments)
                .ThenInclude(enrollment => enrollment.Payments)
            .SingleOrDefaultAsync(profile => profile.Id == studentId);
        if (student is null)
            return NotFound();

        var model = new EnrollStudentViewModel
        {
            StudentProfileId = student.Id,
            StudentName = student.FullName,
            EnrolledBatches = student.Enrollments.Select(enrollment => new EnrolledBatchViewModel
            {
                EnrollmentId = enrollment.Id,
                BatchId = enrollment.BatchId,
                BatchName = enrollment.Batch.Name,
                CourseName = enrollment.Batch.Course.Name,
                TuitionFee = enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee,
                MiscellaneousCharges = enrollment.Charges.Sum(charge => charge.Amount),
                Payments = enrollment.Payments.Sum(payment => payment.Amount),
                Balance = (enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee) +
                          enrollment.Charges.Sum(charge => charge.Amount) -
                          enrollment.Payments.Sum(payment => payment.Amount)
            }).ToList()
        };
        await PopulateEnrollmentOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnrollStudent(EnrollStudentViewModel model)
    {
        var student = await _db.StudentProfiles.FindAsync(model.StudentProfileId);
        if (student is null)
            return NotFound();

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var batch = await _db.Batches
            .Include(item => item.Course)
            .SingleOrDefaultAsync(item => item.Id == model.BatchId);
        if (batch is null)
        {
            ModelState.AddModelError(nameof(model.BatchId), "Select a valid batch.");
        }
        else
        {
            var alreadyEnrolled = await _db.Enrollments
                .AnyAsync(enrollment => enrollment.StudentProfileId == student.Id && enrollment.BatchId == batch.Id);
            if (alreadyEnrolled)
                ModelState.AddModelError(nameof(model.BatchId), "This student is already enrolled in that batch.");

            var enrollmentCount = await _db.Enrollments.CountAsync(enrollment => enrollment.BatchId == batch.Id);
            if (batch.Capacity.HasValue && enrollmentCount >= batch.Capacity.Value)
                ModelState.AddModelError(nameof(model.BatchId), "This batch is full.");
        }

        if (!ModelState.IsValid)
        {
            await transaction.RollbackAsync();
            model.StudentName = student.FullName;
            model.ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage));
            await PopulateEnrollmentOptionsAsync(model);
            await PopulateEnrolledBatchesAsync(model, student.Id);
            return View("Enrollments", model);
        }

        _db.Enrollments.Add(new Enrollment
        {
            StudentProfileId = student.Id,
            BatchId = batch!.Id
        });
        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            TempData["Message"] = "Enrollment could not be saved. The batch may have filled; refresh and try again.";
            return RedirectToAction(nameof(Enrollments), new { studentId = student.Id });
        }
        TempData["Message"] = "Student enrolled in the batch.";
        return RedirectToAction(nameof(Enrollments), new { studentId = student.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveEnrollment(int id)
    {
        var enrollment = await _db.Enrollments.FindAsync(id);
        if (enrollment is null)
            return NotFound();

        var studentId = enrollment.StudentProfileId;
        if (await _db.EnrollmentCharges.AnyAsync(charge => charge.EnrollmentId == id) ||
            await _db.EnrollmentPayments.AnyAsync(payment => payment.EnrollmentId == id))
        {
            TempData["Message"] = "This enrollment has financial history and cannot be removed individually. Keep the ledger intact or delete its course/batch if removal is required.";
            return RedirectToAction(nameof(Enrollments), new { studentId });
        }

        _db.Enrollments.Remove(enrollment);
        await _db.SaveChangesAsync();
        TempData["Message"] = "Enrollment removed.";
        return RedirectToAction(nameof(Enrollments), new { studentId });
    }

    [HttpGet]
    public async Task<IActionResult> EnrollmentFees(int enrollmentId)
    {
        var enrollment = await _db.Enrollments
            .Include(item => item.Student)
            .Include(item => item.Batch)
                .ThenInclude(batch => batch.Course)
            .Include(item => item.Charges)
            .Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.Id == enrollmentId);
        if (enrollment is null)
            return NotFound();

        var tuition = enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee;
        var charges = enrollment.Charges.Sum(charge => charge.Amount);
        var payments = enrollment.Payments.Sum(payment => payment.Amount);
        return View(new EnrollmentFeeViewModel
        {
            EnrollmentId = enrollment.Id,
            StudentProfileId = enrollment.StudentProfileId,
            StudentName = enrollment.Student.FullName,
            BatchName = enrollment.Batch.Name,
            CourseName = enrollment.Batch.Course.Name,
            DefaultFee = enrollment.Batch.Course.DefaultFee,
            FeeOverride = enrollment.FeeOverride,
            EffectiveTuition = tuition,
            MiscellaneousCharges = charges,
            Payments = payments,
            Balance = tuition + charges - payments,
            Charges = enrollment.Charges.OrderByDescending(charge => charge.ChargedUtc)
                .Select(charge => new EnrollmentChargeSummaryViewModel
                {
                    Description = charge.Description,
                    Amount = charge.Amount,
                    ChargedUtc = charge.ChargedUtc
                }).ToList(),
            PaymentHistory = enrollment.Payments.OrderByDescending(payment => payment.PaymentDate)
                .Select(payment => new EnrollmentPaymentSummaryViewModel
                {
                    Amount = payment.Amount,
                    PaymentDate = payment.PaymentDate,
                    Notes = payment.Notes
                }).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEnrollmentFee(EditEnrollmentFeeViewModel model)
    {
        var enrollment = await _db.Enrollments
            .Include(item => item.Batch)
            .ThenInclude(batch => batch.Course)
            .SingleOrDefaultAsync(item => item.Id == model.EnrollmentId);
        if (enrollment is null)
            return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["Message"] = "Enter a tuition fee of zero or greater.";
            return RedirectToAction(nameof(EnrollmentFees), new { enrollmentId = model.EnrollmentId });
        }

        enrollment.FeeOverride = model.FeeOverride;
        await _db.SaveChangesAsync();
        TempData["Message"] = model.FeeOverride.HasValue
            ? "Student-specific tuition fee saved."
            : "Student now uses the current course default tuition fee.";
        return RedirectToAction(nameof(EnrollmentFees), new { enrollmentId = model.EnrollmentId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEnrollmentCharge(AddEnrollmentChargeViewModel model)
    {
        if (!ModelState.IsValid || !await _db.Enrollments.AnyAsync(item => item.Id == model.EnrollmentId))
        {
            TempData["Message"] = "Enter a valid miscellaneous charge.";
            return RedirectToAction(nameof(EnrollmentFees), new { enrollmentId = model.EnrollmentId });
        }

        _db.EnrollmentCharges.Add(new EnrollmentCharge
        {
            EnrollmentId = model.EnrollmentId,
            Description = model.Description.Trim(),
            Amount = model.Amount,
            CreatedByAdminUserId = _userManager.GetUserId(User)!,
            ChargedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Miscellaneous charge added.";
        return RedirectToAction(nameof(EnrollmentFees), new { enrollmentId = model.EnrollmentId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordEnrollmentPayment(RecordEnrollmentPaymentViewModel model)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var enrollment = await _db.Enrollments
            .Include(item => item.Batch)
                .ThenInclude(batch => batch.Course)
            .Include(item => item.Charges)
            .Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.Id == model.EnrollmentId);
        if (enrollment is null)
            return NotFound();
        var balance = (enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee) +
                      enrollment.Charges.Sum(charge => charge.Amount) -
                      enrollment.Payments.Sum(payment => payment.Amount);
        if (!ModelState.IsValid || model.Amount > balance)
        {
            await transaction.RollbackAsync();
            TempData["Message"] = $"Payment must be positive and no greater than the current balance ({balance:C}).";
            return RedirectToAction(nameof(EnrollmentFees), new { enrollmentId = model.EnrollmentId });
        }

        _db.EnrollmentPayments.Add(new EnrollmentPayment
        {
            EnrollmentId = enrollment.Id,
            Amount = model.Amount,
            PaymentDate = model.PaymentDate.Date,
            Notes = model.Notes?.Trim(),
            ReceivedByAdminUserId = _userManager.GetUserId(User)!,
            CreatedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Message"] = "Payment recorded.";
        return RedirectToAction(nameof(EnrollmentFees), new { enrollmentId = model.EnrollmentId });
    }

    [HttpGet]
    public async Task<IActionResult> Parents()
    {
        var parents = await _db.ParentProfiles
            .OrderBy(profile => profile.FullName)
            .Select(profile => new ParentSummaryViewModel
            {
                UserId = profile.UserId,
                FullName = profile.FullName,
                Email = profile.User.Email ?? string.Empty,
                LinkedStudentCount = profile.StudentLinks.Count
            })
            .ToListAsync();
        return View(parents);
    }

    [HttpGet]
    public async Task<IActionResult> CreateParent()
    {
        var model = new CreateParentViewModel();
        await PopulateParentStudentsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateParent(CreateParentViewModel model)
    {
        var validStudentIds = await _db.StudentProfiles
            .Where(student => model.StudentProfileIds.Contains(student.Id))
            .Select(student => student.Id)
            .ToListAsync();
        if (validStudentIds.Count == 0 || validStudentIds.Count != model.StudentProfileIds.Distinct().Count())
            ModelState.AddModelError(nameof(model.StudentProfileIds), "Select at least one valid student.");
        if (await _userManager.FindByEmailAsync(model.Email) is not null)
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
        if (!ModelState.IsValid)
        {
            await PopulateParentStudentsAsync(model);
            return View(model);
        }

        var user = new Users
        {
            UserName = model.Email,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            LockoutEnabled = true
        };
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            await PopulateParentStudentsAsync(model);
            return View(model);
        }
        var roleResult = await _userManager.AddToRoleAsync(user, "Parent");
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            AddIdentityErrors(roleResult);
            await PopulateParentStudentsAsync(model);
            return View(model);
        }

        _db.ParentProfiles.Add(new ParentProfile { UserId = user.Id, FullName = model.FullName.Trim() });
        foreach (var studentProfileId in validStudentIds)
        {
            _db.ParentStudentLinks.Add(new ParentStudentLink
            {
                ParentUserId = user.Id,
                StudentProfileId = studentProfileId
            });
        }
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Message"] = "Parent account created and linked to selected students.";
        return RedirectToAction(nameof(Parents));
    }

    [HttpGet]
    public async Task<IActionResult> ParentLinks(string parentUserId)
    {
        var parent = await _db.ParentProfiles
            .Include(profile => profile.StudentLinks)
            .SingleOrDefaultAsync(profile => profile.UserId == parentUserId);
        if (parent is null)
            return NotFound();
        var model = new ParentLinksViewModel
        {
            ParentUserId = parent.UserId,
            ParentName = parent.FullName,
            StudentProfileIds = parent.StudentLinks.Select(link => link.StudentProfileId).ToList()
        };
        await PopulateParentStudentsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ParentLinks(ParentLinksViewModel model)
    {
        var parent = await _db.ParentProfiles
            .Include(profile => profile.StudentLinks)
            .SingleOrDefaultAsync(profile => profile.UserId == model.ParentUserId);
        if (parent is null)
            return NotFound();
        var selectedIds = model.StudentProfileIds.Distinct().ToList();
        var validIds = await _db.StudentProfiles
            .Where(student => selectedIds.Contains(student.Id))
            .Select(student => student.Id)
            .ToListAsync();
        if (selectedIds.Count != validIds.Count)
            ModelState.AddModelError(nameof(model.StudentProfileIds), "One or more selected students do not exist.");
        if (!ModelState.IsValid)
        {
            model.ParentName = parent.FullName;
            await PopulateParentStudentsAsync(model);
            return View(model);
        }

        _db.ParentStudentLinks.RemoveRange(parent.StudentLinks);
        foreach (var studentId in selectedIds)
            _db.ParentStudentLinks.Add(new ParentStudentLink { ParentUserId = parent.UserId, StudentProfileId = studentId });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Parent-child links updated.";
        return RedirectToAction(nameof(Parents));
    }

    [HttpGet]
    public async Task<IActionResult> ListRoles()
    {
        return View(await _roleManager.Roles.OrderBy(role => role.Name).ToListAsync());
    }

    [HttpGet]
    public IActionResult AddRoles() => View(new IdentityRole());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRoles(IdentityRole role)
    {
        var name = role.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(role.Name), "Enter a role name.");
            return View(role);
        }

        if (await _roleManager.RoleExistsAsync(name))
        {
            ModelState.AddModelError(nameof(role.Name), "That role already exists.");
            return View(role);
        }

        var result = await _roleManager.CreateAsync(new IdentityRole(name));
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(role);
        }

        TempData["Message"] = "Role created.";
        return RedirectToAction(nameof(ListRoles));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRole(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role is null)
            return NotFound();

        if (role.Name is "Admin" or "Staff" or "Student" or "Parent")
        {
            TempData["Message"] = "Built-in coaching roles cannot be deleted.";
            return RedirectToAction(nameof(ListRoles));
        }

        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            TempData["Message"] = "The role could not be deleted.";
        }
        else
        {
            TempData["Message"] = "Role deleted.";
        }
        return RedirectToAction(nameof(ListRoles));
    }

    private async Task PopulateBatchOptionsAsync(BatchViewModel model)
    {
        model.Courses = await _db.Courses
            .Where(course => course.IsActive)
            .OrderBy(course => course.Name)
            .Select(course => new SelectListItem(course.Name, course.Id.ToString()))
            .ToListAsync();
        model.StaffMembers = await _db.StaffProfiles
            .OrderBy(profile => profile.FullName)
            .Select(profile => new SelectListItem(profile.FullName, profile.UserId))
            .ToListAsync();
    }

    private async Task<List<EnrollmentRequestViewModel>> BuildEnrollmentRequestsAsync()
    {
        return await _db.EnrollmentRequests
            .Include(request => request.Student)
                .ThenInclude(student => student.User)
            .Include(request => request.Batch)
                .ThenInclude(batch => batch.Course)
            .OrderBy(request => request.Status == "Pending" ? 0 : 1)
            .ThenByDescending(request => request.RequestedUtc)
            .Select(request => new EnrollmentRequestViewModel
            {
                Id = request.Id,
                StudentProfileId = request.StudentProfileId,
                StudentName = request.Student.FullName,
                StudentEmail = request.Student.User.Email ?? string.Empty,
                BatchId = request.BatchId,
                BatchName = request.Batch.Name,
                CourseName = request.Batch.Course.Name,
                Message = request.Message,
                Status = request.Status,
                RequestedUtc = request.RequestedUtc,
                ReviewNote = request.ReviewNote,
                Capacity = request.Batch.Capacity,
                EnrollmentCount = request.Batch.Enrollments.Count
            })
            .ToListAsync();
    }

    private async Task PopulateEnrollmentOptionsAsync(EnrollStudentViewModel model)
    {
        var enrolledIds = await _db.Enrollments
            .Where(enrollment => enrollment.StudentProfileId == model.StudentProfileId)
            .Select(enrollment => enrollment.BatchId)
            .ToListAsync();
        model.AvailableBatches = await _db.Batches
            .Where(batch => !enrolledIds.Contains(batch.Id))
            .OrderBy(batch => batch.Name)
            .Select(batch => new SelectListItem(
                batch.Capacity.HasValue
                    ? $"{batch.Name} - {batch.Course.Name} ({batch.Enrollments.Count}/{batch.Capacity})"
                    : $"{batch.Name} - {batch.Course.Name}",
                batch.Id.ToString()))
            .ToListAsync();
    }

    private async Task PopulateEnrolledBatchesAsync(EnrollStudentViewModel model, int studentId)
    {
        model.EnrolledBatches = await _db.Enrollments
            .Where(enrollment => enrollment.StudentProfileId == studentId)
            .Select(enrollment => new EnrolledBatchViewModel
            {
                EnrollmentId = enrollment.Id,
                BatchId = enrollment.BatchId,
                BatchName = enrollment.Batch.Name,
                CourseName = enrollment.Batch.Course.Name,
                TuitionFee = enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee,
                MiscellaneousCharges = enrollment.Charges.Sum(charge => charge.Amount),
                Payments = enrollment.Payments.Sum(payment => payment.Amount),
                Balance = (enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee) +
                          enrollment.Charges.Sum(charge => charge.Amount) -
                          enrollment.Payments.Sum(payment => payment.Amount)
            })
            .ToListAsync();
    }

    private async Task PopulateParentStudentsAsync(CreateParentViewModel model)
    {
        model.Students = await _db.StudentProfiles
            .OrderBy(student => student.FullName)
            .Select(student => new SelectListItem(student.FullName, student.Id.ToString()))
            .ToListAsync();
    }

    private async Task PopulateParentStudentsAsync(ParentLinksViewModel model)
    {
        model.Students = await _db.StudentProfiles
            .OrderBy(student => student.FullName)
            .Select(student => new SelectListItem(student.FullName, student.Id.ToString()))
            .ToListAsync();
    }

    private async Task ValidateBatchModelAsync(BatchViewModel model)
    {
        if (model.EndDate.Date < model.StartDate.Date)
            ModelState.AddModelError(nameof(model.EndDate), "End date must be on or after the start date.");
        if (!await _db.Courses.AnyAsync(course => course.Id == model.CourseId && course.IsActive))
            ModelState.AddModelError(nameof(model.CourseId), "Select an active course.");
        if (!string.IsNullOrEmpty(model.StaffUserId) &&
            !await _db.StaffProfiles.AnyAsync(profile => profile.UserId == model.StaffUserId))
            ModelState.AddModelError(nameof(model.StaffUserId), "Select a valid staff member.");
    }

    private async Task<bool> CancelZoomMeetingsAsync(IEnumerable<string> meetingIds)
    {
        foreach (var meetingId in meetingIds.Distinct(StringComparer.Ordinal))
        {
            try
            {
                await _zoomMeetingService.DeleteMeetingAsync(meetingId, HttpContext.RequestAborted);
            }
            catch (ZoomMeetingException exception)
            {
                _logger.LogError(exception, "Could not cancel Zoom meeting {MeetingId}; retaining related course/batch records.", meetingId);
                TempData["Message"] = $"Zoom meeting {meetingId} could not be canceled. No database records were deleted; retry after resolving the Zoom error.";
                return false;
            }
        }

        return true;
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
    }
}
