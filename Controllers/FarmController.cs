using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FruitBilling.Web.Controllers
{
    // ❗ بدون [Route] و بدون [ApiController]
    public class FarmController : Controller
    {
        private readonly AppDbContext _db;
        public FarmController(AppDbContext db) { _db = db; }

        // ---------- لیست مزارع ----------
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var farms = await _db.Farms.OrderBy(f => f.Id).ToListAsync();
            return View(farms);
        }

        // ---------- افزودن مزرعه ----------
        [HttpGet]
        public IActionResult Create() => View();

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Farm model)
        {
            if (!ModelState.IsValid) return View(model);
            model.Id = 0;
            model.CreatedAt = DateTime.Now;
            _db.Farms.Add(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = $"مزرعه «{model.Name}» ثبت شد.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- ویرایش مزرعه ----------
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var farm = await _db.Farms.FindAsync(id);
            if (farm == null) return NotFound();
            return View(farm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Farm model)
        {
            if (id != model.Id) return BadRequest();
            if (!ModelState.IsValid) return View(model);
            _db.Farms.Update(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "تغییرات ذخیره شد.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- افزودن قطعه ----------
        [HttpGet]
        public IActionResult CreatePlot(int farmId)
        {
            ViewBag.FarmId = farmId;
            return View();
        }

        // ❗ نام این متد را به CreatePlotForm تغییر دادیم تا با API تداخل نکند
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePlotForm(Plot model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.FarmId = model.FarmId;
                return View("CreatePlot", model);
            }
            model.Id = 0;
            model.CreatedAt = DateTime.Now;
            _db.Plots.Add(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = $"قطعه «{model.Name}» ثبت شد.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- ویرایش قطعه ----------
        [HttpGet]
        public async Task<IActionResult> EditPlot(int id)
        {
            var plot = await _db.Plots.FindAsync(id);
            if (plot == null) return NotFound();
            return View(plot);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPlot(int id, Plot model)
        {
            if (id != model.Id) return BadRequest();
            if (!ModelState.IsValid) return View(model);
            _db.Plots.Update(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "قطعه به‌روزرسانی شد.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- افزودن درخت ----------
        [HttpGet]
        public IActionResult CreateTree(int plotId)
        {
            ViewBag.PlotId = plotId;
            return View(new Tree { TreeRowId = plotId, HealthStatus = "سالم" });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTree(Tree model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.PlotId = model.TreeRowId;
                return View(model);
            }
            model.Id = 0;
            model.CreatedAt = DateTime.Now;
            _db.Trees.Add(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "درخت ثبت شد.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- ویرایش درخت ----------
        [HttpGet]
        public async Task<IActionResult> EditTree(int id)
        {
            var tree = await _db.Trees.FindAsync(id);
            if (tree == null) return NotFound();
            return View(tree);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTree(int id, Tree model)
        {
            if (id != model.Id) return BadRequest();
            if (!ModelState.IsValid) return View(model);
            _db.Trees.Update(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "درخت به‌روزرسانی شد.";
            return RedirectToAction(nameof(Index));
        }
    }
}