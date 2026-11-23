using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FruitBilling.Web.Controllers
{
    [Authorize(Roles = "Manager")]
    public class CategoriesController : Controller
    {
        private readonly AppDbContext _db;
        public CategoriesController(AppDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            var categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
            return View(categories);
        }

        [HttpGet]
        public IActionResult Create() => View();

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Category model)
        {
            if (await _db.Categories.AnyAsync(c => c.Name == model.Name))
                ModelState.AddModelError("Name", "این گونه قبلاً تعریف شده است.");

            if (!ModelState.IsValid) return View(model);

            _db.Categories.Add(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "گونه با موفقیت اضافه شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var category = await _db.Categories.FindAsync(id);
            if (category == null) return NotFound();
            return View(category);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Category model)
        {
            if (id != model.Id) return BadRequest();
            if (await _db.Categories.AnyAsync(c => c.Name == model.Name && c.Id != id))
                ModelState.AddModelError("Name", "این گونه قبلاً تعریف شده است.");

            if (!ModelState.IsValid) return View(model);

            _db.Categories.Update(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "گونه ویرایش شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _db.Categories.FindAsync(id);
            if (category != null)
            {
                var isUsed = await _db.Products.AnyAsync(p => p.CategoryId == id);
                if (isUsed)
                {
                    TempData["Warning"] = "این گونه دارای محصول است و قابل حذف نیست.";
                    return RedirectToAction(nameof(Index));
                }
                _db.Categories.Remove(category);
                await _db.SaveChangesAsync();
                TempData["Msg"] = "گونه حذف شد.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}