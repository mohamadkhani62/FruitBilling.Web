using FruitBilling.Web.Data;
using FruitBilling.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features; 



var builder = WebApplication.CreateBuilder(args);

// 1) سرویسها
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IPermissionService, PermissionService>();

//builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 4;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.User.RequireUniqueEmail = false;
})

.AddRoles<IdentityRole>() // 👈 برای پشتیبانی از نقش‌ها
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// 🆕 ۱. اضافه کردن سیاست سراسری (اجبار به لاگین برای همه صفحات)
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// تنظیمات Cookie
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";      // 👈 به لاگین دستی اشاره می‌کند
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// 🆕 اضافه کردن Razor Pages (برای صفحات Identity)
//builder.Services.AddRazorPages();

// کنترلرها و ویوها
builder.Services.AddControllersWithViews();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100_000_000; // ۱۰۰ مگابایت
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

// 🆕 تنظیمات Kestrel برای فایل‌های بزرگ
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 100_000_000; // ۱۰۰ مگابایت
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// 2) اعمال خودکار Migration (کلید حل خطای "Invalid object name")
//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//    db.Database.Migrate();
//}

// 3) Middleware با ترتیب درست
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

//app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication(); // (احراز هویت)
app.UseAuthorization(); // (مجوز دسترسی)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
//app.MapRazorPages(); // 👈 برای صفحات Identity (Scaffolding)

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

    // ۱. ساخت نقش‌ها
    string[] roleNames = {
        "Manager", "HarvestStaff", "BasculStaff",
        "SortingStaff", "ColdStorageStaff",
        "SalesManager", "Accountant"
    };
    foreach (var roleName in roleNames)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }

    // ۲. ساخت کاربر مدیر اولیه
    string adminUsername = "admin";
    string adminPassword = "Admin@123";
    if (await userManager.FindByNameAsync(adminUsername) == null)
    {
        var admin = new IdentityUser { UserName = adminUsername, EmailConfirmed = true };
        await userManager.CreateAsync(admin, adminPassword);
        await userManager.AddToRoleAsync(admin, "Manager");
    }
}

app.Run();