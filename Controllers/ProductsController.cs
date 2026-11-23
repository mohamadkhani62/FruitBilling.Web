using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FruitBilling.Web.Controllers
{
    [Authorize(Roles = "Manager")]
    public class ProductsController : Controller
    {
        private readonly AppDbContext _db;
        public ProductsController(AppDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            var products = await _db.Products
                .Include(p => p.Category)
                .OrderBy(p => p.Name)
                .ToListAsync();
            return View(products);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // 🆕 ارسال لیست گونه‌ها به ویو
            ViewBag.Categories = new SelectList(await _db.Categories.ToListAsync(), "Id", "Name");
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Product model)
        {
            if (await _db.Products.AnyAsync(p => p.Name == model.Name))
                ModelState.AddModelError("Name", "این محصول قبلاً تعریف شده است.");

            if (!ModelState.IsValid)
            {
                // 🆕 اگر خطا بود، دوباره لیست را پر کن
                ViewBag.Categories = new SelectList(await _db.Categories.ToListAsync(), "Id", "Name", model.CategoryId);
                return View(model);
            }

            _db.Products.Add(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "محصول با موفقیت اضافه شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var product = await _db.Products.FindAsync(id);
            if (product == null) return NotFound();

            // 🆕 ارسال لیست گونه‌ها به ویو
            ViewBag.Categories = new SelectList(await _db.Categories.ToListAsync(), "Id", "Name", product.CategoryId);
            return View(product);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Product model)
        {
            if (id != model.Id) return BadRequest();

            if (await _db.Products.AnyAsync(p => p.Name == model.Name && p.Id != id))
                ModelState.AddModelError("Name", "این محصول قبلاً تعریف شده است.");

            if (!ModelState.IsValid)
            {
                ViewBag.Categories = new SelectList(await _db.Categories.ToListAsync(), "Id", "Name", model.CategoryId);
                return View(model);
            }

            _db.Products.Update(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "محصول ویرایش شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _db.Products.FindAsync(id);
            if (product != null)
            {
                var isUsed = await _db.FruitExitPermits.AnyAsync(p => p.ProductId == id) ||
                             await _db.SortingExitPermits.AnyAsync(p => p.ProductId == id);
                if (isUsed)
                {
                    TempData["Warning"] = "این محصول در برگه‌های خروج استفاده شده و قابل حذف نیست.";
                    return RedirectToAction(nameof(Index));
                }

                _db.Products.Remove(product);
                await _db.SaveChangesAsync();
                TempData["Msg"] = "محصول حذف شد.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}