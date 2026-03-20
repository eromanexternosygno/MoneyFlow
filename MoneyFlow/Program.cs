using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MoneyFlow.Context;
using MoneyFlow.Entities;
using MoneyFlow.Interfaces;
using MoneyFlow.Managers;
using MoneyFlow.Utilities;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure authentication and authorization if needed (e.g., JWT, Cookie Authentication, etc.)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";

        options.Cookie.HttpOnly = true; // Activate
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // This is for
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
    });


// Add Serilog for logging (optional, but recommended for better logging capabilities)
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information() // Minimum log level (can be adjusted as needed)
    .WriteTo.Console() // Log to console
    .WriteTo.File(@"C:\LogsAppTransaction\log-.txt",
        rollingInterval: RollingInterval.Day, // Log to file with daily rolling
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}" // Custom log format
        ).CreateLogger();

builder.Host.UseSerilog(); // Use Serilog as the logging provider

// This line adds the DbContext to the services container and configures it to use SQL Server (EF Core)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("LocalDb"));
});

// This space is for adding custom services to the Service Container
builder.Services.AddScoped<ServiceManager>();
// Add Station Manager to the Service Container
builder.Services.AddScoped<IStationManager,StationManager>();
// Add Transaction Manager to the Service Container
builder.Services.AddScoped<ITransactionManager,TransactionManager>();
// Add User Manager to the Service Container
builder.Services.AddScoped<IUserManager, UserManager>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Register UserMigrationService for execute migrations at startup, just for execute one time, then you can remove this service and the code in Program.cs
//builder.Services.AddScoped<UserMigrationService>();
builder.Services.AddScoped<PasswordService>(); // PasswordService for hashing and verifying passwords, you can use it in UserManager for secure password handling

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

//Activate Middleware for authentication
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

// Execute UserMigrationService at startup to apply pending migrations
//using (var scope = app.Services.CreateScope())
//{
//    var migrationService = scope.ServiceProvider.GetRequiredService<UserMigrationService>();
//    await migrationService.MigrateUsersAsync();
//}

app.Run();
