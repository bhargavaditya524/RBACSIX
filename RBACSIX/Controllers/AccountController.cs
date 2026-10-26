using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Models.ViewModels;
using System.Data;

namespace RBACSIX.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<Users> _signInManager;
    private readonly UserManager<Users> _userManager;
    private readonly AppDbContext _db;

    public AccountController(
        SignInManager<Users> signInManager,
        UserManager<Users> userManager,
        AppDbContext db)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> SetupAdmin()
    {
        if (await HasAdministratorAsync())
            return NotFound();

        return View(new FirstAdminSetupViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetupAdmin(FirstAdminSetupViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (await HasAdministratorAsync())
            return NotFound();

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
            await _userManager.DeleteAsync(user);
            AddIdentityErrors(roleResult);
            return View(model);
        }

        await transaction.CommitAsync();
        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Admin");
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        ViewData["SetupAvailable"] = !await HasAdministratorAsync();
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["SetupAvailable"] = !await HasAdministratorAsync();
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
            return View(model);

        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        var authenticatedUser = await _userManager.FindByEmailAsync(model.Email);
        if (authenticatedUser is not null &&
            await _userManager.IsInRoleAsync(authenticatedUser, "Staff") &&
            !await _db.StaffProfiles.AnyAsync(profile =>
                profile.UserId == authenticatedUser.Id && profile.IsApproved))
        {
            await _signInManager.SignOutAsync();
            ModelState.AddModelError(string.Empty, "Your Staff registration is awaiting Admin approval.");
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        var user = authenticatedUser;
        if (user is null)
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return RedirectToAction("Index", "Admin");
        if (await _userManager.IsInRoleAsync(user, "Staff"))
            return RedirectToAction("Index", "Staff");
        if (await _userManager.IsInRoleAsync(user, "Student"))
            return RedirectToAction("Index", "Student");
        if (await _userManager.IsInRoleAsync(user, "Parent"))
            return RedirectToAction("Index", "Parent");

        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult RegisterStudent() => View(new StudentRegistrationViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterStudent(StudentRegistrationViewModel model)
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
        var created = await _userManager.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            AddIdentityErrors(created);
            return View(model);
        }
        var assigned = await _userManager.AddToRoleAsync(user, "Student");
        if (!assigned.Succeeded)
        {
            AddIdentityErrors(assigned);
            return View(model);
        }
        _db.StudentProfiles.Add(new StudentProfile
        {
            UserId = user.Id,
            FullName = model.FullName.Trim(),
            GuardianName = model.GuardianName?.Trim()
        });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Student");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult RegisterStaff() => View(new StaffRegistrationViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterStaff(StaffRegistrationViewModel model)
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
        var created = await _userManager.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            AddIdentityErrors(created);
            return View(model);
        }
        var assigned = await _userManager.AddToRoleAsync(user, "Staff");
        if (!assigned.Succeeded)
        {
            AddIdentityErrors(assigned);
            return View(model);
        }
        _db.StaffProfiles.Add(new StaffProfile
        {
            UserId = user.Id,
            FullName = model.FullName.Trim(),
            SubjectSpecialty = model.SubjectSpecialty?.Trim(),
            IsApproved = false
        });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return View("StaffRegistrationPending");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    private async Task<bool> HasAdministratorAsync()
    {
        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        return admins.Count > 0;
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
    }
}
