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
    public class SortingExitPermitsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IPermissionService _permissionService;
        private readonly UserManager<IdentityUser> _userManager;

        public SortingExitPermitsController(AppDbContext db, IPermissionService permissionService, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _permissionService = permissionService;
            _userManager = userManager;
        }

        // ==========================================
        // ۱. لیست برگه‌های خروج سورتینگ
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(int? cycleId, int? page, int? focusId, int pageSize = 50)
        {
            // اگر دوره‌ای انتخاب نشده، آخرین دوره ثبت‌شده انتخاب می‌شود.
            // cycleId = 0 یعنی «برگه‌های بدون دوره» (داده‌های قدیمی)
            if (!cycleId.HasValue)
            {
                var latestCycle = await _db.HarvestCycles.AsNoTracking()
                    .OrderByDescending(x => x.Year)
                    .ThenByDescending(x => x.CycleNumber)
                    .FirstOrDefaultAsync();
                cycleId = latestCycle?.Id ?? 0;
            }
            int cid = cycleId.Value;

            await LoadCyclesAsync(cid, includeNone: true);
            ViewBag.SelectedCycleId = cid;
            ViewBag.CurrentCycle = cid > 0
                ? await _db.HarvestCycles.AsNoTracking().Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == cid)
                : null;

            var query = FilterByCycle(
                    _db.SortingExitPermits.AsNoTracking()
                        .Include(p => p.BasculTickets)
                        .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                        .Include(p => p.Items).ThenInclude(x => x.BasculTickets), cid)
                .OrderBy(p => p.Date)
                .ThenBy(p => p.Id)
                .AsQueryable();

            if (pageSize <= 0) pageSize = 50;

            // ترتیب رکوردها؛ برای پیدا کردن «آخرین رکورد» و صفحه‌ی رکورد فوکوس‌شده
            var orderedIds = await query.Select(p => p.Id).ToListAsync();
            var total = orderedIds.Count;
            int totalPages = (int)Math.Ceiling((double)total / pageSize);
            if (totalPages == 0) totalPages = 1;

            int currentPage;
            if (focusId.HasValue && orderedIds.Contains(focusId.Value))
            {
                // بازگشت از زیرصفحه‌ها یا بعد از تغییر: به صفحه‌ای برو که رکورد در آن است و فوکوس کن
                currentPage = orderedIds.IndexOf(focusId.Value) / pageSize + 1;
            }
            else if (page.HasValue)
            {
                // کلیک روی شماره صفحه (بدون فوکوس)
                currentPage = Math.Min(Math.Max(page.Value, 1), totalPages);
                focusId = null;
            }
            else
            {
                // ورود از صفحه‌ی بالاتر: آخرین رکورد
                currentPage = totalPages;
                focusId = total > 0 ? orderedIds[total - 1] : (int?)null;
            }

            var items = await query
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Page = currentPage;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.FocusId = focusId;

            // 🆕 شمارش تعداد در انتظار تأیید (فقط همین دوره)
            var pendingApprovalCount = await FilterByCycle(_db.SortingExitPermits, cid)
                .CountAsync(p => p.Status == "توزین شده");
            ViewBag.PendingApprovalCount = pendingApprovalCount;

            return View(items);
        }
        // ==========================================
        // ۲. ایجاد (Create)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Create(int? cycleId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // 🆕 قانون: اگر کاربر هیچ دسترسی ویرایشی ندارد، اجازه ایجاد هم ندارد
            var perms = await _permissionService.GetEditableFieldsAsync(userId!, "SortingExitPermits", new SortingExitPermit());
            if (!perms.Values.Any(v => v == true))
            {
                TempData["Warning"] = "شما دسترسی ایجاد برگه خروج سورتینگ را ندارید.";
                return RedirectToAction(nameof(Index));
            }

            // 🆕 صدور برگه فقط داخل یک دوره برداشت معتبر ممکن است
            var cycle = cycleId.HasValue && cycleId.Value > 0
                ? await _db.HarvestCycles.AsNoTracking().Include(x => x.HarvestCycleCategories)
                    .FirstOrDefaultAsync(x => x.Id == cycleId.Value)
                : null;
            if (cycle == null)
            {
                TempData["Warning"] = "ابتدا باید یک دوره برداشت را انتخاب کنید.";
                return RedirectToAction(nameof(Index));
            }

            var fa = new System.Globalization.PersianCalendar();
            var today = DateTime.Now;
            ViewBag.CurrentCycle = cycle;
            await LoadVarietiesForCycleAsync(cycle.Id);
            return View(new SortingExitPermit
            {
                Date = $"{fa.GetYear(today):0000}/{fa.GetMonth(today):00}/{fa.GetDayOfMonth(today):00}",
                Status = "در انتظار توزین",
                HarvestCycleId = cycle.Id
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SortingExitPermit model, List<SortingExitPermitItem> items)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var perms = await _permissionService.GetEditableFieldsAsync(userId!, "SortingExitPermits", model);
            if (!perms.Values.Any(v => v == true))
            {
                TempData["Warning"] = "شما دسترسی ایجاد برگه را ندارید.";
                return RedirectToAction(nameof(Index));
            }

            // 🆕 دوره باید معتبر باشد
            var cycle = model.HarvestCycleId.HasValue
                ? await _db.HarvestCycles.Include(x => x.HarvestCycleCategories)
                    .FirstOrDefaultAsync(x => x.Id == model.HarvestCycleId.Value)
                : null;
            if (cycle == null)
            {
                TempData["Warning"] = "دوره برداشت نامعتبر است. لطفاً دوباره از لیست دوره را انتخاب کنید.";
                return RedirectToAction(nameof(Index));
            }

            items ??= new List<SortingExitPermitItem>();
            items = items.Where(x => x.VarietyId > 0 || x.BasketCount > 0 || !string.IsNullOrWhiteSpace(x.Grade)).ToList();
            if (items.Count == 0)
                ModelState.AddModelError("Items", "حداقل یک ردیف رقم و درجه اضافه کنید.");
            if (items.Any(x => x.VarietyId <= 0 || x.BasketCount <= 0 || string.IsNullOrWhiteSpace(x.Grade)))
                ModelState.AddModelError("Items", "برای هر ردیف، رقم، درجه و تعداد سبد معتبر وارد کنید.");

            var categoryIds = cycle.HarvestCycleCategories.Select(x => x.CategoryId).ToArray();
            if (categoryIds.Length == 0 && cycle.CategoryId.HasValue)
                categoryIds = new[] { cycle.CategoryId.Value };
            var varietyIds = items.Select(x => x.VarietyId).Distinct().ToArray();
            var varieties = await _db.ProductVarieties.Include(v => v.Product)
                .Where(v => varietyIds.Contains(v.Id)).ToListAsync();
            if (varieties.Count != varietyIds.Length || varieties.Any(v => v.Product == null ||
                (categoryIds.Length > 0 && !categoryIds.Contains(v.Product.CategoryId ?? 0))))
                ModelState.AddModelError("Items", "یکی از ارقام انتخاب‌شده متعلق به گونه‌های این دوره برداشت نیست.");

            // برای «جابجایی» باید نوع خروج مشخص شود (اگر کاربر به این فیلد دسترسی دارد)
            if (perms.GetValueOrDefault("SaleType") && perms.GetValueOrDefault("MovementType")
                && SortingMovement.CanonicalSaleType(model.SaleType) == SortingMovement.Transfer
                && SortingMovement.Resolve(model.SaleType, model.MovementType) == null)
            {
                ModelState.AddModelError(nameof(model.MovementType), "برای «جابجایی» باید نوع خروج را انتخاب کنید (خروج برای سورت یا خروج برای ذخیره‌سازی).");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.CurrentCycle = cycle;
                await LoadVarietiesForCycleAsync(cycle.Id);
                return View(model);
            }

            // 🆕 فقط فیلدهای مجاز را در رکورد جدید ذخیره می‌کنیم
            var newPermit = new SortingExitPermit
            {
                CreatedAt = DateTime.Now,
                Status = "در انتظار توزین",
                HarvestCycleId = cycle.Id,
                PermitNumber = perms.GetValueOrDefault("PermitNumber") ? model.PermitNumber : "",
                Date = perms.GetValueOrDefault("Date") ? model.Date : "",
                BasketCount = perms.GetValueOrDefault("BasketCount") ? model.BasketCount : null,
                ProductType = perms.GetValueOrDefault("ProductType") ? model.ProductType : null,
                Grade = perms.GetValueOrDefault("Grade") ? model.Grade : null,
                FarmName = perms.GetValueOrDefault("FarmName") ? model.FarmName : null,
                VehiclePlate = perms.GetValueOrDefault("VehiclePlate") ? model.VehiclePlate : null,
                Destination = perms.GetValueOrDefault("Destination") ? model.Destination : null,
                SaleType = perms.GetValueOrDefault("SaleType") ? model.SaleType : null,
                MovementType = SortingMovement.Resolve(
                    perms.GetValueOrDefault("SaleType") ? model.SaleType : null,
                    perms.GetValueOrDefault("MovementType") ? model.MovementType : null),
                Source = perms.GetValueOrDefault("Source") ? model.Source : null,
                SortingContractor = perms.GetValueOrDefault("SortingContractor") ? model.SortingContractor : null,
                Description = perms.GetValueOrDefault("Description") ? model.Description : null
            };

            foreach (var item in items)
            {
                item.SortingExitPermit = newPermit;
                item.Variety = null!;
            }
            var firstVariety = varieties.First(v => v.Id == items[0].VarietyId);
            newPermit.Items = items;
            newPermit.VarietyId = firstVariety.Id;
            newPermit.ProductId = firstVariety.ProductId;
            newPermit.ProductType = firstVariety.Product?.Name;
            newPermit.Grade = items[0].Grade;
            newPermit.BasketCount = items.Sum(x => x.BasketCount);

            _db.SortingExitPermits.Add(newPermit);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "برگ خروج سورتینگ صادر شد. جهت توزین به باسکول مراجعه کنید.";
            return RedirectToAction(nameof(Index), new { cycleId = cycle.Id, focusId = newPermit.Id });
        }

        // ==========================================
        // ۳. ویرایش (Edit)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id, int? page, int? focusId)
        {
            var permit = await _db.SortingExitPermits
                .Include(p => p.BasculTickets)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .Include(p => p.HarvestCycle)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (permit == null) return NotFound();

            // 🆕 فقط مدیر می‌تواند دوره برگه را تغییر دهد (مثلاً انتقال برگه‌های قدیمی به یک دوره)
            if (User.IsInRole("Manager"))
                await LoadCyclesAsync(permit.HarvestCycleId, includeNone: false);
            await LoadVarietiesForCycleAsync(permit.HarvestCycleId ?? 0);

            ViewBag.ReturnPage = page;
            ViewBag.ReturnCycleId = permit.HarvestCycleId ?? 0;
            ViewBag.ReturnFocusId = focusId ?? id; // اگر focusId نبود، خود id

            return View(permit);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SortingExitPermit model, int? page, int? focusId)
        {
            if (id != model.Id) return BadRequest();
            var permit = await _db.SortingExitPermits
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if ((permit.Status == "تأیید فروش" || permit.Status == "تأیید شده توسط فروش") && !User.IsInRole("Manager"))
            {
                TempData["Warning"] = "این برگه تأیید شده است و فقط مدیر می‌تواند آن را تغییر دهد.";
                return RedirectToAction(nameof(Index), new { cycleId = permit.HarvestCycleId ?? 0, page, focusId = id });
            }

            var perms = await _permissionService.GetEditableFieldsAsync(userId!, "SortingExitPermits", permit);
            if (model.Items != null)
            {
                var submitted = model.Items.Where(x => x.Id > 0 || x.VarietyId > 0 || x.BasketCount > 0 || !string.IsNullOrWhiteSpace(x.Grade)).ToList();
                if (submitted.Count == 0)
                    ModelState.AddModelError("Items", "حداقل یک ردیف رقم و درجه باید باقی بماند.");
                if (submitted.GroupBy(x => x.Id).Any(g => g.Key > 0 && g.Count() > 1))
                    ModelState.AddModelError("Items", "شناسه ردیف تکراری است.");
                if (submitted.Any(x => x.VarietyId <= 0 || x.BasketCount <= 0 || string.IsNullOrWhiteSpace(x.Grade)))
                    ModelState.AddModelError("Items", "برای هر ردیف، رقم، درجه و تعداد سبد معتبر وارد کنید.");

                var cycleIdForItems = User.IsInRole("Manager") && model.HarvestCycleId.HasValue
                    ? model.HarvestCycleId.Value : permit.HarvestCycleId ?? 0;
                var cycle = cycleIdForItems > 0
                    ? await _db.HarvestCycles.Include(x => x.HarvestCycleCategories).FirstOrDefaultAsync(x => x.Id == cycleIdForItems)
                    : null;
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
                bool canManageItemRows = perms.GetValueOrDefault("ProductType")
                    && perms.GetValueOrDefault("Grade") && perms.GetValueOrDefault("BasketCount");
                if ((hasNewItems || hasRemovedItems) && !canManageItemRows)
                    ModelState.AddModelError("Items", "برای افزودن یا حذف ردیف، مجوز ویرایش رقم، درجه و تعداد سبد لازم است.");
                if (submittedIds.Any(x => permit.Items.All(dbItem => dbItem.Id != x)))
                    ModelState.AddModelError("Items", "یک ردیف ارسالی متعلق به این برگ خروج نیست.");
                foreach (var removed in permit.Items.Where(x => !submittedIds.Contains(x.Id)))
                    if (removed.BasculTickets.Any())
                        ModelState.AddModelError("Items", $"ردیف «{removed.Grade}» قبض باسکول دارد و حذف آن مجاز نیست.");

                if (!ModelState.IsValid)
                {
                    await LoadCyclesAsync(permit.HarvestCycleId, includeNone: false);
                    await LoadVarietiesForCycleAsync(cycleIdForItems);
                    ViewBag.ReturnPage = page;
                    ViewBag.ReturnCycleId = permit.HarvestCycleId ?? 0;
                    ViewBag.ReturnFocusId = focusId ?? id;
                    return View(model);
                }

                foreach (var removed in permit.Items.Where(x => !submittedIds.Contains(x.Id)).ToList())
                    _db.SortingExitPermitItems.Remove(removed);
                foreach (var posted in submitted)
                {
                    var item = posted.Id == 0 ? new SortingExitPermitItem() : permit.Items.First(x => x.Id == posted.Id);
                    if (posted.Id == 0)
                    {
                        item.SortingExitPermitId = permit.Id;
                        permit.Items.Add(item);
                    }
                    if (perms.GetValueOrDefault("ProductType")) item.VarietyId = posted.VarietyId;
                    if (perms.GetValueOrDefault("Grade")) item.Grade = posted.Grade.Trim();
                    if (perms.GetValueOrDefault("BasketCount")) item.BasketCount = posted.BasketCount;
                }
                if (permit.Items.Count > 0)
                {
                    var first = permit.Items.OrderBy(x => x.Id).First();
                    var firstVariety = varieties.FirstOrDefault(v => v.Id == first.VarietyId)
                        ?? await _db.ProductVarieties.Include(v => v.Product).FirstAsync(v => v.Id == first.VarietyId);
                    permit.VarietyId = first.VarietyId;
                    permit.ProductId = firstVariety.ProductId;
                    permit.ProductType = firstVariety.Product?.Name;
                    permit.Grade = first.Grade;
                    permit.BasketCount = permit.Items.Sum(x => x.BasketCount);
                }
            }

            if (perms.GetValueOrDefault("PermitNumber")) permit.PermitNumber = model.PermitNumber;
            if (perms.GetValueOrDefault("Date")) permit.Date = model.Date;
            if (perms.GetValueOrDefault("FarmName")) permit.FarmName = model.FarmName;
            if (perms.GetValueOrDefault("VehiclePlate")) permit.VehiclePlate = model.VehiclePlate;
            if (perms.GetValueOrDefault("Destination")) permit.Destination = model.Destination;
            if (perms.GetValueOrDefault("SaleType")) permit.SaleType = model.SaleType;
            permit.MovementType = SortingMovement.Resolve(permit.SaleType,
                perms.GetValueOrDefault("MovementType") ? model.MovementType : permit.MovementType);
            if (SortingMovement.CanonicalSaleType(permit.SaleType) == SortingMovement.Transfer && permit.MovementType == null)
                ModelState.AddModelError(nameof(model.MovementType), "برای «جابجایی» نوع خروج را انتخاب کنید.");
            if (perms.GetValueOrDefault("Source")) permit.Source = model.Source;
            if (perms.GetValueOrDefault("SortingContractor")) permit.SortingContractor = model.SortingContractor;
            if (perms.GetValueOrDefault("Description")) permit.Description = model.Description;

            bool cycleChanged = false;
            if (User.IsInRole("Manager") && model.HarvestCycleId.HasValue && model.HarvestCycleId != permit.HarvestCycleId)
            {
                if (await _db.HarvestCycles.AnyAsync(x => x.Id == model.HarvestCycleId.Value))
                {
                    permit.HarvestCycleId = model.HarvestCycleId;
                    cycleChanged = true;
                }
                else ModelState.AddModelError(nameof(model.HarvestCycleId), "دوره برداشت انتخاب‌شده معتبر نیست.");
            }
            if (!ModelState.IsValid)
            {
                await LoadCyclesAsync(permit.HarvestCycleId, includeNone: false);
                await LoadVarietiesForCycleAsync(permit.HarvestCycleId ?? 0);
                ViewBag.ReturnPage = page;
                ViewBag.ReturnCycleId = permit.HarvestCycleId ?? 0;
                ViewBag.ReturnFocusId = focusId ?? id;
                return View(model);
            }

            await _db.SaveChangesAsync();
            TempData["Msg"] = "تغییرات مجاز ذخیره شد.";
            return RedirectToAction(nameof(Index), new
            {
                cycleId = permit.HarvestCycleId ?? 0,
                page = cycleChanged ? null : page,
                focusId = focusId ?? id
            });
        }

        // ==========================================
        // ۶. حذف برگه (Delete)
        // ==========================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int? page)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // 🆕 چک دسترسی: بعد از تأیید، فقط مدیر می‌تواند حذف کند
            var permit = await _db.SortingExitPermits
                .Include(p => p.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (permit == null) return NotFound();

            if ((permit.Status == "تأیید فروش" || permit.Status == "تأیید شده توسط فروش")
                && !User.IsInRole("Manager"))
            {
                TempData["Warning"] = "این برگه تأیید شده و فقط مدیر می‌تواند آن را حذف کند.";
                return RedirectToAction(nameof(Index), new { cycleId = permit.HarvestCycleId ?? 0, focusId = id });
            }

            int redirectCycleId = permit.HarvestCycleId ?? 0;

            // 🆕 پیدا کردن رکورد قبلی (در همان دوره) برای فوکوس بعد از حذف
            var allIds = await FilterByCycle(_db.SortingExitPermits.AsNoTracking(), redirectCycleId)
                .OrderBy(p => p.Date).ThenBy(p => p.Id)
                .Select(p => p.Id)
                .ToListAsync();

            int index = allIds.IndexOf(id);
            int? focusId = null;
            if (index > 0)
                focusId = allIds[index - 1];
            else if (allIds.Count > 1)
                focusId = allIds[1];

            _db.SortingExitPermits.Remove(permit);
            await _db.SaveChangesAsync();

            TempData["Msg"] = "برگه با موفقیت حذف شد.";
            return RedirectToAction(nameof(Index), new { cycleId = redirectCycleId, page = page, focusId = focusId });
        }

        // ==========================================
        // ۴. مدیریت قبض‌های باسکول
        // ==========================================
        private async Task<bool> CanManageBasculTicketsAsync(string userId, SortingExitPermit permit)
        {
            if (string.IsNullOrEmpty(userId)) return false;

            // 🆕 قانون قفل پس از تأیید
            if ((permit.Status == "تأیید فروش" || permit.Status == "تأیید شده توسط فروش") && !User.IsInRole("Manager"))
                return false;

            var perms = await _permissionService.GetEditableFieldsAsync(userId, "SortingExitPermits", permit);
            return perms.GetValueOrDefault("TicketNo_Sort") || perms.GetValueOrDefault("EmptyWeight_Sort") ||
                   perms.GetValueOrDefault("GrossWeight_Sort") || perms.GetValueOrDefault("NetWeight_Sort");
        }

        [HttpGet]
        public async Task<IActionResult> EditTicket(int id, int permitId, int? page)
        {
            var ticket = await _db.SortingBasculTickets.FindAsync(id);
            var permit = await _db.SortingExitPermits.FindAsync(permitId);
            if (ticket == null || permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (!await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی ویرایش قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            ViewBag.PermitId = permitId;
            ViewBag.ReturnPage = page;
            return View(ticket);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTicket(int id, SortingBasculTicket model, int permitId, int? page)
        {
            if (id != model.Id) return BadRequest();
            var ticket = await _db.SortingBasculTickets.FindAsync(id);
            var permit = await _db.SortingExitPermits.FindAsync(permitId);
            if (ticket == null || permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (!await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی ویرایش قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            if (ModelState.IsValid)
            {
                ticket.TicketNo = model.TicketNo;
                ticket.EmptyWeight = model.EmptyWeight;
                ticket.GrossWeight = model.GrossWeight;
                ticket.NetWeight = model.GrossWeight - model.EmptyWeight; // محاسبه مجدد در سرور
                await _db.SaveChangesAsync();
                TempData["Msg"] = "قبض باسکول ویرایش شد.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            ViewBag.PermitId = permitId;
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTicket(int id, int permitId, int? page)
        {
            var permit = await _db.SortingExitPermits.Include(p => p.BasculTickets).FirstOrDefaultAsync(p => p.Id == permitId);
            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (!await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی حذف قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            if (permit.BasculTickets.Count <= 1)
            {
                TempData["Warning"] = "امکان حذف آخرین قبض وجود ندارد.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            var ticket = permit.BasculTickets.FirstOrDefault(t => t.Id == id);
            if (ticket != null)
            {
                _db.SortingBasculTickets.Remove(ticket);
                await _db.SaveChangesAsync();
                TempData["Msg"] = "قبض باسکول حذف شد.";
            }
            return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTicket(int permitId, string ticketNo, int emptyWeight, int grossWeight, int? page)
        {
            var permit = await _db.SortingExitPermits.FindAsync(permitId);
            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (!await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی افزودن قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            if (grossWeight > emptyWeight)
            {
                var newTicket = new SortingBasculTicket
                {
                    SortingExitPermitId = permitId,
                    TicketNo = ticketNo,
                    EmptyWeight = emptyWeight,
                    GrossWeight = grossWeight,
                    NetWeight = grossWeight - emptyWeight,
                    CreatedAt = DateTime.Now
                };
                _db.SortingBasculTickets.Add(newTicket);
                await _db.SaveChangesAsync();
                TempData["Msg"] = "قبض جدید اضافه شد.";
            }
            else
            {
                TempData["Warning"] = "وزن پر باید بزرگتر از وزن خالی باشد.";
            }
            return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
        }

        // ==========================================
        // ۵. بقیه متدها (توزین، چاپ، تأیید فروش، اکسل)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Weigh(int id)
        {
            var permit = await _db.SortingExitPermits
                .Include(p => p.BasculTickets)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (permit == null) return NotFound();
            // امکان بازبینی و اصلاح قبض‌ها پس از توزین نیز مجاز است؛
            // کنترل دسترسی در ثبت POST مجدداً بررسی می‌شود.
            return View(permit);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Weigh(int id, SortingExitPermit model)
        {
            var permit = await _db.SortingExitPermits
                .Include(p => p.BasculTickets)
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (permit == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId) || !await CanManageBasculTicketsAsync(userId!, permit))
            {
                TempData["Warning"] = "شما دسترسی ثبت توزین را ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId = permit.HarvestCycleId ?? 0, focusId = permit.Id });
            }

            model.Items ??= new List<SortingExitPermitItem>();
            var submittedItems = model.Items.ToList();
            var validItemIds = permit.Items.Select(x => x.Id).ToHashSet();
            if (submittedItems.GroupBy(x => x.Id).Any(g => g.Count() > 1) ||
                submittedItems.Any(x => !validItemIds.Contains(x.Id)) ||
                !submittedItems.Select(x => x.Id).ToHashSet().SetEquals(validItemIds))
            {
                TempData["Warning"] = "ردیف‌های ارسالی با ردیف‌های همین برگ سورتینگ مطابقت ندارند. هیچ تغییری ذخیره نشد.";
                return RedirectToAction(nameof(Weigh), new { id });
            }

            var ticketIdsSeen = new HashSet<int>();
            var prepared = new List<(SortingExitPermitItem Item, List<SortingBasculTicket> Tickets)>();
            foreach (var item in permit.Items)
            {
                var submittedItem = submittedItems.Single(x => x.Id == item.Id);
                var tickets = (submittedItem.BasculTickets ?? new List<SortingBasculTicket>())
                    .Where(t => !string.IsNullOrWhiteSpace(t.TicketNo) || t.GrossWeight > 0 ||
                                t.EmptyWeight > 0 || (t.BasketCount ?? 0) > 0)
                    .ToList();

                var existingTicketIds = item.BasculTickets.Select(t => t.Id).ToHashSet();
                if (tickets.Any(t => t.Id > 0 && (!existingTicketIds.Contains(t.Id) || !ticketIdsSeen.Add(t.Id))))
                {
                    TempData["Warning"] = "یکی از قبض‌ها متعلق به این ردیف نیست یا شناسه آن تکراری است. هیچ تغییری ذخیره نشد.";
                    return RedirectToAction(nameof(Weigh), new { id });
                }

                if (tickets.Count == 1) tickets[0].BasketCount = item.BasketCount;
                if (tickets.Count == 0 ||
                    tickets.Any(t => string.IsNullOrWhiteSpace(t.TicketNo) || t.GrossWeight <= t.EmptyWeight ||
                                     t.EmptyWeight < 0 || (t.BasketCount ?? 0) <= 0) ||
                    tickets.Sum(t => t.BasketCount ?? 0) != item.BasketCount)
                {
                    TempData["Warning"] = $"برای درجه {item.Grade} قبض معتبر ثبت کنید؛ مجموع سبد قبض‌ها باید دقیقاً {item.BasketCount} باشد و وزن پر از وزن خالی بیشتر باشد. هیچ تغییری ذخیره نشد.";
                    return RedirectToAction(nameof(Weigh), new { id });
                }
                prepared.Add((item, tickets));
            }

            var totalNetWeight = 0;
            foreach (var entry in prepared)
            {
                var item = entry.Item;
                var tickets = entry.Tickets;
                var retainedIds = tickets.Where(t => t.Id > 0).Select(t => t.Id).ToHashSet();
                _db.SortingBasculTickets.RemoveRange(item.BasculTickets.Where(t => !retainedIds.Contains(t.Id)));

                foreach (var posted in tickets)
                {
                    SortingBasculTicket ticket;
                    if (posted.Id > 0)
                    {
                        ticket = item.BasculTickets.Single(t => t.Id == posted.Id);
                    }
                    else
                    {
                        ticket = new SortingBasculTicket
                        {
                            SortingExitPermitId = permit.Id,
                            SortingExitPermitItemId = item.Id,
                            CreatedAt = DateTime.Now
                        };
                        _db.SortingBasculTickets.Add(ticket);
                    }

                    ticket.SortingExitPermitId = permit.Id;
                    ticket.SortingExitPermitItemId = item.Id;
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
            return RedirectToAction(nameof(Index), new { cycleId = permit.HarvestCycleId ?? 0, focusId = permit.Id });
        }

                [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmBySales(int id, int? cycleId)
        {
            // 🆕 ۱. گرفتن شناسه کاربر جاری
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // 🆕 ۲. چک کردن دسترسی تأیید از سیستم دسترسی‌ها
            if (!await _permissionService.CanApproveAsync(userId, "SortingExitPermits"))
            {
                TempData["Warning"] = "شما دسترسی تأیید فروش را ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId = cycleId, focusId = id });
            }

            // ۳. پیدا کردن رکورد
            var permit = await _db.SortingExitPermits.Include(p => p.BasculTickets).FirstOrDefaultAsync(p => p.Id == id);
            if (permit == null) return NotFound();

            // ۴. بررسی وضعیت و اعمال تأیید
            if (permit.Status == "توزین شده")
            {
                permit.Status = "تأیید فروش";
                string approvalNote = $"تأیید فروش در {DateTime.Now:yyyy/MM/dd HH:mm} توسط {User.Identity?.Name}";
                permit.Description = string.IsNullOrWhiteSpace(permit.Description)
                    ? approvalNote
                    : $"{permit.Description} | {approvalNote}";

                await _db.SaveChangesAsync();
                TempData["Msg"] = "برگه با موفقیت تأیید شد.";
            }
            else
            {
                TempData["Warning"] = "فقط برگه‌های توزین شده قابل تأیید هستند.";
            }

            return RedirectToAction(nameof(Index), new { cycleId = permit.HarvestCycleId ?? 0, focusId = permit.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Print(int id)
        {
            var permit = await _db.SortingExitPermits.Include(p => p.BasculTickets).FirstOrDefaultAsync(p => p.Id == id);
            if (permit == null) return NotFound();
            return View(permit);
        }

        [HttpGet]
        public async Task<IActionResult> Export(int? cycleId)
        {
            // با cycleId فقط همان دوره (یک تب)؛ بدون cycleId همه‌ی دوره‌ها (یک تب برای هر دوره)
            var exportQuery = _db.SortingExitPermits.Include(p => p.BasculTickets).AsQueryable();
            if (cycleId.HasValue) exportQuery = FilterByCycle(exportQuery, cycleId.Value);
            var list = await exportQuery.OrderBy(p => p.Date).ThenBy(p => p.Id).ToListAsync();

            var cycles = await _db.HarvestCycles.AsNoTracking()
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.CycleNumber)
                .ToListAsync();

            using var wb = new XLWorkbook();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // نام تب = نام دوره برداشت (مثل فایل ورودی)؛ نام دوره در ردیف‌ها تکرار نمی‌شود
            void WriteSheet(string title, List<SortingExitPermit> items)
            {
                var ws = wb.AddWorksheet(SafeSheetName(title, usedNames));

                string[] headers = { "ردیف", "شماره برگ خروج", "تاریخ", "تعداد سبد", "نوع محصول", "درجه", "نام مزرعه", "شماره پلاک", "مقصد", "نوع فروش", "مبدا", "پیمانکار سورت", "توضیحات", "وزن خالص", "نوع خروج" };
                for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

                for (int i = 0; i < items.Count; i++)
                {
                    var r = items[i];
                    int row = i + 2;
                    var totalNet = r.BasculTickets?.Sum(t => t.NetWeight) ?? 0;

                    ws.Cell(row, 1).Value = i + 1;
                    ws.Cell(row, 2).Value = r.PermitNumber;
                    ws.Cell(row, 3).Value = r.Date;
                    ws.Cell(row, 4).Value = r.BasketCount;
                    ws.Cell(row, 5).Value = r.ProductType;
                    ws.Cell(row, 6).Value = r.Grade;
                    ws.Cell(row, 7).Value = r.FarmName;
                    ws.Cell(row, 8).Value = r.VehiclePlate;
                    ws.Cell(row, 9).Value = r.Destination;
                    ws.Cell(row, 10).Value = r.SaleType;
                    ws.Cell(row, 11).Value = r.Source;
                    ws.Cell(row, 12).Value = r.SortingContractor;
                    ws.Cell(row, 13).Value = r.Description;
                    if (totalNet > 0) ws.Cell(row, 14).Value = totalNet;
                    ws.Cell(row, 15).Value = SortingMovement.ExitLabel(r.SaleType, r.MovementType);
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

            // برگه‌های قدیمی بدون دوره
            var legacy = list.Where(p => p.HarvestCycleId == null).ToList();
            if (legacy.Count > 0 || cycleId == 0)
                WriteSheet("بدون دوره", legacy);

            if (wb.Worksheets.Count == 0)
                WriteSheet("SortingPermits", new List<SortingExitPermit>());

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"SortingPermits-{DateTime.Now:yyyyMMddHHmm}.xlsx");
        }

        // ==========================================
        // 📤 ورود یکپارچه اکسل سورتینگ (GET)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Import()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!await _permissionService.CanUserImportAsync(userId!, "SortingExitPermits"))
            {
                TempData["Warning"] = "شما مجوز ورود اکسل سورتینگ را ندارید.";
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

            if (!await _permissionService.CanUserImportAsync(userId!, "SortingExitPermits"))
            {
                TempData["Warning"] = "شما مجوز ورود اکسل سورتینگ را ندارید.";
                return RedirectToAction(nameof(Index));
            }

            var cycles = await _db.HarvestCycles.AsNoTracking()
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.CycleNumber)
                .ToListAsync();

            using var wb = new XLWorkbook();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var cy in cycles)
            {
                var sheetName = (cy.Name ?? "").Trim();
                if (!IsValidSheetName(sheetName) || !usedNames.Add(sheetName)) continue;

                var ws = wb.Worksheets.Add(sheetName);
                ws.RightToLeft = true;

                for (int i = 0; i < ImportHeaders.Length; i++)
                {
                    var cell = ws.Cell(1, i + 1);
                    cell.Value = ImportHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                    ws.Column(i + 1).Width = 18;
                }

                // ستون‌های عددی عدد، بقیه متن (تا تاریخ شمسی و شماره‌ها تغییر نکنند)
                for (int col = 1; col <= ImportHeaders.Length; col++)
                {
                    bool numeric = col == 3 || col == 14 || col == 15;
                    ws.Column(col).Style.NumberFormat.Format = numeric ? "0" : "@";
                }

                ws.SheetView.FreezeRows(1);
            }

            if (wb.Worksheets.Count == 0)
            {
                TempData["Warning"] = "هیچ دوره‌ای با نام معتبر برای تب اکسل وجود ندارد. ابتدا دوره‌ها را تعریف کنید.";
                return RedirectToAction(nameof(Import));
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "SortingExitPermits-Template.xlsx");
        }

        // ==========================================
        // 📤 پردازش فایل اکسل یکپارچه (POST)
        //  - فقط .xlsx با ساختار مشخص (۱۶ ستون A تا P با سربرگ دقیق)
        //  - نام هر تب باید با نام یک دوره برداشت یکی باشد
        //  - هر تبِ معتبر در دوره‌ی خودش ثبت می‌شود؛ تبِ نامعتبر کلاً ثبت نمی‌شود
        // ==========================================
        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(100_000_000)]
        public async Task<IActionResult> Import(IFormFile? file)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!await _permissionService.CanUserImportAsync(userId!, "SortingExitPermits"))
            {
                TempData["Warning"] = "شما مجوز ورود اکسل سورتینگ را ندارید.";
                return RedirectToAction(nameof(Index));
            }

            await PrepareImportViewAsync();

            // ---------- بررسی فایل ----------
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "فایل انتخاب نشده است.");
                return View();
            }
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("", "فقط فایل با فرمت .xlsx مجاز است.");
                return View();
            }
            if (file.Length > 20 * 1024 * 1024)
            {
                ModelState.AddModelError("", "حجم فایل نباید بیشتر از ۲۰ مگابایت باشد.");
                return View();
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);

            // فایل xlsx یک zip است و باید با PK شروع شود
            var head = memoryStream.ToArray();
            if (head.Length < 4 || head[0] != 0x50 || head[1] != 0x4B)
            {
                ModelState.AddModelError("", "فایل یک xlsx معتبر نیست.");
                return View();
            }
            memoryStream.Position = 0;

            XLWorkbook wb;
            try
            {
                wb = new XLWorkbook(memoryStream);
            }
            catch
            {
                ModelState.AddModelError("", "فایل قابل خواندن نیست یا یک xlsx معتبر نیست.");
                return View();
            }

            var reports = new List<SheetImportReport>();
            var toAdd = new List<SortingExitPermit>();

            try
            {
                using (wb)
                {
                    var cycles = await _db.HarvestCycles.AsNoTracking().ToListAsync();
                    var cycleLookup = cycles
                        .GroupBy(x => Norm(x.Name))
                        .ToDictionary(g => g.Key, g => g.ToList());
                    var matchedCycleIds = new HashSet<int>();

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

                        // ---------- خواندن و اعتبارسنجی ساختار و نوع مقادیر ----------
                        var rows = ParseSheet(ws, rep);
                        if (rep.HasErrors) continue;

                        var merged = MergeGroups(rows, rep);
                        if (rep.HasErrors) continue;

                        // ---------- جلوگیری از ثبت برگه‌ی تکراری در همان دوره ----------
                        var existingKeys = (await _db.SortingExitPermits.AsNoTracking()
                                .Where(p => p.HarvestCycleId == cycle.Id)
                                .Select(p => new { p.PermitNumber, p.FarmName })
                                .ToListAsync())
                            .Select(k => Norm(k.PermitNumber) + "|" + Norm(k.FarmName))
                            .ToHashSet();

                        foreach (var (first, ticketRows) in merged)
                        {
                            if (existingKeys.Contains(Norm(first.PermitNumber) + "|" + Norm(first.FarmName)))
                            {
                                rep.Duplicates++;
                                continue;
                            }

                            var permit = new SortingExitPermit
                            {
                                PermitNumber = first.PermitNumber,
                                Date = first.Date,
                                BasketCount = first.BasketCount,
                                ProductType = first.ProductType,
                                Grade = first.Grade,
                                FarmName = first.FarmName,
                                VehiclePlate = first.VehiclePlate,
                                Destination = first.Destination,
                                SaleType = first.SaleType,
                                Source = first.Source,
                                SortingContractor = first.SortingContractor,
                                MovementType = SortingMovement.Resolve(first.SaleType, first.MovementType),
                                Description = first.Description,
                                HarvestCycleId = cycle.Id,
                                Status = "در انتظار توزین",
                                CreatedAt = DateTime.Now
                            };

                            foreach (var t in ticketRows)
                            {
                                permit.BasculTickets.Add(new SortingBasculTicket
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
                }

                // یک SaveChanges = یا همه‌ی تب‌های معتبر ثبت می‌شوند یا هیچ‌کدام
                if (toAdd.Count > 0)
                {
                    _db.SortingExitPermits.AddRange(toAdd);
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                foreach (var rep in reports.Where(r => !r.HasErrors && r.Permits > 0))
                {
                    rep.AddError("خطا در ذخیره‌سازی: " + ex.Message);
                    rep.Permits = 0;
                    rep.Tickets = 0;
                }
                ModelState.AddModelError("", "خطا در پردازش فایل: " + ex.Message);
            }

            ViewBag.Report = reports;

            if (!reports.Any(r => !r.HasErrors && (r.Permits > 0 || r.Duplicates > 0)))
                ModelState.AddModelError("", "هیچ تبی ثبت نشد. جزئیات هر تب را در گزارش زیر ببینید.");

            return View();
        }

        // ==========================================
        // 🆕 متدهای کمکی ورود اکسل
        // ==========================================

        // سربرگ‌های الزامی (ستون A تا P) به همین ترتیب
        private static readonly string[] ImportHeaders =
        {
            "شماره برگ خروج", "تاریخ", "تعداد سبد", "نوع محصول", "درجه", "نام مزرعه",
            "شماره پلاک", "مقصد", "نوع فروش", "مبدا", "پیمانکار سورت", "توضیحات",
            "شماره قبض باسکول", "وزن خالی (kg)", "وزن پر (kg)",
            "نوع خروج"
        };

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
        private static List<ImportRow> ParseSheet(IXLWorksheet ws, SheetImportReport rep)
        {
            var rows = new List<ImportRow>();
            int colCount = ImportHeaders.Length;
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            if (lastRow == 0)
            {
                rep.AddError("تب خالی است.");
                return rows;
            }

            // ---------- سربرگ ----------
            if (lastCol > colCount)
                rep.AddError($"ساختار فایل باید دقیقاً {colCount} ستون (A تا P) باشد؛ ستون اضافه پیدا شد.");

            for (int i = 0; i < colCount; i++)
            {
                var actual = ws.Cell(1, i + 1).GetString();
                // ستون P (نوع خروج) می‌تواند اصلاً در فایل نباشد (برای نقدی، میدان و هدیه خودکار پر می‌شود)
                if (i >= 15 && string.IsNullOrWhiteSpace(actual)) continue;
                if (Norm(actual) != Norm(ImportHeaders[i]))
                    rep.AddError($"عنوان ستون {XLHelper.GetColumnLetterFromNumber(i + 1)} باید «{ImportHeaders[i]}» باشد (در فایل: «{actual}»).");
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
                var item = new ImportRow { RowNumber = r };

                // کلیدهای گروه‌بندی در همه‌ی ردیف‌ها الزامی‌اند
                TryReadText(row.Cell(1), ImportHeaders[0], 15, r, rep, true, out var permitNumber);
                TryReadDate(row.Cell(2), r, rep, out var date);
                TryReadInt(row.Cell(3), ImportHeaders[2], r, rep, out var basket);
                TryReadText(row.Cell(4), ImportHeaders[3], 50, r, rep, false, out var productType);
                TryReadText(row.Cell(5), ImportHeaders[4], 50, r, rep, false, out var grade);
                TryReadText(row.Cell(6), ImportHeaders[5], 100, r, rep, true, out var farmName);
                TryReadText(row.Cell(7), ImportHeaders[6], 20, r, rep, false, out var plate);
                TryReadText(row.Cell(8), ImportHeaders[7], 100, r, rep, false, out var destination);
                TryReadText(row.Cell(9), ImportHeaders[8], 50, r, rep, false, out var saleType);
                TryReadText(row.Cell(10), ImportHeaders[9], 100, r, rep, false, out var source);
                TryReadText(row.Cell(11), ImportHeaders[10], 100, r, rep, false, out var contractor);
                TryReadText(row.Cell(12), ImportHeaders[11], 500, r, rep, false, out var description);
                TryReadText(row.Cell(13), ImportHeaders[12], 30, r, rep, false, out var ticketNo);
                TryReadInt(row.Cell(14), ImportHeaders[13], r, rep, out var emptyW);
                TryReadInt(row.Cell(15), ImportHeaders[14], r, rep, out var grossW);
                TryReadText(row.Cell(16), ImportHeaders[15], 40, r, rep, false, out var movementText);

                string? saleCanon = null;
                if (saleType.Length > 0)
                {
                    saleCanon = SortingMovement.CanonicalSaleType(saleType);
                    if (saleCanon == null)
                        rep.AddError($"ردیف {r}، ستون «{ImportHeaders[8]}»: فقط یکی از «{string.Join("»، «", SortingMovement.SaleTypes)}» مجاز است (مقدار فایل: «{saleType}»).");
                }

                string? movement = null;
                if (movementText.Length > 0)
                {
                    movement = SortingMovement.CanonicalExitType(movementText);
                    if (movement == null)
                        rep.AddError($"ردیف {r}، ستون «{ImportHeaders[15]}»: فقط یکی از «{string.Join("»، «", SortingMovement.AllExitTypes)}» مجاز است (مقدار فایل: «{movementText}»).");
                    else if (saleCanon == null)
                        rep.AddError($"ردیف {r}: برای ثبت «{ImportHeaders[15]}» ستون «{ImportHeaders[8]}» هم باید پر باشد.");
                    else if (!SortingMovement.AllowedExitTypes(saleCanon).Contains(movement))
                        rep.AddError($"ردیف {r}: «{movement}» برای نوع فروش «{saleCanon}» مجاز نیست؛ گزینه‌ی مجاز: «{string.Join("»، «", SortingMovement.AllowedExitTypes(saleCanon))}».");
                }
                else if (saleCanon == SortingMovement.Transfer)
                {
                    rep.AddError($"ردیف {r}: برای «{SortingMovement.Transfer}» ستون «{ImportHeaders[15]}» الزامی است.");
                }

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

                item.PermitNumber = permitNumber;
                item.Date = date;
                item.BasketCount = basket;
                item.ProductType = productType;
                item.Grade = grade;
                item.FarmName = farmName;
                item.VehiclePlate = plate;
                item.Destination = destination;
                item.SaleType = saleCanon ?? saleType;
                item.Source = source;
                item.SortingContractor = contractor;
                item.Description = description;
                item.MovementType = movement ?? "";
                item.TicketNo = ticketNo;
                item.EmptyWeight = emptyW ?? 0;
                item.GrossWeight = grossW ?? 0;
                rows.Add(item);
            }

            if (!rep.HasErrors && rows.Count == 0)
                rep.AddError("تب هیچ ردیف داده‌ای ندارد.");

            return rows;
        }

        // ردیف‌های هم‌کلید (شماره برگه + مزرعه) = یک برگه با چند قبض.
        // مقادیر خالی از ردیف‌های دیگر پر می‌شود؛ مقادیر ناهمسان خطا است.
        private static List<(ImportRow Head, List<ImportRow> TicketRows)> MergeGroups(List<ImportRow> rows, SheetImportReport rep)
        {
            var result = new List<(ImportRow, List<ImportRow>)>();

            foreach (var g in rows.GroupBy(x => Norm(x.PermitNumber) + "|" + Norm(x.FarmName)))
            {
                var list = g.ToList();
                var head = new ImportRow
                {
                    RowNumber = list[0].RowNumber,
                    PermitNumber = list[0].PermitNumber,
                    FarmName = list[0].FarmName
                };
                string label = $"برگه «{head.PermitNumber}» (مزرعه «{head.FarmName}»)";

                void MergeText(string title, Func<ImportRow, string> get, Action<ImportRow, string> set, bool required)
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

                MergeText(ImportHeaders[1], x => x.Date, (h, v) => h.Date = v, true);
                MergeText(ImportHeaders[3], x => x.ProductType, (h, v) => h.ProductType = v, true);
                MergeText(ImportHeaders[4], x => x.Grade, (h, v) => h.Grade = v, true);
                MergeText(ImportHeaders[6], x => x.VehiclePlate, (h, v) => h.VehiclePlate = v, false);
                MergeText(ImportHeaders[7], x => x.Destination, (h, v) => h.Destination = v, false);
                MergeText(ImportHeaders[8], x => x.SaleType, (h, v) => h.SaleType = v, false);
                MergeText(ImportHeaders[9], x => x.Source, (h, v) => h.Source = v, false);
                MergeText(ImportHeaders[10], x => x.SortingContractor, (h, v) => h.SortingContractor = v, false);
                MergeText(ImportHeaders[11], x => x.Description, (h, v) => h.Description = v, false);
                MergeText(ImportHeaders[15], x => x.MovementType, (h, v) => h.MovementType = v, false);

                var baskets = list.Where(x => x.BasketCount.HasValue).GroupBy(x => x.BasketCount!.Value).ToList();
                if (baskets.Count > 1)
                    rep.AddError($"{label}: مقدار ستون «{ImportHeaders[2]}» در ردیف‌های {string.Join("، ", baskets.Select(b => b.First().RowNumber))} با هم تفاوت دارد.");
                else if (baskets.Count == 1)
                    head.BasketCount = baskets[0].Key;

                result.Add((head, list.Where(x => x.TicketNo.Length > 0).ToList()));
            }

            return result;
        }

        // ==========================================
        // ✅ تأیید گروهی همه برگه‌های در انتظار تأیید
        // ==========================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveAll(string? fromDate = null, string? toDate = null, int? cycleId = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // 🆕 چک دسترسی (فقط کاربرانی که مدیر مجوز تأیید داده)
            if (!await _permissionService.CanApproveAsync(userId!, "SortingExitPermits"))
            {
                TempData["Warning"] = "شما دسترسی تأیید برگه‌ها را ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId = cycleId });
            }

            // 🆕 فیلتر بر اساس وضعیت، تاریخ و دوره
            var query = _db.SortingExitPermits
                .Where(p => p.Status == "توزین شده");

            if (cycleId.HasValue)
                query = FilterByCycle(query, cycleId.Value);

            if (!string.IsNullOrWhiteSpace(fromDate))
                query = query.Where(p => string.Compare(p.Date, fromDate) >= 0);
            if (!string.IsNullOrWhiteSpace(toDate))
                query = query.Where(p => string.Compare(p.Date, toDate) <= 0);

            var permits = await query.ToListAsync();

            if (!permits.Any())
            {
                TempData["Warning"] = "هیچ برگه‌ای در وضعیت «توزین شده» برای تأیید وجود ندارد.";
                return RedirectToAction(nameof(Index), new { cycleId = cycleId });
            }

            // 🆕 تأیید همه
            string approvalNote = $"✅ تأیید گروهی در {DateTime.Now:yyyy/MM/dd HH:mm} توسط {User.Identity?.Name}";

            foreach (var permit in permits)
            {
                permit.Status = "تأیید فروش";
                permit.Description = string.IsNullOrWhiteSpace(permit.Description)
                    ? approvalNote
                    : $"{permit.Description} | {approvalNote}";
            }

            await _db.SaveChangesAsync();

            TempData["Msg"] = $"✅ {permits.Count} برگه با موفقیت تأیید شد.";
            return RedirectToAction(nameof(Index), new { cycleId = cycleId });
        }

        // ==========================================
        // 🆕 متدهای کمکی دوره
        // ==========================================
        // cycleId = 0 یعنی برگه‌های بدون دوره (داده‌های قدیمی)
        private static IQueryable<SortingExitPermit> FilterByCycle(IQueryable<SortingExitPermit> q, int cycleId)
            => cycleId == 0
                ? q.Where(p => p.HarvestCycleId == null)
                : q.Where(p => p.HarvestCycleId == cycleId);

        private async Task LoadVarietiesForCycleAsync(int cycleId)
        {
            var cycle = await _db.HarvestCycles.Include(x => x.HarvestCycleCategories)
                .FirstOrDefaultAsync(x => x.Id == cycleId);
            var categoryIds = cycle?.HarvestCycleCategories.Select(x => x.CategoryId).ToArray()
                ?? Array.Empty<int>();
            if (categoryIds.Length == 0 && cycle?.CategoryId is int legacyCategoryId)
                categoryIds = new[] { legacyCategoryId };

            var query = _db.ProductVarieties.Include(v => v.Product).AsQueryable();
            if (categoryIds.Length > 0)
                query = query.Where(v => v.Product != null && v.Product.CategoryId.HasValue
                    && categoryIds.Contains(v.Product.CategoryId.Value));

            var values = await query.Select(v => new
            {
                Id = v.Id,
                DisplayName = v.Product!.Name + " - " + v.Name
            }).ToListAsync();
            ViewBag.VarietyList = new SelectList(values, "Id", "DisplayName");
        }

        private async Task LoadCyclesAsync(int? selectedId, bool includeNone)
        {
            var cycles = await _db.HarvestCycles.AsNoTracking()
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.CycleNumber)
                .ToListAsync();

            var items = cycles
                .Select(x => new SelectListItem(x.Name, x.Id.ToString(), x.Id == selectedId))
                .ToList();

            if (includeNone)
                items.Add(new SelectListItem("— بدون دوره (داده‌های قدیمی) —", "0", selectedId == 0));

            ViewBag.Cycles = items;
        }

        // کلاس داخلی برای خواندن ردیف‌های اکسل
        public class ImportRow
        {
            public int RowNumber { get; set; }
            public string PermitNumber { get; set; } = "";
            public string Date { get; set; } = "";
            public int? BasketCount { get; set; }
            public string ProductType { get; set; } = "";
            public string Grade { get; set; } = "";
            public string FarmName { get; set; } = "";
            public string VehiclePlate { get; set; } = "";
            public string Destination { get; set; } = "";
            public string SaleType { get; set; } = "";
            public string Source { get; set; } = "";
            public string SortingContractor { get; set; } = "";
            public string Description { get; set; } = "";
            public string TicketNo { get; set; } = "";
            public int EmptyWeight { get; set; }
            public int GrossWeight { get; set; }
            public string MovementType { get; set; } = "";
        }
    }
}