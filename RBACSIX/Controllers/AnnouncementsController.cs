using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models.ViewModels;

namespace RBACSIX.Controllers;

[Authorize]
public class AnnouncementsController : Controller
{
    private readonly AppDbContext _db;

    public AnnouncementsController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Admin");
        var visibleBatchIds = Array.Empty<int>();
        if (User.IsInRole("Staff"))
        {
            visibleBatchIds = await _db.Batches
                .Where(batch => batch.StaffUserId == userId)
                .Select(batch => batch.Id)
                .ToArrayAsync();
        }
        else if (User.IsInRole("Student"))
        {
            visibleBatchIds = await _db.Enrollments
                .Where(enrollment => enrollment.Student.UserId == userId)
                .Select(enrollment => enrollment.BatchId)
                .ToArrayAsync();
        }

        var announcements = await _db.Announcements
            .Where(announcement =>
                isAdmin ||
                announcement.BatchId == null ||
                announcement.BatchId.HasValue && visibleBatchIds.Contains(announcement.BatchId.Value))
            .OrderByDescending(announcement => announcement.CreatedUtc)
            .Select(announcement => new AnnouncementSummaryViewModel
            {
                Id = announcement.Id,
                Title = announcement.Title,
                Body = announcement.Body,
                BatchName = announcement.Batch == null ? null : announcement.Batch.Name,
                IsGlobal = announcement.BatchId == null,
                CreatedUtc = announcement.CreatedUtc
            })
            .ToListAsync();

        return View(new AnnouncementListViewModel
        {
            Announcements = announcements,
            CanCreateGlobal = isAdmin
        });
    }
}
