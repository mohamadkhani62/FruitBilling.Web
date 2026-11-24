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
                .Include(c => c.HarvestCycleCategories).ThenInclude(x => x.Category)
                .OrderByDescending(c => c.Year)
                .ThenBy(c => c.CycleNumber)
                .ToListAsync();
            return View(cycles);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadCategoriesAsync();
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(HarvestCycle model, int[] categoryIds)
        {
            var selectedIds = categoryIds.Distinct().ToArray();
            var categories = await _db.Categories.Where(c => selectedIds.Contains(c.Id))
                .Select(c => c.Id).ToListAsync();
            if (selectedIds.Length == 0 || categories.Count != selectedIds.Length)
                ModelState.AddModelError("categoryIds", "حداقل یک گونه معتبر انتخاب کنید.");

            if (!ModelState.IsValid)
            {
                await LoadCategoriesAsync(selectedIds);
                return View(model);
            }

            model.CategoryId = selectedIds[0]; // سازگاری موقت با کدهای قدیمی
            model.HarvestCycleCategories = selectedIds
                .Select(id => new HarvestCycleCategory { CategoryId = id }).ToList();
            _db.HarvestCycles.Add(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "دوره برداشت جدید اضافه شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var cycle = await _db.HarvestCycles.Include(c => c.HarvestCycleCategories)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (cycle == null) return NotFound();

            var selectedIds = cycle.HarvestCycleCategories.Select(x => x.CategoryId).ToArray();
            if (selectedIds.Length == 0 && cycle.CategoryId.HasValue)
                selectedIds = new[] { cycle.CategoryId.Value };
            await LoadCategoriesAsync(selectedIds);
            return View(cycle);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, HarvestCycle model, int[] categoryIds)
        {
            if (id != model.Id) return BadRequest();

            var selectedIds = categoryIds.Distinct().ToArray();
            var categories = await _db.Categories.Where(c => selectedIds.Contains(c.Id))
                .Select(c => c.Id).ToListAsync();
            if (selectedIds.Length == 0 || categories.Count != selectedIds.Length)
                ModelState.AddModelError("categoryIds", "حداقل یک گونه معتبر انتخاب کنید.");

            if (!ModelState.IsValid)
            {
                await LoadCategoriesAsync(selectedIds);
                return View(model);
            }

            var cycle = await _db.HarvestCycles.Include(c => c.HarvestCycleCategories)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (cycle == null) return NotFound();

            cycle.Name = model.Name;
            cycle.Year = model.Year;
            cycle.CycleNumber = model.CycleNumber;
            cycle.StartDate = model.StartDate;
            cycle.EndDate = model.EndDate;
            cycle.CategoryId = selectedIds[0];

            _db.HarvestCycleCategories.RemoveRange(cycle.HarvestCycleCategories);
            cycle.HarvestCycleCategories = selectedIds.Select(categoryId => new HarvestCycleCategory
            {
                HarvestCycleId = cycle.Id,
                CategoryId = categoryId
            }).ToList();

            await _db.SaveChangesAsync();
            TempData["Msg"] = "دوره برداشت ویرایش شد.";
            return RedirectToAction(nameof(Index));
        }

        private async Task LoadCategoriesAsync(IEnumerable<int>? selectedIds = null)
        {
            var categories = await _db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
            ViewBag.Categories = new MultiSelectList(categories, "Id", "Name", selectedIds);
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