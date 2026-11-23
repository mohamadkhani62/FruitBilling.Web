using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FruitBilling.Web.Controllers
{
    [Authorize(Roles = "Manager")]
    public class ProductVarietiesController : Controller
    {
        private readonly AppDbContext _db;
        public ProductVarietiesController(AppDbContext db) { _db = db; }

        public async Task<IActionResult> Index(int? productId)
        {
            var query = _db.ProductVarieties.Include(v => v.Product).AsQueryable();
            if (productId.HasValue) query = query.Where(v => v.ProductId == productId);

            ViewBag.Products = new SelectList(await _db.Products.ToListAsync(), "Id", "Name", productId);
            var varieties = await query.OrderBy(v => v.Product.Name).ThenBy(v => v.Name).ToListAsync();
            return View(varieties);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Products = new SelectList(await _db.Products.ToListAsync(), "Id", "Name");
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductVariety model)
        {
            if (await _db.ProductVarieties.AnyAsync(v => v.ProductId == model.ProductId && v.Name == model.Name))
                ModelState.AddModelError("Name", "این رقم برای این محصول قبلاً تعریف شده است.");

            if (!ModelState.IsValid)
            {
                ViewBag.Products = new SelectList(await _db.Products.ToListAsync(), "Id", "Name", model.ProductId);
                return View(model);
            }

            _db.ProductVarieties.Add(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "رقم جدید با موفقیت اضافه شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var variety = await _db.ProductVarieties.FindAsync(id);
            if (variety == null) return NotFound();
            ViewBag.Products = new SelectList(await _db.Products.ToListAsync(), "Id", "Name", variety.ProductId);
            return View(variety);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductVariety model)
        {
            if (id != model.Id) return BadRequest();

            if (await _db.ProductVarieties.AnyAsync(v => v.ProductId == model.ProductId && v.Name == model.Name && v.Id != id))
                ModelState.AddModelError("Name", "این رقم برای این محصول قبلاً تعریف شده است.");

            if (!ModelState.IsValid)
            {
                ViewBag.Products = new SelectList(await _db.Products.ToListAsync(), "Id", "Name", model.ProductId);
                return View(model);
            }

            _db.ProductVarieties.Update(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "رقم ویرایش شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var variety = await _db.ProductVarieties.FindAsync(id);
            if (variety != null)
            {
                var isUsed = await _db.FruitExitPermits.AnyAsync(p => p.VarietyId == id) ||
                             await _db.SortingExitPermits.AnyAsync(p => p.VarietyId == id);
                if (isUsed)
                {
                    TempData["Warning"] = "این رقم در برگه‌ها استفاده شده و قابل حذف نیست.";
                    return RedirectToAction(nameof(Index));
                }
                _db.ProductVarieties.Remove(variety);
                await _db.SaveChangesAsync();
                TempData["Msg"] = "رقم حذف شد.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}