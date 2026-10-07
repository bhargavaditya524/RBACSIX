using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RBACSIX.Models;

namespace RBACSIX.Controllers
{
    [Authorize(Roles ="Admin")]
    public class AdminController : Controller
    {
        private readonly SignInManager<Users> signInManager;
        private readonly UserManager<Users> userManager;
        private readonly RoleManager<IdentityRole> roleManager;

        public AdminController(SignInManager<Users> signInManager, UserManager<Users> userManager, RoleManager<IdentityRole> roleManager)
        {
            this.signInManager = signInManager;
            this.userManager = userManager;
            this.roleManager = roleManager;
        }
        [HttpGet]
        public async Task<IActionResult> ListRoles()
        {
            var totalRoles =  roleManager.Roles.ToList();
            return View(totalRoles);
        }

        [HttpGet]
        public IActionResult AddRoles()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> AddRoles(IdentityRole role)
        {
            if(role != null)
            {
                //check karo role pehle se to exists nahi karta hai
                var roleExists = await roleManager.RoleExistsAsync(role.Name);
                if(!roleExists)
                {
                  var result =   await roleManager.CreateAsync(role);
                    if(result.Succeeded)
                    {
                        return RedirectToAction("ListRoles");
                    }
                }
                else
                {
                    TempData["Message"] = "Role Already Exists";
                }
            }
            else
            {
                TempData["Message"] = "something went Wrong";
            }
            return RedirectToAction("ListRoles");
        }

        public async Task<IActionResult> Delete(string id)
        {
            var totolRoles = roleManager.Roles.ToList();
            foreach(var role in totolRoles)
            {
               if(role.Id == id)
                {
                    await roleManager.DeleteAsync(role);
                    TempData["Message"] = "Successfully Deleted";
                    return RedirectToAction("ListRoles");
                }
            }
            TempData["Message"] = "Role not Deleted";
              return RedirectToAction("ListRoles");

        }
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Register(Users user)
        {
            if(user != null)
            {
                var userExists = await userManager.FindByEmailAsync(user.Email);
                if(userExists == null)
                {
                    var Adduser = new Users()
                    {
                        Email = user.Email,
                        UserName = user.Email,
                        EmailConfirmed = false
                    };
                    var result = await userManager.CreateAsync(Adduser, user.PasswordHash);
                    if(result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(Adduser, "Staff");
                        TempData["Message"] = "User & Roles Added Successfully";

                    }

                }
                else
                {
                    TempData["Message"] = "User already exists";
                }
            }
            else
            {
                TempData["Message"] = "Something went wrong!";
            }
            return View(user);
        }
    }
}
