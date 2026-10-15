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

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        var user = await _userManager.FindByEmailAsync(model.Email);
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

        return RedirectToAction("Index", "Home");
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
