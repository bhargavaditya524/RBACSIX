using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;

namespace RBACSIX.Controllers;

[Authorize(Roles = "Parent")]
public class ParentController : Controller
{
    private readonly AppDbContext _db;

    public ParentController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var parent = await _db.ParentProfiles
            .Where(profile => profile.UserId == userId)
            .Select(profile => new ParentDashboardViewModel
            {
                ParentName = profile.FullName,
                Children = profile.StudentLinks
                    .OrderBy(link => link.Student.FullName)
                    .Select(link => new ParentChildDashboardViewModel
                    {
                        StudentProfileId = link.StudentProfileId,
                        StudentName = link.Student.FullName,
                        Batches = link.Student.Enrollments
                            .OrderBy(enrollment => enrollment.Batch.StartDate)
                            .Select(enrollment => new StudentBatchViewModel
                            {
                                BatchName = enrollment.Batch.Name,
                                CourseName = enrollment.Batch.Course.Name,
                                StaffName = enrollment.Batch.StaffUser == null ||
                                            enrollment.Batch.StaffUser.StaffProfile == null
                                    ? null
                                    : enrollment.Batch.StaffUser.StaffProfile.FullName,
                                ScheduleDescription = enrollment.Batch.ScheduleDescription,
                                StartDate = enrollment.Batch.StartDate,
                                EndDate = enrollment.Batch.EndDate,
                                PresentCount = enrollment.AttendanceRecords.Count(record =>
                                    record.IsPresent &&
                                    !enrollment.LeaveRequests.Any(request =>
                                        request.Status == "Approved" &&
                                        request.StartDate <= record.Date &&
                                        request.EndDate >= record.Date)),
                                AttendanceCount = enrollment.AttendanceRecords.Count(record =>
                                    !enrollment.LeaveRequests.Any(request =>
                                        request.Status == "Approved" &&
                                        request.StartDate <= record.Date &&
                                        request.EndDate >= record.Date)),
                                TuitionFee = enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee,
                                MiscellaneousCharges = enrollment.Charges.Sum(charge => charge.Amount),
                                Payments = enrollment.Payments.Sum(payment => payment.Amount),
                                Balance = (enrollment.FeeOverride ?? enrollment.Batch.Course.DefaultFee) +
                                          enrollment.Charges.Sum(charge => charge.Amount) -
                                          enrollment.Payments.Sum(payment => payment.Amount),
                                Results = enrollment.Results
                                    .OrderByDescending(result => result.ExamDate)
                                    .Select(result => new StudentResultSummaryViewModel
                                    {
                                        ExamName = result.ExamName,
                                        ExamDate = result.ExamDate,
                                        MarksObtained = result.MarksObtained,
                                        MaxMarks = result.MaxMarks
                                    }).ToList()
                            }).ToList()
                    }).ToList()
            })
            .SingleOrDefaultAsync();

        return parent is null
            ? NotFound("Your parent profile is not linked to this account. Contact the coaching center administrator.")
            : View(parent);
    }

    [HttpGet]
    public async Task<IActionResult> LeaveRequests(int? studentProfileId)
    {
        var parentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var linkedStudentIds = await _db.ParentStudentLinks
            .Where(link => link.ParentUserId == parentId)
            .Select(link => link.StudentProfileId)
            .ToListAsync();
        if (studentProfileId.HasValue && !linkedStudentIds.Contains(studentProfileId.Value))
            return NotFound();

        var selectedIds = studentProfileId.HasValue
            ? new[] { studentProfileId.Value }
            : linkedStudentIds.ToArray();
        var model = new LeaveRequestsViewModel
        {
            PageTitle = "Leave requests for your children",
            SelectedStudentProfileId = studentProfileId,
            Enrollments = await _db.Enrollments
                .Where(enrollment => linkedStudentIds.Contains(enrollment.StudentProfileId) &&
                    (!studentProfileId.HasValue || enrollment.StudentProfileId == studentProfileId.Value))
                .OrderBy(enrollment => enrollment.Student.FullName)
                .ThenBy(enrollment => enrollment.Batch.Name)
                .Select(enrollment => new SelectListItem(
                    $"{enrollment.Student.FullName} — {enrollment.Batch.Name}",
                    enrollment.Id.ToString()))
                .ToListAsync(),
            Requests = await BuildLeaveRequestListAsync(selectedIds)
        };
        return View("~/Views/Shared/LeaveRequests.cshtml", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestLeave(CreateLeaveRequestViewModel model)
    {
        var parentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var linkedStudentId = await _db.ParentStudentLinks
            .Where(link => link.ParentUserId == parentId &&
                           link.Student.Enrollments.Any(enrollment => enrollment.Id == model.EnrollmentId))
            .Select(link => (int?)link.StudentProfileId)
            .SingleOrDefaultAsync();
        if (linkedStudentId is null)
            return NotFound();
        model.StudentProfileId = linkedStudentId;

        if (model.StartDate.Date > model.EndDate.Date)
            ModelState.AddModelError(nameof(model.EndDate), "End date must be on or after the start date.");
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage));
            return RedirectToAction(nameof(LeaveRequests), new { studentProfileId = model.StudentProfileId });
        }

        _db.LeaveRequests.Add(new LeaveRequest
        {
            EnrollmentId = model.EnrollmentId,
            RequestedByUserId = parentId!,
            StartDate = model.StartDate.Date,
            EndDate = model.EndDate.Date,
            Reason = model.Reason.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = "Leave request submitted.";
        return RedirectToAction(nameof(LeaveRequests), new { studentProfileId = model.StudentProfileId });
    }

    private async Task<List<LeaveRequestItemViewModel>> BuildLeaveRequestListAsync(int[] studentIds)
    {
        return await _db.LeaveRequests
            .Where(request => studentIds.Contains(request.Enrollment.StudentProfileId))
            .OrderByDescending(request => request.CreatedUtc)
            .Select(request => new LeaveRequestItemViewModel
            {
                Id = request.Id,
                EnrollmentId = request.EnrollmentId,
                StudentName = request.Enrollment.Student.FullName,
                BatchName = request.Enrollment.Batch.Name,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Reason = request.Reason,
                Status = request.Status,
                RequestedByName = _db.StudentProfiles
                    .Where(profile => profile.UserId == request.RequestedByUserId)
                    .Select(profile => profile.FullName)
                    .FirstOrDefault() ??
                    _db.ParentProfiles
                        .Where(profile => profile.UserId == request.RequestedByUserId)
                        .Select(profile => profile.FullName)
                        .FirstOrDefault() ?? "You",
                ReviewNote = request.ReviewNote,
                CreatedUtc = request.CreatedUtc
            })
            .ToListAsync();
    }
}
