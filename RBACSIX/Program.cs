using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RBACSIX.Data;
using RBACSIX.Models;
using RBACSIX.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(Options=> Options.UseSqlServer(
    builder.Configuration.GetConnectionString("DefaultConnection")
  ));
builder.Services.AddIdentity<Users, IdentityRole>(options =>
{
    options.Lockout.AllowedForNewUsers = true;
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();
builder.Services.Configure<ZoomOptions>(builder.Configuration.GetSection("Zoom"));
builder.Services.AddHttpClient<IZoomMeetingService, ZoomMeetingService>();
builder.Services.AddHostedService<ScheduledClassCleanupService>();
builder.Services.Configure<PrivateFileStorageOptions>(builder.Configuration.GetSection("Files"));
builder.Services.AddSingleton<IPrivateFileStorage, PrivateFileStorage>();
var coachingTimeZoneId = builder.Configuration["Coaching:TimeZoneId"] ?? "Asia/Kolkata";
var coachingTimeZone = TimeZoneInfo.FindSystemTimeZoneById(coachingTimeZoneId);
builder.Services.AddSingleton(coachingTimeZone);
builder.Services.ConfigureApplicationCookie(Options =>
{
    Options.AccessDeniedPath = "/Account/AccessDenied";
    Options.LoginPath = "/Account/Login";
} );
var app = builder.Build();

var zoomOptions = app.Services.GetRequiredService<IOptions<ZoomOptions>>().Value;
var zoomConfiguration = new[] { zoomOptions.AccountId, zoomOptions.ClientId, zoomOptions.ClientSecret };
if (zoomConfiguration.Any(value => !string.IsNullOrWhiteSpace(value)) &&
    zoomConfiguration.Any(string.IsNullOrWhiteSpace))
{
    throw new InvalidOperationException(
        "Zoom configuration is incomplete. Configure Zoom:AccountId, Zoom:ClientId, and Zoom:ClientSecret together.");
}
if (zoomConfiguration.All(string.IsNullOrWhiteSpace))
{
    app.Logger.LogInformation(
        "Zoom class scheduling is disabled until Zoom:AccountId, Zoom:ClientId, and Zoom:ClientSecret are configured.");
}

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = { "Admin", "Staff", "Student", "Parent" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var result = await roleManager.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Unable to create the required '{role}' role: {errors}");
            }
        }
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

app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
