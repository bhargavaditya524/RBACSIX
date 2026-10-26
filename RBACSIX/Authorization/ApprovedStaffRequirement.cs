using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;

namespace RBACSIX.Authorization;

public sealed class ApprovedStaffRequirement : IAuthorizationRequirement
{
}

public sealed class ApprovedStaffHandler : AuthorizationHandler<ApprovedStaffRequirement>
{
    private readonly AppDbContext _db;

    public ApprovedStaffHandler(AppDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApprovedStaffRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId) &&
            await _db.StaffProfiles.AnyAsync(profile =>
                profile.UserId == userId && profile.IsApproved))
        {
            context.Succeed(requirement);
        }
    }
}
