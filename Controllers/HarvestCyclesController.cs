using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FruitBilling.Web.Controllers
{
    [Authorize(Roles = "Manager")]
    public class HarvestCyclesController : Controller
    {
        private readonly AppDbContext _db;
        public HarvestCyclesController(AppDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            var cycles = await _db.HarvestCycles
                .Include(c => c.Category)
                .OrderByDescending(c => c.Year)
                .ThenBy(c => c.CycleNumber)
                .ToListAsync();
            return View(cycles);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // 🆕 ارسال لیست گونه‌ها
            ViewBag.Categories = new SelectList(await _db.Categories.ToListAsync(), "Id", "Name");
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(HarvestCycle model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Categories = new SelectList(await _db.Categories.ToListAsync(), "Id", "Name", model.CategoryId);
                return View(model);
            }
            _db.HarvestCycles.Add(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "دوره برداشت جدید اضافه شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var cycle = await _db.HarvestCycles.FindAsync(id);
            if (cycle == null) return NotFound();

            // 🆕 ارسال لیست گونه‌ها با مقدار انتخاب‌شده
            ViewBag.Categories = new SelectList(await _db.Categories.ToListAsync(), "Id", "Name", cycle.CategoryId);
            return View(cycle);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, HarvestCycle model)
        {
            if (id != model.Id) return BadRequest();

            if (!ModelState.IsValid)
            {
                ViewBag.Categories = new SelectList(await _db.Categories.ToListAsync(), "Id", "Name", model.CategoryId);
                return View(model);
            }

            _db.HarvestCycles.Update(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "دوره برداشت ویرایش شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var cycle = await _db.HarvestCycles.FindAsync(id);
            if (cycle != null)
            {
                var isUsed = await _db.FruitExitPermits.AnyAsync(p => p.HarvestCycleId == id) ||
                             await _db.SortingExitPermits.AnyAsync(p => p.HarvestCycleId == id);
                if (isUsed)
                {
                    TempData["Warning"] = "این دوره در برگه‌ها استفاده شده و قابل حذف نیست.";
                    return RedirectToAction(nameof(Index));
                }
                _db.HarvestCycles.Remove(cycle);
                await _db.SaveChangesAsync();
                TempData["Msg"] = "دوره برداشت حذف شد.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}