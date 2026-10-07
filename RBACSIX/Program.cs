using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(Options=> Options.UseSqlServer(
    builder.Configuration.GetConnectionString("DefaultConnection")
  ));
builder.Services.AddIdentity<Users, IdentityRole>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(Options =>
{
    Options.AccessDeniedPath = "/Account/AccessDenied";
    Options.LoginPath = "/Account/Login";
} );
var app = builder.Build();
using(var scope = app.Services.CreateScope())
{
    try
    {
        var services = scope.ServiceProvider;
        var userManager = services.GetRequiredService<UserManager<Users>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        string[] roles = { "Admin", "Staff", "Student" };
        foreach(var role in roles)
        {
            var roleExists = await roleManager.RoleExistsAsync(role);
            if(!roleExists)
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
        string email = "example3@xyz";
        var userExists = await userManager.FindByEmailAsync(email);
        if(userExists == null)
        {
            var user = new Users()
            {
                Email = email,
                UserName = email,
                EmailConfirmed = false
            };
            string password = "example@#123";
            var result = await userManager.CreateAsync(user, password);
            if(result.Succeeded)
            {
                await userManager.AddToRoleAsync(user,"Staff");
            }
        }
    }
    catch(Exception ex)
    {
        Console.WriteLine("Unable to process further" + ex.ToString());
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
