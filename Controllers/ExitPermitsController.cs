using ClosedXML.Excel;
using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using FruitBilling.Web.Services;
using static FruitBilling.Web.Services.ExcelImportHelper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FruitBilling.Web.Controllers
{
    public class ExitPermitsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IPermissionService _permissionService;
        private readonly UserManager<IdentityUser> _userManager;

        public ExitPermitsController(AppDbContext db, IPermissionService permissionService, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _permissionService = permissionService;
            _userManager = userManager;
        }

        // ==========================================
        // ۱. لیست برگه‌های خروج (با فیلتر دوره)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(int? cycleId, int? focusId)
        {
            // اگر دوره انتخاب نشده، آخرین دوره ثبت شده را انتخاب کن
            if (!cycleId.HasValue)
            {
                var latestCycle = await _db.HarvestCycles
                    .OrderByDescending(c => c.Year)
                    .ThenByDescending(c => c.CycleNumber)
                    .FirstOrDefaultAsync();

                if (latestCycle != null) cycleId = latestCycle.Id;
            }

            ViewBag.Cycles = new SelectList(await _db.HarvestCycles.OrderByDescending(c => c.Year).ThenByDescending(c => c.CycleNumber).ToListAsync(), "Id", "Name", cycleId);
            ViewBag.SelectedCycleId = cycleId;

            if (!cycleId.HasValue) return View(new List<FruitExitPermit>());

            var cycle = await _db.HarvestCycles.Include(c => c.Category).FirstOrDefaultAsync(c => c.Id == cycleId);
            ViewBag.CurrentCycle = cycle;

            var query = _db.FruitExitPermits
                .Include(p => p.Farm)
                .Include(p => p.Plot)
                .Include(p => p.BasculTickets)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .Include(p => p.Product)
                .Include(p => p.Variety)
                .Where(p => p.HarvestCycleId == cycleId);

            // اگر رکوردی برای فوکوس مشخص نشده، آخرین رکورد را پیدا کن
            if (!focusId.HasValue)
            {
                var lastRecord = await query.OrderByDescending(p => p.Id).FirstOrDefaultAsync();
                if (lastRecord != null) focusId = lastRecord.Id;
            }
            ViewBag.FocusId = focusId;

            var permits = await query.OrderByDescending(p => p.Id).ToListAsync();
            return View(permits);
        }

        // ==========================================
        // ۲. ایجاد (Create)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Create(int? cycleId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!cycleId.HasValue)
            {
                TempData["Warning"] = "ابتدا باید یک دوره برداشت را انتخاب کنید.";
                return RedirectToAction(nameof(Index));
            }

            var perms = await _permissionService.GetEditableFieldsAsync(userId!, "ExitPermits", new FruitExitPermit());
            if (!perms.Values.Any(v => v == true))
            {
                TempData["Warning"] = "شما دسترسی ایجاد برگه خروج را ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId = cycleId });
            }

            await LoadDropdownsForCycle(cycleId.Value);
            var fa = new System.Globalization.PersianCalendar();
            var today = DateTime.Now;

            var model = new FruitExitPermit
            {
                Date = $"{fa.GetYear(today):0000}/{fa.GetMonth(today):00}/{fa.GetDayOfMonth(today):00}",
                Status = "در انتظار توزین",
                HarvestCycleId = cycleId
            };
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FruitExitPermit model, string? farmNameInput, string? plotNameInput)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var perms = await _permissionService.GetEditableFieldsAsync(userId!, "ExitPermits", model);

            // ثبت مزرعه و قطعه
            if (!string.IsNullOrWhiteSpace(farmNameInput))
            {
                var farm = await _db.Farms.FirstOrDefaultAsync(f => f.Name == farmNameInput);
                if (farm == null)
                {
                    farm = new Farm { Name = farmNameInput, CoordinatesJson = "[]", CreatedAt = DateTime.Now };
                    _db.Farms.Add(farm);
                    await _db.SaveChangesAsync();
                }
                model.FarmId = farm.Id;
            }

            if (!string.IsNullOrWhiteSpace(plotNameInput) && model.FarmId.HasValue)
            {
                var plot = await _db.Plots.FirstOrDefaultAsync(p => p.Name == plotNameInput && p.FarmId == model.FarmId.Value);
                if (plot == null)
                {
                    plot = new Plot { Name = plotNameInput, FarmId = model.FarmId.Value, CoordinatesJson = "[]", CreatedAt = DateTime.Now };
                    _db.Plots.Add(plot);
                    await _db.SaveChangesAsync();
                }
                model.PlotId = plot.Id;
            }

            // هر ردیف محصول/رقم در جزئیات جداگانه ذخیره می‌شود.
            model.Items ??= new List<FruitExitPermitItem>();
            model.Items = model.Items
                .Where(x => x.VarietyId > 0 || x.BasketCount > 0)
                .ToList();

            if (model.Items.Count == 0)
                ModelState.AddModelError("Items", "حداقل یک محصول/رقم به برگ خروج اضافه کنید.");

            if (model.Items.Any(x => x.VarietyId <= 0 || x.BasketCount <= 0))
                ModelState.AddModelError("Items", "برای هر ردیف، رقم و تعداد سبد معتبر وارد کنید.");

            var cycleForItems = model.HarvestCycleId.HasValue
                ? await _db.HarvestCycles.Include(c => c.HarvestCycleCategories)
                    .FirstOrDefaultAsync(c => c.Id == model.HarvestCycleId.Value)
                : null;
            var allowedCategoryIds = cycleForItems?.HarvestCycleCategories.Select(x => x.CategoryId).ToArray()
                ?? Array.Empty<int>();
            if (allowedCategoryIds.Length == 0 && cycleForItems?.CategoryId is int legacyCategoryId)
                allowedCategoryIds = new[] { legacyCategoryId };

            var selectedVarietyIds = model.Items.Select(x => x.VarietyId).Distinct().ToArray();
            var selectedVarieties = await _db.ProductVarieties.Include(v => v.Product)
                .Where(v => selectedVarietyIds.Contains(v.Id)).ToListAsync();

            if (selectedVarieties.Count != selectedVarietyIds.Length ||
                selectedVarieties.Any(v => v.Product == null ||
                    (allowedCategoryIds.Length > 0 && !allowedCategoryIds.Contains(v.Product.CategoryId ?? 0))))
                ModelState.AddModelError("Items", "یکی از ارقام انتخاب‌شده متعلق به گونه‌های این دوره برداشت نیست.");

            if (!ModelState.IsValid)
            {
                await LoadDropdownsForCycle(model.HarvestCycleId ?? 0);
                return View(model);
            }

            foreach (var item in model.Items)
            {
                item.HarvestMethod = model.HarvestMethod;
                item.BasketType = model.BasketType;
            }

            var firstVariety = selectedVarieties.First(v => v.Id == model.Items[0].VarietyId);
            model.VarietyId = firstVariety.Id;
            model.ProductId = firstVariety.ProductId;
            model.ProductType = firstVariety.Product?.Name;
            model.BasketCount = model.Items.Sum(x => x.BasketCount);

            model.CreatedAt = DateTime.Now;
            _db.FruitExitPermits.Add(model);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "برگ خروج صادر شد.";

            // بازگشت به همان دوره با فوکوس روی رکورد جدید
            return RedirectToAction(nameof(Index), new { cycleId = model.HarvestCycleId, focusId = model.Id });
        }

        // ==========================================
        // ۳. ویرایش (Edit)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id, int? cycleId)
        {
            var permit = await _db.FruitExitPermits
                .Include(p => p.Farm)
                .Include(p => p.Plot)
                .Include(p => p.BasculTickets)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if ((permit.Status == "تأیید شده در مقصد" || permit.Status == "تأیید فروش") && !User.IsInRole("Manager"))
            {
                TempData["Warning"] = "این برگه تأیید شده و فقط مدیر می‌تواند آن را تغییر دهد.";
            }

            await LoadDropdownsForCycle(permit.HarvestCycleId ?? 0);
            ViewBag.Farms = new SelectList(await _db.Farms.AsNoTracking().OrderBy(f => f.Name).ToListAsync(), "Id", "Name", permit.FarmId);
            ViewBag.Plots = new SelectList(await _db.Plots.AsNoTracking().OrderBy(p => p.Name).ToListAsync(), "Id", "Name", permit.PlotId);

            ViewBag.ReturnCycleId = cycleId ?? permit.HarvestCycleId;
            ViewBag.ReturnFocusId = id;

            return View(permit);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, FruitExitPermit model, int? cycleId)
        {
            if (id != model.Id) return BadRequest();
            var permit = await _db.FruitExitPermits
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if ((permit.Status == "تأیید شده در مقصد" || permit.Status == "تأیید فروش") && !User.IsInRole("Manager"))
            {
                TempData["Warning"] = "شما دسترسی ویرایش این رکورد تأیید شده را ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId, focusId = id });
            }

            var perms = await _permissionService.GetEditableFieldsAsync(userId!, "ExitPermits", permit);
            int? targetFarmId = perms.GetValueOrDefault("FarmId") ? model.FarmId : permit.FarmId;
            int? targetPlotId = perms.GetValueOrDefault("PlotId") ? model.PlotId : permit.PlotId;
            if (targetFarmId.HasValue && !await _db.Farms.AnyAsync(f => f.Id == targetFarmId.Value))
                ModelState.AddModelError(nameof(model.FarmId), "مزرعه انتخاب‌شده معتبر نیست.");
            if (targetPlotId.HasValue && (!targetFarmId.HasValue ||
                !await _db.Plots.AnyAsync(p => p.Id == targetPlotId.Value && p.FarmId == targetFarmId.Value)))
                ModelState.AddModelError(nameof(model.PlotId), "قطعه انتخاب‌شده متعلق به مزرعه انتخاب‌شده نیست.");

            // اعتبارسنجی ردیف‌های رقم و سبد؛ شناسه ردیف فقط در همین برگ پذیرفته می‌شود.
            if (model.Items != null)
            {
                var submitted = model.Items.Where(x => x.Id > 0 || x.VarietyId > 0 || x.BasketCount > 0).ToList();
                if (submitted.Count == 0)
                    ModelState.AddModelError("Items", "حداقل یک ردیف محصول/رقم باید باقی بماند.");
                if (submitted.GroupBy(x => x.Id).Any(g => g.Key > 0 && g.Count() > 1))
                    ModelState.AddModelError("Items", "شناسه ردیف تکراری است.");
                if (submitted.Any(x => x.VarietyId <= 0 || x.BasketCount <= 0))
                    ModelState.AddModelError("Items", "برای هر ردیف، رقم و تعداد سبد معتبر وارد کنید.");

                var cycle = permit.HarvestCycleId.HasValue
                    ? await _db.HarvestCycles.Include(c => c.HarvestCycleCategories)
                        .FirstOrDefaultAsync(c => c.Id == permit.HarvestCycleId.Value) : null;
                var categoryIds = cycle?.HarvestCycleCategories.Select(x => x.CategoryId).ToArray() ?? Array.Empty<int>();
                if (categoryIds.Length == 0 && cycle?.CategoryId is int legacyCategoryId)
                    categoryIds = new[] { legacyCategoryId };
                var varietyIds = submitted.Select(x => x.VarietyId).Where(x => x > 0).Distinct().ToArray();
                var varieties = await _db.ProductVarieties.Include(v => v.Product)
                    .Where(v => varietyIds.Contains(v.Id)).ToListAsync();
                if (varieties.Count != varietyIds.Length || varieties.Any(v => v.Product == null ||
                    (categoryIds.Length > 0 && !categoryIds.Contains(v.Product.CategoryId ?? 0))))
                    ModelState.AddModelError("Items", "یکی از ارقام انتخاب‌شده متعلق به گونه‌های این دوره برداشت نیست.");

                var submittedIds = submitted.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();
                var hasNewItems = submitted.Any(x => x.Id == 0);
                var hasRemovedItems = permit.Items.Any(x => !submittedIds.Contains(x.Id));
                if (hasNewItems && (!perms.GetValueOrDefault("ProductType") || !perms.GetValueOrDefault("BasketCount")))
                    ModelState.AddModelError("Items", "مجوز افزودن ردیف رقم و تعداد سبد را ندارید.");
                if (hasRemovedItems && (!perms.GetValueOrDefault("ProductType") || !perms.GetValueOrDefault("BasketCount")))
                    ModelState.AddModelError("Items", "مجوز حذف ردیف محصول را ندارید.");
                if (submittedIds.Any(x => permit.Items.All(dbItem => dbItem.Id != x)))
                    ModelState.AddModelError("Items", "یک ردیف ارسالی متعلق به این برگ خروج نیست.");
                foreach (var removed in permit.Items.Where(x => !submittedIds.Contains(x.Id)))
                    if (removed.BasculTickets.Any())
                        ModelState.AddModelError("Items", $"ردیف «{removed.Variety?.Name ?? removed.Id.ToString()}» قبض باسکول دارد و حذف آن مجاز نیست.");

                if (!ModelState.IsValid)
                {
                    await LoadDropdownsForCycle(permit.HarvestCycleId ?? 0);
                    ViewBag.Farms = new SelectList(await _db.Farms.AsNoTracking().OrderBy(f => f.Name).ToListAsync(), "Id", "Name", targetFarmId);
                    ViewBag.Plots = new SelectList(await _db.Plots.AsNoTracking().OrderBy(p => p.Name).ToListAsync(), "Id", "Name", targetPlotId);
                    ViewBag.ReturnCycleId = cycleId ?? permit.HarvestCycleId;
                    ViewBag.ReturnFocusId = id;
                    return View(model);
                }

                foreach (var removed in permit.Items.Where(x => !submittedIds.Contains(x.Id)).ToList())
                    _db.FruitExitPermitItems.Remove(removed);

                foreach (var posted in submitted)
                {
                    var item = posted.Id == 0 ? new FruitExitPermitItem() :
                        permit.Items.First(x => x.Id == posted.Id);
                    if (posted.Id == 0)
                    {
                        item.FruitExitPermitId = permit.Id;
                        permit.Items.Add(item);
                    }
                    if (perms.GetValueOrDefault("ProductType")) item.VarietyId = posted.VarietyId;
                    if (perms.GetValueOrDefault("BasketCount")) item.BasketCount = posted.BasketCount;
                    if (perms.GetValueOrDefault("BasketType")) item.BasketType = posted.BasketType;
                    if (perms.GetValueOrDefault("HarvestMethod")) item.HarvestMethod = posted.HarvestMethod;
                }
                if (permit.Items.Count > 0)
                {
                    var first = permit.Items.OrderBy(x => x.Id).First();
                    var firstVariety = varieties.FirstOrDefault(v => v.Id == first.VarietyId)
                        ?? await _db.ProductVarieties.Include(v => v.Product).FirstAsync(v => v.Id == first.VarietyId);
                    permit.VarietyId = first.VarietyId;
                    permit.ProductId = firstVariety.ProductId;
                    permit.ProductType = firstVariety.Product?.Name;
                    permit.BasketCount = permit.Items.Sum(x => x.BasketCount);
                }
            }

            if (perms.GetValueOrDefault("PermitNumber")) permit.PermitNumber = model.PermitNumber;
            if (perms.GetValueOrDefault("Date")) permit.Date = model.Date;
            if (perms.GetValueOrDefault("FarmId")) permit.FarmId = model.FarmId;
            if (perms.GetValueOrDefault("PlotId")) permit.PlotId = model.PlotId;
            if (perms.GetValueOrDefault("Destination")) permit.Destination = model.Destination;
            if (perms.GetValueOrDefault("VehiclePlate")) permit.VehiclePlate = model.VehiclePlate;
            if (perms.GetValueOrDefault("DriverName")) permit.DriverName = model.DriverName;
            if (perms.GetValueOrDefault("ContractorName")) permit.ContractorName = model.ContractorName;
            if (perms.GetValueOrDefault("Description")) permit.Description = model.Description;
            await _db.SaveChangesAsync();
            TempData["Msg"] = "تغییرات مجاز ذخیره شد.";
            return RedirectToAction(nameof(Index), new { cycleId = cycleId ?? permit.HarvestCycleId, focusId = permit.Id });
        }

        // ==========================================
        // ۴. توزین (Weigh)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Weigh(int id, int? cycleId)
        {
            var permit = await _db.FruitExitPermits
                .Include(p => p.Farm).Include(p => p.Plot)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId) || !await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی ثبت توزین را ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId = cycleId ?? permit.HarvestCycleId, focusId = id });
            }

            ViewBag.ReturnCycleId = cycleId ?? permit.HarvestCycleId;
            return View(permit);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Weigh(int id, FruitExitPermit model, int? cycleId)
        {
            var permit = await _db.FruitExitPermits
                .Include(p => p.Farm).Include(p => p.Plot)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId) || !await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی ثبت توزین را ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId = cycleId ?? permit.HarvestCycleId, focusId = id });
            }

            model.Items ??= new List<FruitExitPermitItem>();
            var submittedItems = model.Items.ToList();
            var validItemIds = permit.Items.Select(x => x.Id).ToHashSet();
            if (validItemIds.Count == 0)
            {
                TempData["Warning"] = "این برگ خروج ردیف محصول/رقم ندارد و قابل توزین نیست.";
                return RedirectToAction(nameof(Weigh), new { id, cycleId });
            }

            if (submittedItems.GroupBy(x => x.Id).Any(g => g.Count() > 1) ||
                submittedItems.Any(x => !validItemIds.Contains(x.Id)) ||
                submittedItems.Select(x => x.Id).ToHashSet().SetEquals(validItemIds) == false)
            {
                TempData["Warning"] = "ردیف‌های ارسالی با ردیف‌های همین برگ خروج مطابقت ندارند. هیچ تغییری ذخیره نشد.";
                return RedirectToAction(nameof(Weigh), new { id, cycleId });
            }

            var ticketIdsSeen = new HashSet<int>();
            var prepared = new List<(FruitExitPermitItem Item, List<BasculTicket> Tickets)>();
            foreach (var item in permit.Items)
            {
                var submittedItem = submittedItems.Single(x => x.Id == item.Id);
                var tickets = (submittedItem.BasculTickets ?? new List<BasculTicket>())
                    .Where(t => !string.IsNullOrWhiteSpace(t.TicketNo) || t.GrossWeight > 0 ||
                                t.EmptyWeight > 0 || (t.BasketCount ?? 0) > 0)
                    .ToList();

                var existingTicketIds = item.BasculTickets.Select(t => t.Id).ToHashSet();
                if (tickets.Any(t => t.Id > 0 && (!existingTicketIds.Contains(t.Id) || !ticketIdsSeen.Add(t.Id))))
                {
                    TempData["Warning"] = "یکی از قبض‌ها متعلق به این ردیف نیست یا شناسه آن تکراری است. هیچ تغییری ذخیره نشد.";
                    return RedirectToAction(nameof(Weigh), new { id, cycleId });
                }

                if (tickets.Count == 1) tickets[0].BasketCount = item.BasketCount;
                if (tickets.Count == 0 ||
                    tickets.Any(t => string.IsNullOrWhiteSpace(t.TicketNo) || t.GrossWeight <= t.EmptyWeight ||
                                     t.EmptyWeight < 0 || (t.BasketCount ?? 0) <= 0) ||
                    tickets.Sum(t => t.BasketCount ?? 0) != item.BasketCount)
                {
                    TempData["Warning"] = $"برای رقم {item.Variety?.Name} قبض معتبر ثبت کنید؛ مجموع سبد قبض‌ها باید دقیقاً {item.BasketCount} باشد و وزن پر از وزن خالی بیشتر باشد. هیچ تغییری ذخیره نشد.";
                    return RedirectToAction(nameof(Weigh), new { id, cycleId });
                }
                prepared.Add((item, tickets));
            }

            var totalNetWeight = 0;
            foreach (var entry in prepared)
            {
                var item = entry.Item;
                var tickets = entry.Tickets;
                var retainedIds = tickets.Where(t => t.Id > 0).Select(t => t.Id).ToHashSet();

                _db.BasculTickets.RemoveRange(item.BasculTickets.Where(t => !retainedIds.Contains(t.Id)));

                foreach (var posted in tickets)
                {
                    BasculTicket ticket;
                    if (posted.Id > 0)
                    {
                        ticket = item.BasculTickets.Single(t => t.Id == posted.Id);
                    }
                    else
                    {
                        ticket = new BasculTicket
                        {
                            FruitExitPermitId = permit.Id,
                            FruitExitPermitItemId = item.Id,
                            CreatedAt = DateTime.Now
                        };
                        _db.BasculTickets.Add(ticket);
                    }

                    ticket.FruitExitPermitId = permit.Id;
                    ticket.FruitExitPermitItemId = item.Id;
                    ticket.TicketNo = posted.TicketNo!.Trim();
                    ticket.BasketCount = posted.BasketCount;
                    ticket.EmptyWeight = posted.EmptyWeight;
                    ticket.GrossWeight = posted.GrossWeight;
                    ticket.NetWeight = posted.GrossWeight - posted.EmptyWeight;
                    totalNetWeight += ticket.NetWeight;
                }
            }

            permit.Status = "توزین شده";
            await _db.SaveChangesAsync();
            TempData["Msg"] = $"توزین ثبت شد. مجموع وزن خالص: {totalNetWeight:N0} کیلوگرم.";
            return RedirectToAction(nameof(Index), new { cycleId = cycleId ?? permit.HarvestCycleId, focusId = permit.Id });
        }

                // ==========================================
        // ۵. حذف (Delete)
        // ==========================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int? cycleId)
        {
            var permit = await _db.FruitExitPermits.FindAsync(id);
            if (permit != null)
            {
                int? targetFocusId = null;
                if (permit.HarvestCycleId.HasValue)
                {
                    var prevRecord = await _db.FruitExitPermits
                        .Where(p => p.HarvestCycleId == permit.HarvestCycleId && p.Id < id)
                        .OrderByDescending(p => p.Id).FirstOrDefaultAsync();

                    if (prevRecord != null) targetFocusId = prevRecord.Id;
                    else
                    {
                        var nextRecord = await _db.FruitExitPermits
                            .Where(p => p.HarvestCycleId == permit.HarvestCycleId && p.Id > id)
                            .OrderBy(p => p.Id).FirstOrDefaultAsync();
                        if (nextRecord != null) targetFocusId = nextRecord.Id;
                    }
                }

                var currentCycleId = permit.HarvestCycleId;
                _db.FruitExitPermits.Remove(permit);
                await _db.SaveChangesAsync();
                TempData["Msg"] = "برگه خروج حذف شد.";

                return RedirectToAction(nameof(Index), new { cycleId = currentCycleId, focusId = targetFocusId });
            }
            return RedirectToAction(nameof(Index), new { cycleId = cycleId });
        }

        // ==========================================
        // ۶. تأیید مقصد (ConfirmArrival)
        // ==========================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmArrival(int id, int? cycleId)
        {
            var permit = await _db.FruitExitPermits
                .Include(p => p.Farm).Include(p => p.Plot).Include(p => p.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!await _permissionService.CanApproveAsync(userId, "ExitPermits"))
            {
                TempData["Warning"] = "شما دسترسی تأیید این رکورد را ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId = cycleId ?? permit.HarvestCycleId, focusId = id });
            }

            if (permit.Status == "توزین شده")
            {
                permit.Status = "تأیید شده در مقصد";
                await _db.SaveChangesAsync();
                TempData["Msg"] = "تأیید مقصد با موفقیت ثبت شد.";

                // انتقال به سورتینگ (اگر مقصد بازار بود)
                if (!string.IsNullOrEmpty(permit.Destination) && (permit.Destination.Contains("بازار") || permit.Destination.Contains("فروش")))
                {
                    string? farmName = permit.Farm?.Name;
                    if (!string.IsNullOrEmpty(farmName))
                    {
                        var alreadyExists = await _db.SortingExitPermits
                            .AnyAsync(s => s.PermitNumber == permit.PermitNumber && s.FarmName == farmName);

                        if (!alreadyExists)
                        {
                            var newSortingPermit = new SortingExitPermit
                            {
                                PermitNumber = permit.PermitNumber,
                                Date = permit.Date,
                                BasketCount = permit.BasketCount,
                                ProductType = permit.ProductType,
                                Grade = "بدون سورت",
                                FarmName = farmName,
                                VehiclePlate = permit.VehiclePlate,
                                Destination = permit.Destination,
                                SaleType = "نقدی",
                                Source = farmName,
                                SortingContractor = permit.ContractorName,
                                Description = $"ثبت خودکار از تأیید مقصد (برگه خروج {permit.PermitNumber})",
                                Status = "توزین شده",
                                CreatedAt = DateTime.Now
                            };

                            if (permit.BasculTickets != null)
                            {
                                foreach (var t in permit.BasculTickets)
                                {
                                    newSortingPermit.BasculTickets.Add(new SortingBasculTicket
                                    {
                                        TicketNo = t.TicketNo,
                                        EmptyWeight = t.EmptyWeight,
                                        GrossWeight = t.GrossWeight,
                                        NetWeight = t.NetWeight,
                                        CreatedAt = t.CreatedAt
                                    });
                                }
                            }
                            _db.SortingExitPermits.Add(newSortingPermit);
                            await _db.SaveChangesAsync();
                            TempData["Msg"] += " (در لیست سورتینگ ثبت شد)";
                        }
                    }
                }
            }
            return RedirectToAction(nameof(Index), new { cycleId = cycleId ?? permit.HarvestCycleId, focusId = permit.Id });
        }

        // ==========================================
        // ۷. مدیریت قبض‌های باسکول
        // ==========================================
        private async Task<bool> CanManageBasculTicketsAsync(string userId, FruitExitPermit permit)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            if ((permit.Status == "تأیید شده در مقصد" || permit.Status == "تأیید فروش") && !User.IsInRole("Manager")) return false;

            var perms = await _permissionService.GetEditableFieldsAsync(userId, "ExitPermits", permit);
            return perms.GetValueOrDefault("TicketNo_Mزرعه") || perms.GetValueOrDefault("EmptyWeight_Mزرعه") ||
                   perms.GetValueOrDefault("GrossWeight_Mزرعه") || perms.GetValueOrDefault("NetWeight_Mزرعه");
        }

        [HttpGet]
        public async Task<IActionResult> EditTicket(int id, int permitId, int? cycleId)
        {
            var ticket = await _db.BasculTickets.FindAsync(id);
            var permit = await _db.FruitExitPermits.FindAsync(permitId);
            if (ticket == null || permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (!await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی ویرایش قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, cycleId = cycleId });
            }

            ViewBag.PermitId = permitId;
            ViewBag.ReturnCycleId = cycleId ?? permit.HarvestCycleId;
            return View(ticket);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTicket(int id, BasculTicket model, int permitId, int? cycleId)
        {
            if (id != model.Id) return BadRequest();
            var ticket = await _db.BasculTickets.FindAsync(id);
            var permit = await _db.FruitExitPermits.FindAsync(permitId);
            if (ticket == null || permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (!await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی ویرایش قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, cycleId = cycleId });
            }

            if (ModelState.IsValid)
            {
                ticket.TicketNo = model.TicketNo;
                ticket.EmptyWeight = model.EmptyWeight;
                ticket.GrossWeight = model.GrossWeight;
                ticket.NetWeight = model.GrossWeight - model.EmptyWeight;
                await _db.SaveChangesAsync();
                TempData["Msg"] = "قبض باسکول ویرایش شد.";
                return RedirectToAction(nameof(Edit), new { id = permitId, cycleId = cycleId });
            }
            ViewBag.PermitId = permitId;
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTicket(int id, int permitId, int? cycleId)
        {
            var permit = await _db.FruitExitPermits.Include(p => p.BasculTickets).FirstOrDefaultAsync(p => p.Id == permitId);
            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (!await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی حذف قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, cycleId = cycleId });
            }

            if (permit.BasculTickets.Count <= 1)
            {
                TempData["Warning"] = "امکان حذف آخرین قبض وجود ندارد.";
                return RedirectToAction(nameof(Edit), new { id = permitId, cycleId = cycleId });
            }

            var ticket = permit.BasculTickets.FirstOrDefault(t => t.Id == id);
            if (ticket != null)
            {
                _db.BasculTickets.Remove(ticket);
                await _db.SaveChangesAsync();
                TempData["Msg"] = "قبض حذف شد.";
            }
            return RedirectToAction(nameof(Edit), new { id = permitId, cycleId = cycleId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTicket(int permitId, string ticketNo, int emptyWeight, int grossWeight, int? cycleId)
        {
            var permit = await _db.FruitExitPermits.FindAsync(permitId);
            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (!await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی افزودن قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, cycleId = cycleId });
            }

            if (grossWeight > emptyWeight)
            {
                var newTicket = new BasculTicket
                {
                    FruitExitPermitId = permitId,
                    TicketNo = ticketNo,
                    EmptyWeight = emptyWeight,
                    GrossWeight = grossWeight,
                    NetWeight = grossWeight - emptyWeight,
                    CreatedAt = DateTime.Now
                };
                _db.BasculTickets.Add(newTicket);
                await _db.SaveChangesAsync();
                TempData["Msg"] = "قبض جدید اضافه شد.";
            }
            else
            {
                TempData["Warning"] = "وزن پر باید بیشتر از وزن خالی باشد.";
            }
            return RedirectToAction(nameof(Edit), new { id = permitId, cycleId = cycleId });
        }

        // ==========================================
        // ۸. چاپ (Print)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Print(int id)
        {
            var permit = await _db.FruitExitPermits
                .Include(p => p.Farm).Include(p => p.Plot).Include(p => p.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (permit == null) return NotFound();
            return View(permit);
        }

        // ==========================================
        // 📥 خروجی اکسل برگ‌های خروج از مزرعه
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Export(int? cycleId)
        {
            // با cycleId فقط همان دوره (یک تب)؛ بدون cycleId همه‌ی دوره‌ها (یک تب برای هر دوره)
            var exportQuery = _db.FruitExitPermits
                .Include(p => p.Farm)
                .Include(p => p.Plot)
                .Include(p => p.Product)
                .Include(p => p.Variety)
                .Include(p => p.BasculTickets)
                .AsQueryable();
            if (cycleId.HasValue) exportQuery = exportQuery.Where(p => p.HarvestCycleId == cycleId.Value);
            var list = await exportQuery.OrderBy(p => p.Date).ThenBy(p => p.Id).ToListAsync();

            var cycles = await _db.HarvestCycles.AsNoTracking()
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.CycleNumber)
                .ToListAsync();

            using var wb = new XLWorkbook();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // نام تب = نام دوره برداشت (مثل فایل ورودی)؛ نام دوره در ردیف‌ها تکرار نمی‌شود
            void WriteSheet(string title, List<FruitExitPermit> items)
            {
                var ws = wb.AddWorksheet(SafeSheetName(title, usedNames));

                string[] headers = { "ردیف", "شماره برگ خروج", "تاریخ", "نام مزرعه", "نام قطعه", "نوع محصول", "نام رقم", "روش برداشت", "تعداد سبد", "نوع سبد", "مقصد", "شماره پلاک", "نام راننده", "پیمانکار برداشت", "توضیحات", "شماره قبض باسکول", "وزن خالص" };
                for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

                for (int i = 0; i < items.Count; i++)
                {
                    var r = items[i];
                    int row = i + 2;
                    var totalNet = r.BasculTickets?.Sum(t => t.NetWeight) ?? 0;
                    var ticketNos = r.BasculTickets != null && r.BasculTickets.Any()
                        ? string.Join(" | ", r.BasculTickets.Select(t => t.TicketNo)) : null;

                    ws.Cell(row, 1).Value = i + 1;
                    ws.Cell(row, 2).Value = r.PermitNumber;
                    ws.Cell(row, 3).Value = r.Date;
                    ws.Cell(row, 4).Value = r.Farm?.Name;
                    ws.Cell(row, 5).Value = r.Plot?.Name;
                    ws.Cell(row, 6).Value = r.Product?.Name ?? r.ProductType;
                    ws.Cell(row, 7).Value = r.Variety?.Name;
                    ws.Cell(row, 8).Value = r.HarvestMethod;
                    ws.Cell(row, 9).Value = r.BasketCount;
                    ws.Cell(row, 10).Value = r.BasketType;
                    ws.Cell(row, 11).Value = r.Destination;
                    ws.Cell(row, 12).Value = r.VehiclePlate;
                    ws.Cell(row, 13).Value = r.DriverName;
                    ws.Cell(row, 14).Value = r.ContractorName;
                    ws.Cell(row, 15).Value = r.Description;
                    ws.Cell(row, 16).Value = ticketNos;
                    if (totalNet > 0) ws.Cell(row, 17).Value = totalNet;
                }

                ws.RightToLeft = true;
                ws.SheetView.FreezeRows(1);
            }

            foreach (var cy in cycles)
            {
                // در خروجی تک‌دوره، تب همان دوره همیشه ساخته می‌شود (حتی اگر خالی باشد)
                var items = list.Where(p => p.HarvestCycleId == cy.Id).ToList();
                if (items.Count == 0 && cycleId != cy.Id) continue;
                WriteSheet(cy.Name, items);
            }

            // برگه‌های بدون دوره (اگر وجود داشته باشد)
            var noCycle = list.Where(p => p.HarvestCycleId == null).ToList();
            if (noCycle.Count > 0)
                WriteSheet("بدون دوره", noCycle);

            if (wb.Worksheets.Count == 0)
                WriteSheet("ExitPermits", new List<FruitExitPermit>());

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ExitPermits-{DateTime.Now:yyyyMMddHHmm}.xlsx");
        }

        // ==========================================
        // 📤 ورود یکپارچه اکسل برگ‌های خروج از مزرعه (GET)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Import()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!await _permissionService.CanUserImportAsync(userId!, "ExitPermits"))
            {
                TempData["Warning"] = "شما مجوز ورود اکسل برگ خروج از مزرعه را ندارید.";
                return RedirectToAction(nameof(Index));
            }

            await PrepareImportViewAsync();
            return View();
        }

        // ==========================================
        // 📄 دانلود قالب اکسل: یک تب به‌ازای هر دوره (نام تب = نام دوره)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> DownloadTemplate()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!await _permissionService.CanUserImportAsync(userId!, "ExitPermits"))
            {
                TempData["Warning"] = "شما مجوز ورود اکسل برگ خروج از مزرعه را ندارید.";
                return RedirectToAction(nameof(Index));
            }

            var cycles = await _db.HarvestCycles.AsNoTracking()
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.CycleNumber)
                .ToListAsync();

            var bytes = BuildTemplate(cycles.Select(x => x.Name), FarmImportHeaders, new[] { 8, 16, 17 });
            if (bytes == null)
            {
                TempData["Warning"] = "هیچ دوره‌ای با نام معتبر برای تب اکسل وجود ندارد. ابتدا دوره‌ها را تعریف کنید.";
                return RedirectToAction(nameof(Import));
            }

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "ExitPermits-Template.xlsx");
        }

        // ==========================================
        // 📤 پردازش فایل اکسل یکپارچه (POST)
        //  - فقط .xlsx با ساختار مشخص (۱۷ ستون A تا Q با سربرگ دقیق)
        //  - نام هر تب باید با نام یک دوره برداشت یکی باشد
        //  - هر تبِ معتبر در دوره‌ی خودش ثبت می‌شود؛ تبِ نامعتبر کلاً ثبت نمی‌شود
        // ==========================================
        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(100_000_000)]
        public async Task<IActionResult> Import(IFormFile? file)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!await _permissionService.CanUserImportAsync(userId!, "ExitPermits"))
            {
                TempData["Warning"] = "شما مجوز ورود اکسل برگ خروج از مزرعه را ندارید.";
                return RedirectToAction(nameof(Index));
            }

            await PrepareImportViewAsync();

            var (wb, openError) = await OpenWorkbookAsync(file);
            if (wb == null)
            {
                ModelState.AddModelError("", openError ?? "فایل نامعتبر است.");
                return View();
            }

            var reports = new List<SheetImportReport>();

            try
            {
                using (wb)
                {
                    var cycles = await _db.HarvestCycles.AsNoTracking().ToListAsync();
                    var cycleLookup = cycles
                        .GroupBy(x => Norm(x.Name))
                        .ToDictionary(g => g.Key, g => g.ToList());
                    var matchedCycleIds = new HashSet<int>();

                    // ---------- کاتالوگ‌ها ----------
                    var farms = await _db.Farms.ToListAsync();
                    var farmByKey = new Dictionary<string, Farm>();
                    foreach (var f in farms) farmByKey.TryAdd(Norm(f.Name), f);

                    var farmKeyById = new Dictionary<int, string>();
                    foreach (var f in farms) farmKeyById[f.Id] = Norm(f.Name);

                    var plotByKey = new Dictionary<string, Plot>();
                    foreach (var pl in await _db.Plots.ToListAsync())
                        if (farmKeyById.TryGetValue(pl.FarmId, out var fk))
                            plotByKey.TryAdd(fk + "|" + Norm(pl.Name), pl);

                    var products = await _db.Products.AsNoTracking().ToListAsync();
                    var varieties = await _db.ProductVarieties.AsNoTracking().ToListAsync();

                    // کلید برگه‌های موجود (مزرعه + شماره برگه)؛ ایندکس یکتا سراسری است نه فقط در یک دوره
                    var usedKeys = (await _db.FruitExitPermits.AsNoTracking()
                            .Where(p => p.FarmId != null)
                            .Select(p => new { FarmName = p.Farm!.Name, p.PermitNumber })
                            .ToListAsync())
                        .Select(k => Norm(k.FarmName) + "|" + Norm(k.PermitNumber))
                        .ToHashSet();

                    var toAdd = new List<FruitExitPermit>();

                    foreach (var ws in wb.Worksheets)
                    {
                        var rep = new SheetImportReport { SheetName = ws.Name };
                        reports.Add(rep);

                        // ---------- تطبیق نام تب با نام دوره ----------
                        if (!cycleLookup.TryGetValue(Norm(ws.Name), out var matches))
                        {
                            rep.AddError("نام این تب با نام هیچ دوره‌ی برداشتی یکی نیست؛ این تب ثبت نشد.");
                            continue;
                        }
                        if (matches.Count > 1)
                        {
                            rep.AddError("چند دوره با این نام وجود دارد؛ نام دوره‌ها باید یکتا باشد.");
                            continue;
                        }
                        var cycle = matches[0];
                        rep.CycleId = cycle.Id;
                        rep.CycleName = cycle.Name;
                        if (!matchedCycleIds.Add(cycle.Id))
                        {
                            rep.AddError("برای همین دوره در این فایل تب دیگری هم وجود دارد؛ برای هر دوره فقط یک تب مجاز است.");
                            continue;
                        }

                        // ---------- ساختار و نوع مقادیر ----------
                        var rows = ParseFarmSheet(ws, rep);
                        if (rep.HasErrors) continue;

                        var merged = MergeFarmGroups(rows, rep);
                        if (rep.HasErrors) continue;

                        // ---------- تطبیق محصول و رقم با کاتالوگ ----------
                        var plan = new List<(FarmImportRow Head, List<FarmImportRow> TicketRows, Product? Product, ProductVariety? Variety)>();
                        foreach (var (first, ticketRows) in merged)
                        {
                            Product? product = null;
                            ProductVariety? variety = null;
                            string label = $"برگه «{first.PermitNumber}» (مزرعه «{first.FarmName}»)";

                            if (first.ProductName.Length > 0)
                            {
                                var productKey = Norm(first.ProductName);
                                var cands = products
                                    .Where(p => Norm(p.Name) == productKey
                                             && (!cycle.CategoryId.HasValue || p.CategoryId == cycle.CategoryId))
                                    .ToList();

                                if (cands.Count == 0)
                                    rep.AddError($"{label}: محصول «{first.ProductName}» " +
                                        (cycle.CategoryId.HasValue ? "در گونه‌ی این دوره تعریف نشده است." : "تعریف نشده است."));
                                else if (cands.Count > 1)
                                    rep.AddError($"{label}: چند محصول با نام «{first.ProductName}» وجود دارد.");
                                else
                                {
                                    var prod = cands[0];
                                    product = prod;

                                    if (first.VarietyName.Length > 0)
                                    {
                                        var varietyKey = Norm(first.VarietyName);
                                        var vs = varieties
                                            .Where(v => v.ProductId == prod.Id && Norm(v.Name) == varietyKey)
                                            .ToList();
                                        if (vs.Count == 0)
                                            rep.AddError($"{label}: رقم «{first.VarietyName}» برای محصول «{prod.Name}» تعریف نشده است.");
                                        else
                                            variety = vs[0];
                                    }
                                }
                            }

                            plan.Add((first, ticketRows, product, variety));
                        }
                        if (rep.HasErrors) continue;

                        // ---------- ساخت رکوردها (مزرعه و قطعه‌ی ناموجود مثل صدور دستی ساخته می‌شوند) ----------
                        foreach (var (first, ticketRows, product, variety) in plan)
                        {
                            string farmKey = Norm(first.FarmName);

                            // جلوگیری از برگه‌ی تکراری (مزرعه + شماره برگه) در دیتابیس یا در همین فایل
                            if (!usedKeys.Add(farmKey + "|" + Norm(first.PermitNumber)))
                            {
                                rep.Duplicates++;
                                continue;
                            }

                            if (!farmByKey.TryGetValue(farmKey, out var farm))
                            {
                                farm = new Farm { Name = first.FarmName, CoordinatesJson = "[]", CreatedAt = DateTime.Now };
                                farmByKey[farmKey] = farm;
                                _db.Farms.Add(farm);
                                rep.AddNote($"مزرعه‌ی جدید «{farm.Name}» ساخته شد.");
                            }

                            Plot? plot = null;
                            if (first.PlotName.Length > 0)
                            {
                                var plotKey = farmKey + "|" + Norm(first.PlotName);
                                if (!plotByKey.TryGetValue(plotKey, out plot))
                                {
                                    plot = new Plot { Name = first.PlotName, Farm = farm, CoordinatesJson = "[]", CreatedAt = DateTime.Now };
                                    plotByKey[plotKey] = plot;
                                    _db.Plots.Add(plot);
                                    rep.AddNote($"قطعه‌ی جدید «{plot.Name}» برای مزرعه‌ی «{farm.Name}» ساخته شد.");
                                }
                            }

                            var permit = new FruitExitPermit
                            {
                                PermitNumber = first.PermitNumber,
                                Date = NullIfEmpty(first.Date),
                                Farm = farm,
                                Plot = plot,
                                ProductId = product?.Id,
                                VarietyId = variety?.Id,
                                ProductType = product == null ? null
                                    : (product.Name.Length > 50 ? product.Name.Substring(0, 50) : product.Name),
                                HarvestCycleId = cycle.Id,
                                HarvestMethod = NullIfEmpty(first.HarvestMethod),
                                BasketCount = first.BasketCount,
                                BasketType = NullIfEmpty(first.BasketType),
                                Destination = NullIfEmpty(first.Destination),
                                VehiclePlate = NullIfEmpty(first.VehiclePlate),
                                DriverName = NullIfEmpty(first.DriverName),
                                ContractorName = NullIfEmpty(first.ContractorName),
                                Description = NullIfEmpty(first.Description),
                                Status = "در انتظار توزین",
                                CreatedAt = DateTime.Now
                            };

                            foreach (var t in ticketRows)
                            {
                                permit.BasculTickets.Add(new BasculTicket
                                {
                                    TicketNo = t.TicketNo,
                                    EmptyWeight = t.EmptyWeight,
                                    GrossWeight = t.GrossWeight,
                                    NetWeight = t.GrossWeight - t.EmptyWeight,
                                    CreatedAt = DateTime.Now
                                });
                                rep.Tickets++;
                            }

                            // اگر قبض داشت، وضعیت را توزین‌شده کن
                            if (permit.BasculTickets.Any())
                                permit.Status = "توزین شده";

                            toAdd.Add(permit);
                            rep.Permits++;
                        }
                    }

                    // یک SaveChanges = یا همه‌ی تب‌های معتبر ثبت می‌شوند یا هیچ‌کدام
                    if (toAdd.Count > 0)
                    {
                        _db.FruitExitPermits.AddRange(toAdd);
                        await _db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (var rep in reports.Where(r => !r.HasErrors && r.Permits > 0))
                {
                    rep.AddError("خطا در ذخیره‌سازی: " + (ex.InnerException?.Message ?? ex.Message));
                    rep.Permits = 0;
                    rep.Tickets = 0;
                    rep.Notes.Clear();
                }
                ModelState.AddModelError("", "خطا در پردازش فایل: " + (ex.InnerException?.Message ?? ex.Message));
            }

            ViewBag.Report = reports;

            if (!reports.Any(r => !r.HasErrors && (r.Permits > 0 || r.Duplicates > 0)))
                ModelState.AddModelError("", "هیچ تبی ثبت نشد. جزئیات هر تب را در گزارش زیر ببینید.");

            return View();
        }

        // ==========================================
        // 🆕 متدهای کمکی ورود اکسل برگ خروج از مزرعه
        // ==========================================

        // سربرگ‌های الزامی (ستون A تا Q) به همین ترتیب
        private static readonly string[] FarmImportHeaders =
        {
            "شماره برگ خروج", "تاریخ", "نام مزرعه", "نام قطعه", "نوع محصول", "نام رقم",
            "روش برداشت", "تعداد سبد", "نوع سبد", "مقصد", "شماره پلاک", "نام راننده",
            "پیمانکار برداشت", "توضیحات", "شماره قبض باسکول", "وزن خالی (kg)", "وزن پر (kg)"
        };

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private async Task PrepareImportViewAsync()
        {
            var cycles = await _db.HarvestCycles.AsNoTracking()
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.CycleNumber)
                .ToListAsync();

            ViewBag.ImportCycleNames = cycles.Where(x => IsValidSheetName(x.Name)).Select(x => x.Name).ToList();
            ViewBag.UnusableCycleNames = cycles.Where(x => !IsValidSheetName(x.Name)).Select(x => x.Name).ToList();
        }

        // خواندن یک تب: بررسی سربرگ، تعداد ستون‌ها، نوع و طول مقادیر، و قبض‌ها
        private static List<FarmImportRow> ParseFarmSheet(IXLWorksheet ws, SheetImportReport rep)
        {
            var rows = new List<FarmImportRow>();
            int colCount = FarmImportHeaders.Length;
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            if (lastRow == 0)
            {
                rep.AddError("تب خالی است.");
                return rows;
            }

            // ---------- سربرگ ----------
            if (lastCol > colCount)
                rep.AddError($"ساختار فایل باید دقیقاً {colCount} ستون (A تا Q) باشد؛ ستون اضافه پیدا شد.");

            for (int i = 0; i < colCount; i++)
            {
                var actual = ws.Cell(1, i + 1).GetString();
                if (Norm(actual) != Norm(FarmImportHeaders[i]))
                    rep.AddError($"عنوان ستون {XLHelper.GetColumnLetterFromNumber(i + 1)} باید «{FarmImportHeaders[i]}» باشد (در فایل: «{actual}»).");
            }
            if (rep.HasErrors) return rows;

            if (lastRow < 2)
            {
                rep.AddError("تب هیچ ردیف داده‌ای ندارد.");
                return rows;
            }
            if (lastRow > 20001)
            {
                rep.AddError("حداکثر ۲۰٬۰۰۰ ردیف در هر تب مجاز است.");
                return rows;
            }

            // ---------- ردیف‌ها ----------
            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);

                bool allEmpty = true;
                for (int col = 1; col <= colCount; col++)
                {
                    var cell = row.Cell(col);
                    if (cell.DataType != XLDataType.Blank && !(cell.DataType == XLDataType.Text && string.IsNullOrWhiteSpace(cell.GetString())))
                    {
                        allEmpty = false;
                        break;
                    }
                }
                if (allEmpty) continue;

                int errorsBefore = rep.Errors.Count + rep.HiddenErrorCount;

                // کلیدهای گروه‌بندی (شماره برگه و مزرعه) در همه‌ی ردیف‌ها الزامی‌اند
                TryReadText(row.Cell(1), FarmImportHeaders[0], 15, r, rep, true, out var permitNumber);
                TryReadDate(row.Cell(2), r, rep, out var date);
                TryReadText(row.Cell(3), FarmImportHeaders[2], 100, r, rep, true, out var farmName);
                TryReadText(row.Cell(4), FarmImportHeaders[3], 100, r, rep, false, out var plotName);
                TryReadText(row.Cell(5), FarmImportHeaders[4], 100, r, rep, false, out var productName);
                TryReadText(row.Cell(6), FarmImportHeaders[5], 100, r, rep, false, out var varietyName);
                TryReadText(row.Cell(7), FarmImportHeaders[6], 50, r, rep, false, out var harvestMethod);
                TryReadInt(row.Cell(8), FarmImportHeaders[7], r, rep, out var basket);
                TryReadText(row.Cell(9), FarmImportHeaders[8], 50, r, rep, false, out var basketType);
                TryReadText(row.Cell(10), FarmImportHeaders[9], 100, r, rep, false, out var destination);
                TryReadText(row.Cell(11), FarmImportHeaders[10], 20, r, rep, false, out var plate);
                TryReadText(row.Cell(12), FarmImportHeaders[11], 100, r, rep, false, out var driver);
                TryReadText(row.Cell(13), FarmImportHeaders[12], 100, r, rep, false, out var contractor);
                TryReadText(row.Cell(14), FarmImportHeaders[13], 500, r, rep, false, out var description);
                TryReadText(row.Cell(15), FarmImportHeaders[14], 30, r, rep, false, out var ticketNo);
                TryReadInt(row.Cell(16), FarmImportHeaders[15], r, rep, out var emptyW);
                TryReadInt(row.Cell(17), FarmImportHeaders[16], r, rep, out var grossW);

                // اگر هر کدام از ستون‌های قبض پر باشد، هر سه الزامی‌اند
                bool anyTicket = ticketNo.Length > 0 || emptyW.HasValue || grossW.HasValue;
                if (anyTicket && rep.Errors.Count + rep.HiddenErrorCount == errorsBefore)
                {
                    if (ticketNo.Length == 0 || !emptyW.HasValue || !grossW.HasValue)
                        rep.AddError($"ردیف {r}: برای ثبت قبض باید «شماره قبض باسکول»، «وزن خالی» و «وزن پر» هر سه پر باشند.");
                    else if (grossW.Value <= emptyW.Value)
                        rep.AddError($"ردیف {r}: وزن پر ({grossW.Value}) باید بزرگ‌تر از وزن خالی ({emptyW.Value}) باشد.");
                }

                if (rep.Errors.Count + rep.HiddenErrorCount != errorsBefore) continue;

                rows.Add(new FarmImportRow
                {
                    RowNumber = r,
                    PermitNumber = permitNumber,
                    Date = date,
                    FarmName = farmName,
                    PlotName = plotName,
                    ProductName = productName,
                    VarietyName = varietyName,
                    HarvestMethod = harvestMethod,
                    BasketCount = basket,
                    BasketType = basketType,
                    Destination = destination,
                    VehiclePlate = plate,
                    DriverName = driver,
                    ContractorName = contractor,
                    Description = description,
                    TicketNo = ticketNo,
                    EmptyWeight = emptyW ?? 0,
                    GrossWeight = grossW ?? 0
                });
            }

            if (!rep.HasErrors && rows.Count == 0)
                rep.AddError("تب هیچ ردیف داده‌ای ندارد.");

            return rows;
        }

        // ردیف‌های هم‌کلید (مزرعه + شماره برگه) = یک برگه با چند قبض.
        // مقادیر خالی از ردیف‌های دیگر پر می‌شود؛ مقادیر ناهمسان خطا است.
        private static List<(FarmImportRow Head, List<FarmImportRow> TicketRows)> MergeFarmGroups(List<FarmImportRow> rows, SheetImportReport rep)
        {
            var result = new List<(FarmImportRow, List<FarmImportRow>)>();

            foreach (var g in rows.GroupBy(x => Norm(x.PermitNumber) + "|" + Norm(x.FarmName)))
            {
                var list = g.ToList();
                var head = new FarmImportRow
                {
                    RowNumber = list[0].RowNumber,
                    PermitNumber = list[0].PermitNumber,
                    FarmName = list[0].FarmName
                };
                string label = $"برگه «{head.PermitNumber}» (مزرعه «{head.FarmName}»)";

                void MergeText(string title, Func<FarmImportRow, string> get, Action<FarmImportRow, string> set, bool required)
                {
                    var distinct = list.Where(x => !string.IsNullOrWhiteSpace(get(x)))
                                       .GroupBy(x => Norm(get(x))).ToList();
                    if (distinct.Count > 1)
                        rep.AddError($"{label}: مقدار ستون «{title}» در ردیف‌های {string.Join("، ", distinct.Select(d => d.First().RowNumber))} با هم تفاوت دارد.");
                    else if (distinct.Count == 1)
                        set(head, get(distinct[0].First()));
                    else if (required)
                        rep.AddError($"{label}: ستون «{title}» الزامی است.");
                }

                MergeText(FarmImportHeaders[1], x => x.Date, (h, v) => h.Date = v, true);
                MergeText(FarmImportHeaders[3], x => x.PlotName, (h, v) => h.PlotName = v, false);
                MergeText(FarmImportHeaders[4], x => x.ProductName, (h, v) => h.ProductName = v, false);
                MergeText(FarmImportHeaders[5], x => x.VarietyName, (h, v) => h.VarietyName = v, false);
                MergeText(FarmImportHeaders[6], x => x.HarvestMethod, (h, v) => h.HarvestMethod = v, false);
                MergeText(FarmImportHeaders[8], x => x.BasketType, (h, v) => h.BasketType = v, false);
                MergeText(FarmImportHeaders[9], x => x.Destination, (h, v) => h.Destination = v, false);
                MergeText(FarmImportHeaders[10], x => x.VehiclePlate, (h, v) => h.VehiclePlate = v, false);
                MergeText(FarmImportHeaders[11], x => x.DriverName, (h, v) => h.DriverName = v, false);
                MergeText(FarmImportHeaders[12], x => x.ContractorName, (h, v) => h.ContractorName = v, false);
                MergeText(FarmImportHeaders[13], x => x.Description, (h, v) => h.Description = v, false);

                var baskets = list.Where(x => x.BasketCount.HasValue).GroupBy(x => x.BasketCount!.Value).ToList();
                if (baskets.Count > 1)
                    rep.AddError($"{label}: مقدار ستون «{FarmImportHeaders[7]}» در ردیف‌های {string.Join("، ", baskets.Select(b => b.First().RowNumber))} با هم تفاوت دارد.");
                else if (baskets.Count == 1)
                    head.BasketCount = baskets[0].Key;

                if (head.VarietyName.Length > 0 && head.ProductName.Length == 0)
                    rep.AddError($"{label}: برای ثبت «{FarmImportHeaders[5]}» ستون «{FarmImportHeaders[4]}» هم باید پر باشد.");

                result.Add((head, list.Where(x => x.TicketNo.Length > 0).ToList()));
            }

            return result;
        }

        // ردیف خوانده‌شده از اکسل
        public class FarmImportRow
        {
            public int RowNumber { get; set; }
            public string PermitNumber { get; set; } = "";
            public string Date { get; set; } = "";
            public string FarmName { get; set; } = "";
            public string PlotName { get; set; } = "";
            public string ProductName { get; set; } = "";
            public string VarietyName { get; set; } = "";
            public string HarvestMethod { get; set; } = "";
            public int? BasketCount { get; set; }
            public string BasketType { get; set; } = "";
            public string Destination { get; set; } = "";
            public string VehiclePlate { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string ContractorName { get; set; } = "";
            public string Description { get; set; } = "";
            public string TicketNo { get; set; } = "";
            public int EmptyWeight { get; set; }
            public int GrossWeight { get; set; }
        }

        // ==========================================
        // متد کمکی
        // ==========================================
        private async Task LoadDropdownsForCycle(int cycleId)
        {
            ViewBag.Farms = new SelectList(await _db.Farms.ToListAsync(), "Id", "Name");
            ViewBag.Plots = new SelectList(await _db.Plots.Include(p => p.Farm).ToListAsync(), "Id", "Name");

            var cycle = await _db.HarvestCycles
                .Include(c => c.HarvestCycleCategories)
                .FirstOrDefaultAsync(c => c.Id == cycleId);
            var varietiesQuery = _db.ProductVarieties.Include(v => v.Product).AsQueryable();

            if (cycle != null)
            {
                var categoryIds = cycle.HarvestCycleCategories.Select(x => x.CategoryId).ToArray();
                // پشتیبانی از دوره‌های قدیمی که هنوز فقط CategoryId دارند
                if (categoryIds.Length == 0 && cycle.CategoryId.HasValue)
                    categoryIds = new[] { cycle.CategoryId.Value };

                if (categoryIds.Length > 0)
                    varietiesQuery = varietiesQuery.Where(v => v.Product != null && v.Product.CategoryId.HasValue
                        && categoryIds.Contains(v.Product.CategoryId.Value));
                else if (cycle.CategoryId.HasValue)
                    varietiesQuery = varietiesQuery.Where(v => v.Product!.CategoryId == cycle.CategoryId);
            }

            var varietiesList = await varietiesQuery
                .Select(v => new { Id = v.Id, DisplayName = (v.Product != null ? v.Product.Name : "") + " - " + v.Name })
                .ToListAsync();

            ViewBag.VarietyList = new SelectList(varietiesList, "Id", "DisplayName");
        }
    }
}