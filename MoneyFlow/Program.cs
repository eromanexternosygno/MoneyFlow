using Microsoft.EntityFrameworkCore;
using MoneyFlow.Context;
using MoneyFlow.Managers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// This line adds the DbContext to the services container and configures it to use SQL Server (EF Core)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("LocalDb"));
});

// This space is for adding custom services to the Service Container
builder.Services.AddScoped<ServiceManager>();
// Add Station Manager to the Service Container
builder.Services.AddScoped<StationManager>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
