using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Models;

namespace RBACSIX.Data
{
    public class AppDbContext: IdentityDbContext
    {
        public AppDbContext(DbContextOptions opt): base(opt) { }
        public DbSet<Users>users { get; set; }
        public DbSet<Users> roles { get; set; }
    }
}
