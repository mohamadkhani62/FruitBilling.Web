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
                .Include(p => p.Farm).Include(p => p.Plot)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .Include(p => p.BasculTickets)
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
                // تأیید مقصد و ایجاد برگه سورتینگ در یک تراکنش انجام می‌شود تا
                // در صورت خطای انتقال، وضعیت برگه مزرعه نیمه‌کاره تغییر نکند.
                await using var transaction = await _db.Database.BeginTransactionAsync();
                var transferredToSorting = false;

                // انتقال به سورتینگ فقط برای مقصد بازار/فروش انجام می‌شود.
                if (!string.IsNullOrWhiteSpace(permit.Destination) &&
                    (permit.Destination.Contains("بازار") || permit.Destination.Contains("فروش")))
                {
                    var farmName = permit.Farm?.Name;
                    if (!string.IsNullOrWhiteSpace(farmName))
                    {
                        var sameCyclePermits = await _db.SortingExitPermits.AsNoTracking()
                            .Where(s => s.HarvestCycleId == permit.HarvestCycleId)
                            .Select(s => new { s.PermitNumber, s.FarmName })
                            .ToListAsync();

                        var sameNumberPermit = string.IsNullOrWhiteSpace(permit.PermitNumber)
                            ? null
                            : sameCyclePermits.FirstOrDefault(s =>
                                Norm(s.PermitNumber) == Norm(permit.PermitNumber));

                        // استفاده از یک شماره برای دو مزرعه در یک دوره مجاز نیست.
                        if (sameNumberPermit != null && Norm(sameNumberPermit.FarmName) != Norm(farmName))
                        {
                            await transaction.RollbackAsync();
                            TempData["Warning"] = "تأیید مقصد انجام نشد؛ شماره مجوز تحویل این برگه قبلاً در همین دوره برای مزرعه دیگری استفاده شده است. ابتدا شماره برگه را اصلاح کنید.";
                            return RedirectToAction(nameof(Index), new { cycleId = cycleId ?? permit.HarvestCycleId, focusId = permit.Id });
                        }

                        // اگر همین شماره قبلاً برای همین مزرعه منتقل شده باشد، عملیات تکراری ایجاد نمی‌کنیم.
                        var alreadyExists = sameNumberPermit != null;

                        if (!alreadyExists)
                        {
                            var sourceItems = permit.Items?.OrderBy(x => x.Id).ToList()
                                ?? new List<FruitExitPermitItem>();

                            // پشتیبانی از برگه‌های قدیمی که هنوز Item ندارند.
                            if (sourceItems.Count == 0 && permit.VarietyId.HasValue)
                            {
                                sourceItems.Add(new FruitExitPermitItem
                                {
                                    VarietyId = permit.VarietyId.Value,
                                    BasketCount = permit.BasketCount ?? 0,
                                    BasketType = permit.BasketType,
                                    HarvestMethod = permit.HarvestMethod
                                });
                            }

                            if (sourceItems.Count == 0 || sourceItems.Any(x => x.VarietyId <= 0 || x.BasketCount <= 0))
                            {
                                await transaction.RollbackAsync();
                                TempData["Warning"] = "تأیید مقصد انجام نشد؛ برای انتقال به سورتینگ باید حداقل یک ردیف رقم با تعداد سبد معتبر وجود داشته باشد.";
                                return RedirectToAction(nameof(Index), new { cycleId = cycleId ?? permit.HarvestCycleId, focusId = permit.Id });
                            }

                            var varietyIds = sourceItems.Select(x => x.VarietyId).Distinct().ToArray();
                            var validVarietyIds = await _db.ProductVarieties
                                .Where(v => varietyIds.Contains(v.Id))
                                .Select(v => v.Id)
                                .ToListAsync();
                            if (validVarietyIds.Count != varietyIds.Length)
                            {
                                await transaction.RollbackAsync();
                                TempData["Warning"] = "تأیید مقصد انجام نشد؛ یکی از ارقام برگ خروج دیگر در اطلاعات پایه موجود نیست.";
                                return RedirectToAction(nameof(Index), new { cycleId = cycleId ?? permit.HarvestCycleId, focusId = permit.Id });
                            }

                            var newSortingPermit = new SortingExitPermit
                            {
                                PermitNumber = permit.PermitNumber,
                                Date = permit.Date,
                                HarvestCycleId = permit.HarvestCycleId,
                                BasketCount = sourceItems.Sum(x => x.BasketCount),
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

                            // قبض‌های قدیمیِ متصل به سربرگ، در صورت نبود قبض در ردیف‌ها،
                            // به اولین ردیف منتقل می‌شوند تا هیچ قبضی گم نشود.
                            var itemTicketsExist = sourceItems.Any(x => x.BasculTickets?.Count > 0);
                            for (var index = 0; index < sourceItems.Count; index++)
                            {
                                var sourceItem = sourceItems[index];
                                var sortingItem = new SortingExitPermitItem
                                {
                                    VarietyId = sourceItem.VarietyId,
                                    Grade = "بدون سورت",
                                    BasketCount = sourceItem.BasketCount
                                };

                                var tickets = sourceItem.BasculTickets?.OrderBy(t => t.Id).ToList()
                                    ?? new List<BasculTicket>();
                                if (!itemTicketsExist && index == 0)
                                    tickets = permit.BasculTickets?.OrderBy(t => t.Id).ToList()
                                        ?? new List<BasculTicket>();

                                foreach (var ticket in tickets)
                                {
                                    sortingItem.BasculTickets.Add(new SortingBasculTicket
                                    {
                                        TicketNo = ticket.TicketNo,
                                        BasketCount = ticket.BasketCount,
                                        EmptyWeight = ticket.EmptyWeight,
                                        GrossWeight = ticket.GrossWeight,
                                        NetWeight = ticket.NetWeight,
                                        CreatedAt = ticket.CreatedAt
                                    });
                                }

                                newSortingPermit.Items.Add(sortingItem);
                            }

                            var firstItem = newSortingPermit.Items[0];
                            var firstVariety = await _db.ProductVarieties
                                .Include(v => v.Product)
                                .FirstAsync(v => v.Id == firstItem.VarietyId);
                            newSortingPermit.VarietyId = firstItem.VarietyId;
                            newSortingPermit.ProductId = firstVariety.ProductId;
                            newSortingPermit.ProductType = firstVariety.Product?.Name ?? permit.ProductType;

                            _db.SortingExitPermits.Add(newSortingPermit);
                            transferredToSorting = true;
                        }
                    }
                }

                permit.Status = "تأیید شده در مقصد";
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Msg"] = transferredToSorting
                    ? "تأیید مقصد ثبت شد و همه ردیف‌های رقم و قبض‌های مرتبط به سورتینگ منتقل شدند."
                    : "تأیید مقصد با موفقیت ثبت شد.";
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
                .Include(p => p.Farm).Include(p => p.Plot)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .Include(p => p.BasculTickets)
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
            var query = _db.FruitExitPermits.Include(p => p.Farm).Include(p => p.Plot)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets).Include(p => p.BasculTickets).AsQueryable();
            if (cycleId.HasValue) query = query.Where(p => p.HarvestCycleId == cycleId.Value);
            var permits = await query.OrderBy(p => p.Date).ThenBy(p => p.Id).ToListAsync();
            var cycles = await _db.HarvestCycles.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.CycleNumber).ToListAsync();
            using var wb = new XLWorkbook();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void WriteSheet(string title, List<FruitExitPermit> records)
            {
                var ws = wb.AddWorksheet(SafeSheetName(title, usedNames));
                string[] headers = { "ردیف", "شماره برگ خروج", "تاریخ", "مزرعه", "قطعه", "محصول", "رقم", "روش برداشت", "تعداد سبد ردیف", "نوع سبد", "مقصد", "پلاک", "راننده", "پیمانکار برداشت", "شماره قبض", "تعداد سبد قبض", "وزن خالی (kg)", "وزن پر (kg)", "وزن خالص (kg)", "توضیحات" };
                for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
                int row = 2, sequence = 1;
                void WriteRow(FruitExitPermit p, FruitExitPermitItem? item, BasculTicket? ticket)
                {
                    ws.Cell(row, 1).Value = sequence++; ws.Cell(row, 2).Value = p.PermitNumber; ws.Cell(row, 3).Value = p.Date;
                    ws.Cell(row, 4).Value = p.Farm?.Name; ws.Cell(row, 5).Value = p.Plot?.Name;
                    ws.Cell(row, 6).Value = item?.Variety?.Product?.Name ?? p.ProductType; ws.Cell(row, 7).Value = item?.Variety?.Name ?? p.Variety?.Name;
                    ws.Cell(row, 8).Value = item?.HarvestMethod ?? p.HarvestMethod; ws.Cell(row, 9).Value = item?.BasketCount ?? p.BasketCount;
                    ws.Cell(row, 10).Value = item?.BasketType ?? p.BasketType; ws.Cell(row, 11).Value = p.Destination; ws.Cell(row, 12).Value = p.VehiclePlate;
                    ws.Cell(row, 13).Value = p.DriverName; ws.Cell(row, 14).Value = p.ContractorName; ws.Cell(row, 15).Value = ticket?.TicketNo;
                    if (ticket?.BasketCount is int count) ws.Cell(row, 16).Value = count;
                    if (ticket != null) { ws.Cell(row, 17).Value = ticket.EmptyWeight; ws.Cell(row, 18).Value = ticket.GrossWeight; ws.Cell(row, 19).Value = ticket.NetWeight; }
                    ws.Cell(row, 20).Value = p.Description; row++;
                }
                foreach (var p in records)
                {
                    if (p.Items?.Count > 0)
                        foreach (var item in p.Items.OrderBy(x => x.Id))
                        {
                            var tickets = item.BasculTickets?.OrderBy(t => t.Id).ToList() ?? new List<BasculTicket>();
                            if (tickets.Count == 0) WriteRow(p, item, null); else foreach (var t in tickets) WriteRow(p, item, t);
                        }
                    else
                    {
                        var tickets = p.BasculTickets?.OrderBy(t => t.Id).ToList() ?? new List<BasculTicket>();
                        if (tickets.Count == 0) WriteRow(p, null, null); else foreach (var t in tickets) WriteRow(p, null, t);
                    }
                }
                ws.RightToLeft = true; ws.SheetView.FreezeRows(1); ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true; ws.Columns().AdjustToContents();
            }
            foreach (var cycle in cycles) { var records = permits.Where(p => p.HarvestCycleId == cycle.Id).ToList(); if (records.Count == 0 && cycleId != cycle.Id) continue; WriteSheet(cycle.Name, records); }
            var legacy = permits.Where(p => p.HarvestCycleId == null).ToList(); if (legacy.Count > 0) WriteSheet("بدون دوره", legacy);
            if (wb.Worksheets.Count == 0) WriteSheet("خروج مزرعه", new List<FruitExitPermit>());
            using var ms = new MemoryStream(); wb.SaveAs(ms);
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

            var bytes = BuildTemplate(cycles.Select(x => x.Name), FarmImportHeaders, new[] { 9, 16, 17, 18 });
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
        //  - فقط .xlsx با قالب جدید ۲۰ ستونی یا قالب قدیمی ۱۷ ستونی
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
                    var cycles = await _db.HarvestCycles.AsNoTracking().Include(x => x.HarvestCycleCategories).ToListAsync();
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

                        // هر برگ با کلید (مزرعه + شماره برگ) یک بار ساخته می‌شود؛
                        // ردیف‌های محصول/رقم و قبض‌ها درون همان برگ جداگانه نگهداری می‌شوند.
                        var categoryIds = cycle.HarvestCycleCategories.Select(x => x.CategoryId).ToArray();
                        if (categoryIds.Length == 0 && cycle.CategoryId.HasValue)
                            categoryIds = new[] { cycle.CategoryId.Value };

                        foreach (var (first, itemGroups) in merged)
                        {
                            string farmKey = Norm(first.FarmName);
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
                                PermitNumber = first.PermitNumber, Date = NullIfEmpty(first.Date),
                                Farm = farm, Plot = plot, HarvestCycleId = cycle.Id,
                                Destination = NullIfEmpty(first.Destination), VehiclePlate = NullIfEmpty(first.VehiclePlate),
                                DriverName = NullIfEmpty(first.DriverName), ContractorName = NullIfEmpty(first.ContractorName),
                                Description = NullIfEmpty(first.Description), Status = "در انتظار توزین",
                                CreatedAt = DateTime.Now
                            };

                            foreach (var (itemHead, ticketRows) in itemGroups)
                            {
                                var productMatches = products.Where(p => Norm(p.Name) == Norm(itemHead.ProductName) &&
                                    (categoryIds.Length == 0 || categoryIds.Contains(p.CategoryId ?? 0))).ToList();
                                if (productMatches.Count != 1)
                                {
                                    rep.AddError($"برگه «{first.PermitNumber}»، ردیف {itemHead.RowNumber}: محصول «{itemHead.ProductName}» در گونه‌های این دوره پیدا نشد یا نام آن تکراری است.");
                                    continue;
                                }

                                var product = productMatches[0];
                                var varietyMatches = varieties.Where(v => v.ProductId == product.Id && Norm(v.Name) == Norm(itemHead.VarietyName)).ToList();
                                if (varietyMatches.Count != 1)
                                {
                                    rep.AddError($"برگه «{first.PermitNumber}»، ردیف {itemHead.RowNumber}: رقم «{itemHead.VarietyName}» برای محصول «{product.Name}» پیدا نشد یا تکراری است.");
                                    continue;
                                }

                                var variety = varietyMatches[0];
                                var detail = new FruitExitPermitItem
                                {
                                    FruitExitPermit = permit, VarietyId = variety.Id,
                                    BasketCount = itemHead.BasketCount!.Value,
                                    BasketType = NullIfEmpty(itemHead.BasketType),
                                    HarvestMethod = NullIfEmpty(itemHead.HarvestMethod)
                                };

                                var counts = new List<int>();
                                if (ticketRows.Count == 1 && !ticketRows[0].TicketBasketCount.HasValue)
                                    counts.Add(itemHead.BasketCount!.Value);
                                else if (ticketRows.Any(t => !t.TicketBasketCount.HasValue || t.TicketBasketCount.Value <= 0))
                                {
                                    rep.AddError($"برگه «{first.PermitNumber}»، رقم «{itemHead.VarietyName}»: تعداد سبد هر قبض باید مشخص و مثبت باشد.");
                                    continue;
                                }
                                else if (ticketRows.Count > 0)
                                    counts.AddRange(ticketRows.Select(t => t.TicketBasketCount!.Value));

                                if (ticketRows.Count > 0 && counts.Sum() != itemHead.BasketCount!.Value)
                                {
                                    rep.AddError($"برگه «{first.PermitNumber}»، رقم «{itemHead.VarietyName}»: مجموع سبد قبض‌ها ({counts.Sum()}) با تعداد سبد ردیف ({itemHead.BasketCount}) برابر نیست.");
                                    continue;
                                }

                                for (int i = 0; i < ticketRows.Count; i++)
                                {
                                    var t = ticketRows[i];
                                    detail.BasculTickets.Add(new BasculTicket
                                    {
                                        FruitExitPermit = permit, FruitExitPermitItem = detail,
                                        TicketNo = t.TicketNo, BasketCount = counts[i],
                                        EmptyWeight = t.EmptyWeight, GrossWeight = t.GrossWeight,
                                        NetWeight = t.GrossWeight - t.EmptyWeight, CreatedAt = DateTime.Now
                                    });
                                }
                                permit.Items.Add(detail);
                            }

                            if (rep.Errors.Count + rep.HiddenErrorCount > 0) continue;
                            if (permit.Items.Count == 0)
                            {
                                rep.AddError($"برگه «{first.PermitNumber}» ردیف محصول/رقم معتبری ندارد.");
                                continue;
                            }

                            var firstVariety = varieties.First(v => v.Id == permit.Items[0].VarietyId);
                            var firstProduct = products.First(p => p.Id == firstVariety.ProductId);
                            permit.VarietyId = firstVariety.Id;
                            permit.ProductId = firstProduct.Id;
                            permit.ProductType = firstProduct.Name.Length > 50 ? firstProduct.Name[..50] : firstProduct.Name;
                            permit.HarvestMethod = permit.Items[0].HarvestMethod;
                            permit.BasketType = permit.Items[0].BasketType;
                            permit.BasketCount = permit.Items.Sum(x => x.BasketCount);
                            permit.Status = permit.Items.All(x => x.BasculTickets.Any()) ? "توزین شده" : "در انتظار توزین";
                            toAdd.Add(permit);
                            rep.Tickets += permit.Items.Sum(x => x.BasculTickets.Count);
                            rep.Permits++;
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

        private static readonly string[] FarmImportHeaders =
        {
            "ردیف", "شماره برگ خروج", "تاریخ", "مزرعه", "قطعه", "محصول", "رقم",
            "روش برداشت", "تعداد سبد ردیف", "نوع سبد", "مقصد", "پلاک", "راننده",
            "پیمانکار برداشت", "شماره قبض", "تعداد سبد قبض", "وزن خالی (kg)",
            "وزن پر (kg)", "وزن خالص (kg)", "توضیحات"
        };

        private static readonly string[] LegacyFarmImportHeaders =
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

        private static List<FarmImportRow> ParseFarmSheet(IXLWorksheet ws, SheetImportReport rep)
        {
            var rows = new List<FarmImportRow>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow == 0) { rep.AddError("تب خالی است."); return rows; }

            bool newFormat = Enumerable.Range(0, FarmImportHeaders.Length)
                .All(i => Norm(ws.Cell(1, i + 1).GetString()) == Norm(FarmImportHeaders[i]));
            bool legacyFormat = Enumerable.Range(0, LegacyFarmImportHeaders.Length)
                .All(i => Norm(ws.Cell(1, i + 1).GetString()) == Norm(LegacyFarmImportHeaders[i]));
            if (!newFormat && !legacyFormat)
            {
                rep.AddError("سربرگ فایل با قالب جدید ۲۰ ستونی یا قالب قدیمی ۱۷ ستونی مطابقت ندارد.");
                return rows;
            }
            if ((newFormat && lastCol > FarmImportHeaders.Length) || (legacyFormat && lastCol > LegacyFarmImportHeaders.Length))
            {
                rep.AddError("فایل ستون اضافه دارد.");
                return rows;
            }
            if (lastRow < 2) { rep.AddError("تب هیچ ردیف داده‌ای ندارد."); return rows; }
            if (lastRow > 20001) { rep.AddError("حداکثر ۲۰٬۰۰۰ ردیف در هر تب مجاز است."); return rows; }

            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                int colCount = newFormat ? FarmImportHeaders.Length : LegacyFarmImportHeaders.Length;
                bool empty = true;
                for (int col = 1; col <= colCount; col++)
                {
                    var cell = row.Cell(col);
                    if (cell.DataType != XLDataType.Blank && !(cell.DataType == XLDataType.Text && string.IsNullOrWhiteSpace(cell.GetString())))
                    { empty = false; break; }
                }
                if (empty) continue;

                int before = rep.Errors.Count + rep.HiddenErrorCount;
                int offset = newFormat ? 1 : 0;
                int? sequence = null;
                if (newFormat) TryReadInt(row.Cell(1), FarmImportHeaders[0], r, rep, out sequence);

                var h = newFormat ? FarmImportHeaders : LegacyFarmImportHeaders;
                TryReadText(row.Cell(1 + offset), h[0 + offset], 15, r, rep, true, out var permitNo);
                TryReadDate(row.Cell(2 + offset), r, rep, out var date);
                TryReadText(row.Cell(3 + offset), h[2 + offset], 100, r, rep, true, out var farm);
                TryReadText(row.Cell(4 + offset), h[3 + offset], 100, r, rep, false, out var plot);
                TryReadText(row.Cell(5 + offset), h[4 + offset], 100, r, rep, true, out var product);
                TryReadText(row.Cell(6 + offset), h[5 + offset], 100, r, rep, true, out var variety);
                TryReadText(row.Cell(7 + offset), h[6 + offset], 50, r, rep, false, out var method);
                TryReadInt(row.Cell(8 + offset), h[7 + offset], r, rep, out var basket);
                TryReadText(row.Cell(9 + offset), h[8 + offset], 50, r, rep, false, out var basketType);
                TryReadText(row.Cell(10 + offset), h[9 + offset], 100, r, rep, false, out var destination);
                TryReadText(row.Cell(11 + offset), h[10 + offset], 20, r, rep, false, out var plate);
                TryReadText(row.Cell(12 + offset), h[11 + offset], 100, r, rep, false, out var driver);
                TryReadText(row.Cell(13 + offset), h[12 + offset], 100, r, rep, false, out var contractor);
                TryReadText(row.Cell(14 + offset), h[13 + offset], 500, r, rep, false, out var description);

                string ticketNo;
                int? ticketBasket = null, emptyW, grossW, netW = null;
                if (newFormat)
                {
                    TryReadText(row.Cell(15), FarmImportHeaders[14], 30, r, rep, false, out ticketNo);
                    TryReadInt(row.Cell(16), FarmImportHeaders[15], r, rep, out ticketBasket);
                    TryReadInt(row.Cell(17), FarmImportHeaders[16], r, rep, out emptyW);
                    TryReadInt(row.Cell(18), FarmImportHeaders[17], r, rep, out grossW);
                    TryReadInt(row.Cell(19), FarmImportHeaders[18], r, rep, out netW);
                }
                else
                {
                    TryReadText(row.Cell(15), LegacyFarmImportHeaders[14], 30, r, rep, false, out ticketNo);
                    TryReadInt(row.Cell(16), LegacyFarmImportHeaders[15], r, rep, out emptyW);
                    TryReadInt(row.Cell(17), LegacyFarmImportHeaders[16], r, rep, out grossW);
                }

                bool anyTicket = ticketNo.Length > 0 || ticketBasket.HasValue || emptyW.HasValue || grossW.HasValue || netW.HasValue;
                if (anyTicket && rep.Errors.Count + rep.HiddenErrorCount == before)
                {
                    if (ticketNo.Length == 0 || !emptyW.HasValue || !grossW.HasValue)
                        rep.AddError($"ردیف {r}: برای ثبت قبض باید شماره قبض، وزن خالی و وزن پر هر سه وارد شوند.");
                    else if (emptyW.Value < 0 || grossW.Value <= emptyW.Value)
                        rep.AddError($"ردیف {r}: وزن خالی نباید منفی باشد و وزن پر باید از وزن خالی بیشتر باشد.");
                    else if (ticketBasket.HasValue && ticketBasket.Value <= 0)
                        rep.AddError($"ردیف {r}: تعداد سبد قبض باید بیشتر از صفر باشد.");
                    else if (netW.HasValue && netW.Value != grossW.Value - emptyW.Value)
                        rep.AddError($"ردیف {r}: وزن خالص باید برابر تفاضل وزن پر و وزن خالی باشد.");
                }
                if (rep.Errors.Count + rep.HiddenErrorCount != before) continue;
                if (!basket.HasValue || basket.Value <= 0)
                { rep.AddError($"ردیف {r}: تعداد سبد ردیف باید بیشتر از صفر باشد."); continue; }

                rows.Add(new FarmImportRow
                {
                    RowNumber = r, SequenceNumber = sequence, IsLegacyFormat = legacyFormat,
                    PermitNumber = permitNo, Date = date, FarmName = farm, PlotName = plot,
                    ProductName = product, VarietyName = variety, HarvestMethod = method,
                    BasketCount = basket, BasketType = basketType, Destination = destination,
                    VehiclePlate = plate, DriverName = driver, ContractorName = contractor,
                    Description = description, TicketNo = ticketNo, TicketBasketCount = ticketBasket,
                    EmptyWeight = emptyW ?? 0, GrossWeight = grossW ?? 0, NetWeight = netW
                });
            }
            if (!rep.HasErrors && rows.Count == 0) rep.AddError("تب هیچ ردیف داده‌ای ندارد.");
            return rows;
        }

        private static List<(FarmImportRow Head, List<(FarmImportRow Head, List<FarmImportRow> TicketRows)> Items)> MergeFarmGroups(
            List<FarmImportRow> rows, SheetImportReport rep)
        {
            var result = new List<(FarmImportRow, List<(FarmImportRow, List<FarmImportRow>)>)>();
            foreach (var g in rows.GroupBy(x => (Permit: Norm(x.PermitNumber), Farm: Norm(x.FarmName))))
            {
                var list = g.ToList();
                var head = new FarmImportRow { RowNumber = list[0].RowNumber, PermitNumber = list[0].PermitNumber, FarmName = list[0].FarmName };
                string label = $"برگه «{head.PermitNumber}» (مزرعه «{head.FarmName}»)";

                void MergePermit(string title, Func<FarmImportRow, string> get, Action<FarmImportRow, string> set, bool required)
                {
                    var values = list.Where(x => !string.IsNullOrWhiteSpace(get(x))).GroupBy(x => Norm(get(x))).ToList();
                    if (values.Count > 1) rep.AddError($"{label}: ستون «{title}» در ردیف‌های {string.Join("، ", values.Select(v => v.First().RowNumber))} متفاوت است.");
                    else if (values.Count == 1) set(head, get(values[0].First()));
                    else if (required) rep.AddError($"{label}: ستون «{title}» الزامی است.");
                }

                MergePermit("تاریخ", x => x.Date, (h, v) => h.Date = v, true);
                MergePermit("قطعه", x => x.PlotName, (h, v) => h.PlotName = v, false);
                MergePermit("مقصد", x => x.Destination, (h, v) => h.Destination = v, false);
                MergePermit("پلاک", x => x.VehiclePlate, (h, v) => h.VehiclePlate = v, false);
                MergePermit("راننده", x => x.DriverName, (h, v) => h.DriverName = v, false);
                MergePermit("پیمانکار برداشت", x => x.ContractorName, (h, v) => h.ContractorName = v, false);
                MergePermit("توضیحات", x => x.Description, (h, v) => h.Description = v, false);

                var items = new List<(FarmImportRow, List<FarmImportRow>)>();
                foreach (var ig in list.GroupBy(x => (Product: Norm(x.ProductName), Variety: Norm(x.VarietyName))))
                {
                    var itemRows = ig.ToList();
                    var item = itemRows[0];
                    if (string.IsNullOrWhiteSpace(item.ProductName) || string.IsNullOrWhiteSpace(item.VarietyName))
                    { rep.AddError($"{label}، ردیف {item.RowNumber}: محصول و رقم الزامی است."); continue; }

                    var basketValues = itemRows.Select(x => x.BasketCount!.Value).Distinct().ToList();
                    if (basketValues.Count != 1 || basketValues[0] <= 0)
                    { rep.AddError($"{label}، رقم «{item.VarietyName}»: تعداد سبد ردیف باید در تمام ردیف‌های آن یکسان و مثبت باشد."); continue; }

                    foreach (var field in new[] {
                        (Title:"روش برداشت", Get:(Func<FarmImportRow,string>)(x=>x.HarvestMethod)),
                        (Title:"نوع سبد", Get:(Func<FarmImportRow,string>)(x=>x.BasketType))
                    })
                    {
                        var vals = itemRows.Where(x=>!string.IsNullOrWhiteSpace(field.Get(x))).GroupBy(x=>Norm(field.Get(x))).ToList();
                        if (vals.Count > 1) rep.AddError($"{label}، رقم «{item.VarietyName}»: ستون «{field.Title}» متفاوت است.");
                    }

                    var tickets = itemRows.Where(x=>!string.IsNullOrWhiteSpace(x.TicketNo)).ToList();
                    if (tickets.Count == 1 && itemRows.All(x => x.IsLegacyFormat) && !tickets[0].TicketBasketCount.HasValue)
                    {
                        // فایل ۱۷ ستونی قدیمی: یک قبض، تمام سبدهای ردیف را پوشش می‌دهد.
                    }
                    else if (tickets.Any(x=>!x.TicketBasketCount.HasValue || x.TicketBasketCount.Value<=0))
                        rep.AddError($"{label}، رقم «{item.VarietyName}»: تعداد سبد هر قبض باید مشخص و مثبت باشد.");
                    else if (tickets.Count>0 && tickets.Sum(x=>x.TicketBasketCount!.Value)!=basketValues[0])
                        rep.AddError($"{label}، رقم «{item.VarietyName}»: مجموع سبد قبض‌ها باید برابر تعداد سبد ردیف باشد.");

                    item.BasketCount = basketValues[0];
                    item.HarvestMethod = itemRows.Select(x=>x.HarvestMethod).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x)) ?? "";
                    item.BasketType = itemRows.Select(x=>x.BasketType).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x)) ?? "";
                    items.Add((item,tickets));
                }
                result.Add((head,items));
            }
            return result;
        }

        public class FarmImportRow
        {
            public int RowNumber { get; set; }
            public int? SequenceNumber { get; set; }
            public bool IsLegacyFormat { get; set; }
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
            public int? TicketBasketCount { get; set; }
            public int EmptyWeight { get; set; }
            public int GrossWeight { get; set; }
            public int? NetWeight { get; set; }
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