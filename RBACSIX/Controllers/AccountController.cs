using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RBACSIX.Models;

namespace RBACSIX.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<Users> signInManager;
        private readonly UserManager<Users> userManager;
        private readonly RoleManager<IdentityRole> roleManager;

        public AccountController(SignInManager<Users> signInManager, UserManager<Users> userManager, RoleManager<IdentityRole> roleManager)
        {
            this.signInManager = signInManager;
            this.userManager = userManager;
            this.roleManager = roleManager;
        }
        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(Users user)
        {
            if(user != null)
            {
                var userExists = await userManager.FindByEmailAsync(user.Email);
                if(userExists != null)
                {
                    var result = await signInManager.PasswordSignInAsync(user.Email, user.PasswordHash, false, false);
                    if (result.Succeeded)
                    {
                        return RedirectToAction("Index", "Home");
                    }
                    else
                    {
                        TempData["Message"] = "password is wrong";
                    }
                }
                else
                {
                    TempData["Message"] = "Email is wrong";
                }
            }
            else
            {
                TempData["Message"] = "something went is wrong";
            }
            return View(user);
        }
        public IActionResult LogOut()
        {
            return RedirectToAction("Login");
        }
        public IActionResult AccessDenied()
        {
            return View();
        }
        
    }
}
