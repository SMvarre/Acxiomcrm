using AcxiomCRM.Data;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// MVC + Razor: views for the browser UI
builder.Services.AddControllersWithViews(o =>
{
    // Require antiforgery token on every POST reaching a controller action -
    // CSRF protection by default instead of remembering it per action.
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddControllers(); // for /api controllers

// EF Core 8 + SQL Server LocalDB, Code First
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ASP.NET Core Identity with the required password & lockout policy.
// AddEntityFrameworkStores persists users/roles/hashes inside AppDbContext.
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 8;
    o.Password.RequireUppercase = true;
    o.Password.RequireLowercase = true;
    o.Password.RequireDigit = true;
    o.Password.RequireNonAlphanumeric = true;
    o.Lockout.MaxFailedAccessAttempts = 5;   // lock after 5 bad passwords
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.User.RequireUniqueEmail = true;
    o.SignIn.RequireConfirmedEmail = false;  // demo: allow seeded users to log in
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
    // Secure cookies: HTTPS-only transmission so the auth cookie cannot be
    // sniffed over plaintext HTTP.
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.HttpOnly = true;                 // JS cannot read the cookie (XSS mitigation)
    o.Cookie.SameSite = SameSiteMode.Lax;     // CSRF hardening
    o.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    o.SlidingExpiration = true;
});

builder.Services.AddSession(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.IdleTimeout = TimeSpan.FromMinutes(20);
});

// App services (scoped = one instance per HTTP request, sharing the DbContext)
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<IOpportunityService, OpportunityService>();
builder.Services.AddScoped<IFollowUpService, FollowUpService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Never leak stack traces / DB error details to end users: a generic error page
// is shown instead, while the exception is logged on the server side.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Dashboard}/{id?}");

// Attribute-routed API controllers under /api/*
app.MapControllers();

// Seed roles + demo users + sample data on startup.
using (var scope = app.Services.CreateScope())
{
    await SeedData.SeedAsync(scope.ServiceProvider);
}

app.Run();
