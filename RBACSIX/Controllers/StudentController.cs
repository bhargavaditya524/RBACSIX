using Microsoft.AspNetCore.Mvc;

namespace RBACSIX.Controllers
{
    public class StudentController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
