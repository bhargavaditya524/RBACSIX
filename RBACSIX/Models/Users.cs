using Microsoft.AspNetCore.Identity;

namespace RBACSIX.Models
{
    public class Users: IdentityUser
    {
        public StudentProfile? StudentProfile { get; set; }
        public StaffProfile? StaffProfile { get; set; }
        public ParentProfile? ParentProfile { get; set; }
    }
}