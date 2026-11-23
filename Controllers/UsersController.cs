using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using FruitBilling.Web.Data;

namespace FruitBilling.Web.Controllers
{
    [Authorize(Roles = "Manager")]
    public class UsersController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AppDbContext _db;

        public UsersController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, AppDbContext db)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.ToListAsync();
            var userRoles = new Dictionary<string, List<string>>();
            foreach (var u in users)
            {
                userRoles[u.Id] = (await _userManager.GetRolesAsync(u)).ToList();
            }
            ViewBag.UserRoles = userRoles;
            return View(users);
        }

        // ==========================================
        // ساخت کاربر جدید (فقط با نام کاربری)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Roles = new MultiSelectList(await _roleManager.Roles.ToListAsync(), "Name", "Name");
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Roles = new MultiSelectList(await _roleManager.Roles.ToListAsync(), "Name", "Name", model.SelectedRoles);
                return View(model);
            }

            // بررسی تکراری نبودن نام کاربری
            var existing = await _userManager.FindByNameAsync(model.Username);
            if (existing != null)
            {
                ModelState.AddModelError("Username", "این نام کاربری قبلاً ثبت شده است.");
                ViewBag.Roles = new MultiSelectList(await _roleManager.Roles.ToListAsync(), "Name", "Name", model.SelectedRoles);
                return View(model);
            }

            var user = new IdentityUser
            {
                UserName = model.Username,
                Email = null, // بدون ایمیل
                EmailConfirmed = false
            };
            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                if (model.SelectedRoles != null && model.SelectedRoles.Length > 0)
                    await _userManager.AddToRolesAsync(user, model.SelectedRoles);

                TempData["Msg"] = $"کاربر «{model.Username}» با موفقیت ساخته شد.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            ViewBag.Roles = new MultiSelectList(await _roleManager.Roles.ToListAsync(), "Name", "Name", model.SelectedRoles);
            return View(model);
        }

        // ==========================================
        // ویرایش نقش‌ها
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> EditRoles(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var userRoles = await _userManager.GetRolesAsync(user);
            var model = new EditUserRolesViewModel
            {
                UserId = user.Id,
                Username = user.UserName,
                SelectedRoles = userRoles.ToArray()
            };
            ViewBag.Roles = new MultiSelectList(await _roleManager.Roles.ToListAsync(), "Name", "Name", userRoles);
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRoles(EditUserRolesViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            var currentRoles = await _userManager.GetRolesAsync(user);
            var newRoles = model.SelectedRoles ?? Array.Empty<string>();

            var rolesToRemove = currentRoles.Except(newRoles).ToList();
            if (rolesToRemove.Any())
                await _userManager.RemoveFromRolesAsync(user, rolesToRemove);

            var rolesToAdd = newRoles.Except(currentRoles).ToList();
            if (rolesToAdd.Any())
                await _userManager.AddToRolesAsync(user, rolesToAdd);

            TempData["Msg"] = $"نقش‌های کاربر «{user.UserName}» با موفقیت به‌روزرسانی شد.";
            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // تغییر نام کاربری
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> EditUsername(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var model = new EditUserUsernameViewModel
            {
                UserId = user.Id,
                Username = user.UserName ?? ""
            };
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUsername(EditUserUsernameViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            // بررسی تکراری نبودن نام کاربری
            var existingUser = await _userManager.FindByNameAsync(model.Username);
            if (existingUser != null && existingUser.Id != model.UserId)
            {
                ModelState.AddModelError("Username", "این نام کاربری قبلاً برای کاربر دیگری ثبت شده است.");
                return View(model);
            }

            user.UserName = model.Username;
            user.NormalizedUserName = model.Username.ToUpperInvariant();

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                TempData["Msg"] = "نام کاربری با موفقیت تغییر کرد.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            return View(model);
        }

        // ==========================================
        // بازنشانی رمز عبور
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> ResetPassword(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var model = new ResetPasswordViewModel
            {
                UserId = user.Id,
                Username = user.UserName
            };
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

            if (result.Succeeded)
            {
                TempData["Msg"] = $"رمز عبور کاربر «{user.UserName}» با موفقیت تغییر کرد.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            return View(model);
        }

        // ==========================================
        // حذف کاربر
        // ==========================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            if (user.UserName == "admin")
            {
                TempData["Warning"] = "امکان حذف کاربر مدیر اصلی وجود ندارد.";
                return RedirectToAction(nameof(Index));
            }

            await _userManager.DeleteAsync(user);
            TempData["Msg"] = $"کاربر «{user.UserName}» با موفقیت حذف شد.";
            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // مدیریت مجوز ورود اکسل (GET)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> ManageImportPermissions(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var userPermissions = await _db.UserImportPermissions
                .Where(p => p.UserId == id)
                .ToListAsync();

            ViewBag.UserId = user.Id;
            ViewBag.Username = user.UserName;
            ViewBag.SortingExitPermits = userPermissions.Any(p => p.ResourceName == "SortingExitPermits" && p.CanImport);
            ViewBag.ExitPermits = userPermissions.Any(p => p.ResourceName == "ExitPermits" && p.CanImport);

            return View();
        }

        // ==========================================
        // ذخیره مجوز ورود اکسل (POST)
        // ==========================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ManageImportPermissions(string userId, bool canImportSorting, bool canImportFarm)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            // پاک کردن مجوزهای قبلی این کاربر
            var existing = await _db.UserImportPermissions
                .Where(p => p.UserId == userId)
                .ToListAsync();
            _db.UserImportPermissions.RemoveRange(existing);

            // افزودن مجوزهای جدید
            if (canImportSorting)
            {
                _db.UserImportPermissions.Add(new UserImportPermission
                {
                    UserId = userId,
                    ResourceName = "SortingExitPermits",
                    CanImport = true
                });
            }

            if (canImportFarm)
            {
                _db.UserImportPermissions.Add(new UserImportPermission
                {
                    UserId = userId,
                    ResourceName = "ExitPermits",
                    CanImport = true
                });
            }

            await _db.SaveChangesAsync();
            TempData["Msg"] = $"مجوز ورود اکسل کاربر «{user.UserName}» به‌روزرسانی شد.";
            return RedirectToAction(nameof(Index));
        }
    }

    // ==========================================
    // ViewModelها
    // ==========================================
    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "نام کاربری الزامی است")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "نام کاربری باید بین ۳ تا ۵۰ کاراکتر باشد")]
        [Display(Name = "نام کاربری")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "رمز عبور الزامی است")]
        [StringLength(100, MinimumLength = 4)]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور")]
        public string Password { get; set; } = "";

        [Display(Name = "نقش‌ها")]
        public string[]? SelectedRoles { get; set; }
    }

    public class EditUserRolesViewModel
    {
        public string UserId { get; set; } = "";
        public string? Username { get; set; }

        [Display(Name = "نقش‌ها")]
        public string[]? SelectedRoles { get; set; }
    }

    public class EditUserUsernameViewModel
    {
        public string UserId { get; set; } = "";

        [Required(ErrorMessage = "نام کاربری الزامی است")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "نام کاربری باید بین ۳ تا ۵۰ کاراکتر باشد")]
        [Display(Name = "نام کاربری جدید")]
        public string Username { get; set; } = "";
    }

    public class ResetPasswordViewModel
    {
        public string UserId { get; set; } = "";
        public string? Username { get; set; }

        [Required(ErrorMessage = "رمز عبور جدید الزامی است")]
        [StringLength(100, MinimumLength = 4, ErrorMessage = "رمز باید حداقل ۴ کاراکتر باشد")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور جدید")]
        public string NewPassword { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "تکرار رمز عبور")]
        [Compare("NewPassword", ErrorMessage = "رمز عبور و تکرار آن یکسان نیستند")]
        public string ConfirmPassword { get; set; } = "";
    }
}