using Microsoft.AspNetCore.Mvc;

namespace RBACSIX.Controllers
{
    public class StaffController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
