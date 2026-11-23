using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FruitBilling.Web.Controllers
{
    // مشاهده برای مدیر و مدیر فروش؛ ثبت، ویرایش و حذف فقط مدیر
    [Authorize(Roles = "Manager,SalesManager")]
    public class YieldEstimatesController : Controller
    {
        private readonly AppDbContext _db;

        public YieldEstimatesController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? cycleId)
        {
            var cycles = await _db.HarvestCycles.AsNoTracking()
                .OrderByDescending(c => c.Year).ThenByDescending(c => c.CycleNumber)
                .ToListAsync();
            int cid = cycleId ?? (cycles.FirstOrDefault()?.Id ?? 0);

            var list = await _db.YieldEstimates.AsNoTracking()
                .Include(e => e.Variety).ThenInclude(v => v!.Product)
                .Include(e => e.Plot).ThenInclude(p => p!.Farm)
                .Where(e => e.HarvestCycleId == cid)
                .OrderBy(e => e.Variety!.Product!.Name).ThenBy(e => e.Variety!.Name)
                .ToListAsync();

            ViewBag.CycleId = cid;
            ViewBag.Cycles = new SelectList(cycles, "Id", "Name", cid);
            await LoadLookupsAsync(null, null);
            return View(list);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> Create(int harvestCycleId, int varietyId, int? plotId, decimal estimatedKg, string? note)
        {
            if (estimatedKg <= 0)
            {
                TempData["Warning"] = "مقدار برآورد باید بیشتر از صفر باشد.";
                return RedirectToAction(nameof(Index), new { cycleId = harvestCycleId });
            }

            bool exists = await _db.YieldEstimates.AnyAsync(e =>
                e.HarvestCycleId == harvestCycleId && e.VarietyId == varietyId && e.PlotId == plotId);
            if (exists)
            {
                TempData["Warning"] = "برای این دوره، رقم و قطعه قبلاً برآورد ثبت شده است؛ آن را ویرایش کنید.";
                return RedirectToAction(nameof(Index), new { cycleId = harvestCycleId });
            }

            _db.YieldEstimates.Add(new YieldEstimate
            {
                HarvestCycleId = harvestCycleId,
                VarietyId = varietyId,
                PlotId = plotId,
                EstimatedKg = estimatedKg,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                CreatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();

            TempData["Msg"] = "برآورد ثبت شد.";
            return RedirectToAction(nameof(Index), new { cycleId = harvestCycleId });
        }

        [HttpGet]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> Edit(int id)
        {
            var e = await _db.YieldEstimates.FindAsync(id);
            if (e == null) return NotFound();

            ViewBag.Cycles = new SelectList(await _db.HarvestCycles.AsNoTracking()
                .OrderByDescending(c => c.Year).ThenByDescending(c => c.CycleNumber).ToListAsync(), "Id", "Name", e.HarvestCycleId);
            await LoadLookupsAsync(e.VarietyId, e.PlotId);
            return View(e);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> Edit(int id, int harvestCycleId, int varietyId, int? plotId, decimal estimatedKg, string? note)
        {
            var e = await _db.YieldEstimates.FindAsync(id);
            if (e == null) return NotFound();

            if (estimatedKg <= 0)
            {
                TempData["Warning"] = "مقدار برآورد باید بیشتر از صفر باشد.";
                return RedirectToAction(nameof(Edit), new { id });
            }

            bool duplicate = await _db.YieldEstimates.AnyAsync(x =>
                x.Id != id && x.HarvestCycleId == harvestCycleId && x.VarietyId == varietyId && x.PlotId == plotId);
            if (duplicate)
            {
                TempData["Warning"] = "برای این دوره، رقم و قطعه برآورد دیگری وجود دارد.";
                return RedirectToAction(nameof(Edit), new { id });
            }

            e.HarvestCycleId = harvestCycleId;
            e.VarietyId = varietyId;
            e.PlotId = plotId;
            e.EstimatedKg = estimatedKg;
            e.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            await _db.SaveChangesAsync();

            TempData["Msg"] = "برآورد ویرایش شد.";
            return RedirectToAction(nameof(Index), new { cycleId = e.HarvestCycleId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> Delete(int id)
        {
            var e = await _db.YieldEstimates.FindAsync(id);
            if (e == null) return NotFound();

            int cid = e.HarvestCycleId;
            _db.YieldEstimates.Remove(e);
            await _db.SaveChangesAsync();

            TempData["Msg"] = "برآورد حذف شد.";
            return RedirectToAction(nameof(Index), new { cycleId = cid });
        }

        private async Task LoadLookupsAsync(int? varietyId, int? plotId)
        {
            var varieties = await _db.ProductVarieties.AsNoTracking()
                .Include(v => v.Product)
                .OrderBy(v => v.Product!.Name).ThenBy(v => v.Name)
                .ToListAsync();
            ViewBag.Varieties = varieties
                .Select(v => new SelectListItem((v.Product?.Name ?? "—") + " - " + v.Name, v.Id.ToString(), v.Id == varietyId))
                .ToList();

            var plots = await _db.Plots.AsNoTracking()
                .Include(p => p.Farm)
                .OrderBy(p => p.Farm!.Name).ThenBy(p => p.Name)
                .ToListAsync();
            ViewBag.Plots = plots
                .Select(p => new SelectListItem((p.Farm?.Name ?? "—") + " / " + p.Name, p.Id.ToString(), p.Id == plotId))
                .ToList();
        }
    }
}
