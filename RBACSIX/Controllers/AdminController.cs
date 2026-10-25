using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;

namespace RBACSIX.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<Users> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AdminController(
        AppDbContext db,
        UserManager<Users> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<IActionResult> Index()
    {
        return View(new AdminDashboardViewModel
        {
            StudentCount = await _db.StudentProfiles.CountAsync(),
            StaffCount = await _db.StaffProfiles.CountAsync(),
            CourseCount = await _db.Courses.CountAsync(),
            BatchCount = await _db.Batches.CountAsync()
        });
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

    public async Task<IActionResult> Students()
    {
        var students = await _db.StudentProfiles
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
        return View(students);
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
                Details = profile.SubjectSpecialty
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
            IsActive = model.IsActive
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Course created.";
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
                StudentCount = batch.Enrollments.Count
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
        if (model.EndDate.Date < model.StartDate.Date)
            ModelState.AddModelError(nameof(model.EndDate), "End date must be on or after the start date.");

        if (!await _db.Courses.AnyAsync(course => course.Id == model.CourseId && course.IsActive))
            ModelState.AddModelError(nameof(model.CourseId), "Select an active course.");

        if (!string.IsNullOrEmpty(model.StaffUserId) &&
            !await _db.StaffProfiles.AnyAsync(profile => profile.UserId == model.StaffUserId))
            ModelState.AddModelError(nameof(model.StaffUserId), "Select a valid staff member.");

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
            ScheduleDescription = model.ScheduleDescription?.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Batch created.";
        return RedirectToAction(nameof(Batches));
    }

    public async Task<IActionResult> Enrollments(int studentId)
    {
        var student = await _db.StudentProfiles
            .Include(profile => profile.Enrollments)
                .ThenInclude(enrollment => enrollment.Batch)
                    .ThenInclude(batch => batch.Course)
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
                CourseName = enrollment.Batch.Course.Name
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

        var batchExists = await _db.Batches.AnyAsync(batch => batch.Id == model.BatchId);
        if (!batchExists)
            ModelState.AddModelError(nameof(model.BatchId), "Select a valid batch.");

        var alreadyEnrolled = await _db.Enrollments
            .AnyAsync(enrollment => enrollment.StudentProfileId == model.StudentProfileId &&
                                    enrollment.BatchId == model.BatchId);
        if (alreadyEnrolled)
            ModelState.AddModelError(nameof(model.BatchId), "This student is already enrolled in that batch.");

        if (!ModelState.IsValid)
        {
            model.StudentName = student.FullName;
            await PopulateEnrollmentOptionsAsync(model);
            model.EnrolledBatches = await _db.Enrollments
                .Where(enrollment => enrollment.StudentProfileId == student.Id)
                .Select(enrollment => new EnrolledBatchViewModel
                {
                    EnrollmentId = enrollment.Id,
                    BatchId = enrollment.BatchId,
                    BatchName = enrollment.Batch.Name,
                    CourseName = enrollment.Batch.Course.Name
                })
                .ToListAsync();
            return View("Enrollments", model);
        }

        _db.Enrollments.Add(new Enrollment
        {
            StudentProfileId = model.StudentProfileId,
            BatchId = model.BatchId
        });
        await _db.SaveChangesAsync();
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
        _db.Enrollments.Remove(enrollment);
        await _db.SaveChangesAsync();
        TempData["Message"] = "Enrollment removed.";
        return RedirectToAction(nameof(Enrollments), new { studentId });
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

        if (role.Name is "Admin" or "Staff" or "Student")
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

    private async Task PopulateEnrollmentOptionsAsync(EnrollStudentViewModel model)
    {
        var enrolledIds = await _db.Enrollments
            .Where(enrollment => enrollment.StudentProfileId == model.StudentProfileId)
            .Select(enrollment => enrollment.BatchId)
            .ToListAsync();
        model.AvailableBatches = await _db.Batches
            .Where(batch => !enrolledIds.Contains(batch.Id))
            .OrderBy(batch => batch.Name)
            .Select(batch => new SelectListItem($"{batch.Name} - {batch.Course.Name}", batch.Id.ToString()))
            .ToListAsync();
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
    }
}
