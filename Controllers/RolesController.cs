using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FruitBilling.Web.Controllers
{
    [Authorize(Roles = "Manager")]
    public class RolesController : Controller
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AppDbContext _db;

        public RolesController(RoleManager<IdentityRole> roleManager, AppDbContext db)
        {
            _roleManager = roleManager;
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var roles = await _roleManager.Roles.ToListAsync();
            return View(roles);
        }

        [HttpGet]
        public async Task<IActionResult> Permissions(string roleId)
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role == null) return NotFound();

            ViewBag.RoleId = roleId;
            ViewBag.RoleName = role.Name;

            var resources = new List<string> {
                "ExitPermits", "SortingExitPermits", "Farms", "Plots", "Trees", "Roads"
            };
            ViewBag.Resources = resources;

            var permissions = await _db.RolePermissions
                .Include(p => p.RowConditions)
                .Where(p => p.RoleId == roleId)
                .ToListAsync();

            ViewBag.Permissions = permissions;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePermissions(string roleId, List<RolePermissionInput> permissions)
        {
            var existing = await _db.RolePermissions
                .Where(p => p.RoleId == roleId)
                .Include(p => p.RowConditions)
                .ToListAsync();

            _db.RolePermissions.RemoveRange(existing);
            await _db.SaveChangesAsync();

            if (permissions != null)
            {
                foreach (var p in permissions)
                {
                    if (p.AccessLevel != "None" && !string.IsNullOrEmpty(p.FieldName))
                    {
                        var rp = new RolePermission
                        {
                            RoleId = roleId,
                            ResourceName = p.ResourceName,
                            FieldName = p.FieldName,
                            AccessLevel = p.AccessLevel
                        };
                        _db.RolePermissions.Add(rp);
                    }
                }
                await _db.SaveChangesAsync();
            }

            TempData["Msg"] = "دسترسی‌ها با موفقیت ذخیره شد.";
            return RedirectToAction(nameof(Permissions), new { roleId = roleId });
        }
    }

    public class RolePermissionInput
    {
        public string ResourceName { get; set; } = "";
        public string FieldName { get; set; } = "";
        public string AccessLevel { get; set; } = "None";
    }
}