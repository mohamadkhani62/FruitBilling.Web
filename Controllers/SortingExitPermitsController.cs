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

            var normalizedPermitNumber = perms.GetValueOrDefault("PermitNumber")
                ? (model.PermitNumber ?? string.Empty).Trim()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedPermitNumber))
            {
                var existingPermitNumbers = await _db.SortingExitPermits.AsNoTracking()
                    .Where(p => p.HarvestCycleId == cycle.Id)
                    .Select(p => p.PermitNumber)
                    .ToListAsync();
                if (existingPermitNumbers.Any(number => Norm(number) == Norm(normalizedPermitNumber)))
                    ModelState.AddModelError(nameof(model.PermitNumber),
                        "این شماره مجوز تحویل قبلاً در همین دوره برداشت ثبت شده است.");
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
                PermitNumber = normalizedPermitNumber,
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

                // پس از ثبت قبض، تغییر رقم/درجه یا تعداد سبد ردیف باعث می‌شود
                // قبض‌های موجود به محصول اشتباه منتسب شوند یا جمع سبدها ناسازگار شود.
                foreach (var posted in submitted.Where(x => x.Id > 0))
                {
                    var existingItem = permit.Items.FirstOrDefault(x => x.Id == posted.Id);
                    if (existingItem == null || !existingItem.BasculTickets.Any())
                        continue;

                    if (posted.VarietyId != existingItem.VarietyId ||
                        !string.Equals(posted.Grade?.Trim(), existingItem.Grade?.Trim(), StringComparison.Ordinal))
                    {
                        ModelState.AddModelError("Items",
                            $"ردیف «{existingItem.Grade}» قبض باسکول دارد؛ تغییر رقم یا درجه آن تا زمان تعیین تکلیف قبض‌ها مجاز نیست.");
                    }

                    if (posted.BasketCount != existingItem.BasketCount)
                    {
                        ModelState.AddModelError("Items",
                            $"ردیف «{existingItem.Grade}» قبض باسکول دارد؛ برای جلوگیری از مغایرت تعداد سبد قبض‌ها، ابتدا قبض‌ها را اصلاح کنید.");
                    }
                }

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

            if (perms.GetValueOrDefault("PermitNumber"))
                permit.PermitNumber = (model.PermitNumber ?? string.Empty).Trim();
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
            if (permit.HarvestCycleId.HasValue && !string.IsNullOrWhiteSpace(permit.PermitNumber))
            {
                var existingPermitNumbers = await _db.SortingExitPermits.AsNoTracking()
                    .Where(p => p.Id != permit.Id && p.HarvestCycleId == permit.HarvestCycleId)
                    .Select(p => p.PermitNumber)
                    .ToListAsync();
                if (existingPermitNumbers.Any(number => Norm(number) == Norm(permit.PermitNumber)))
                    ModelState.AddModelError(nameof(model.PermitNumber),
                        "این شماره مجوز تحویل قبلاً در دوره برداشت انتخاب‌شده ثبت شده است.");
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
        private async Task<Dictionary<string, bool>?> GetTicketPermissionsAsync(string userId, SortingExitPermit permit)
        {
            if (string.IsNullOrEmpty(userId)) return null;

            // برگه‌ی تأییدشده فقط برای مدیر قابل تغییر است.
            if ((permit.Status == "تأیید فروش" || permit.Status == "تأیید شده توسط فروش") && !User.IsInRole("Manager"))
                return null;

            return await _permissionService.GetEditableFieldsAsync(userId, "SortingExitPermits", permit);
        }

        // افزودن یا حذف قبض قدیمی یک عملیات ساختاری است و به مجوز همه‌ی فیلدهای اجباری قبض نیاز دارد.
        private async Task<bool> CanManageLegacyTicketStructureAsync(string userId, SortingExitPermit permit)
        {
            var perms = await GetTicketPermissionsAsync(userId, permit);
            return perms != null &&
                perms.GetValueOrDefault("TicketNo_Sort") &&
                perms.GetValueOrDefault("EmptyWeight_Sort") &&
                perms.GetValueOrDefault("GrossWeight_Sort");
        }

        [HttpGet]
        public async Task<IActionResult> EditTicket(int id, int permitId, int? page)
        {
            var ticket = await _db.SortingBasculTickets.FindAsync(id);
            var permit = await _db.SortingExitPermits
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == permitId);
            if (ticket == null || permit == null || ticket.SortingExitPermitId != permitId) return NotFound();

            if (permit.Items.Any())
            {
                TempData["Warning"] = "این قبض باید در صفحه توزین ردیف رقم/درجه خودش ویرایش شود.";
                return RedirectToAction(nameof(Weigh), new { id = permitId });
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var ticketPermissions = await GetTicketPermissionsAsync(userId, permit);
            if (ticketPermissions == null ||
                !new[] { "TicketNo_Sort", "EmptyWeight_Sort", "GrossWeight_Sort" }
                    .Any(field => ticketPermissions.GetValueOrDefault(field)))
            {
                TempData["Warning"] = "شما دسترسی ویرایش فیلدهای قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            ViewBag.PermitId = permitId;
            ViewBag.ReturnPage = page;
            ViewBag.TicketPermissions = ticketPermissions;
            return View(ticket);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTicket(int id, SortingBasculTicket model, int permitId, int? page)
        {
            if (id != model.Id) return BadRequest();

            var ticket = await _db.SortingBasculTickets.FindAsync(id);
            var permit = await _db.SortingExitPermits
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == permitId);
            if (ticket == null || permit == null || ticket.SortingExitPermitId != permitId) return NotFound();

            if (permit.Items.Any())
            {
                TempData["Warning"] = "این قبض باید در صفحه توزین ردیف رقم/درجه خودش ویرایش شود.";
                return RedirectToAction(nameof(Weigh), new { id = permitId });
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var ticketPermissions = await GetTicketPermissionsAsync(userId, permit);
            if (ticketPermissions == null)
            {
                TempData["Warning"] = "شما دسترسی ویرایش قبض باسکول را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            bool canEditTicketNo = ticketPermissions.GetValueOrDefault("TicketNo_Sort");
            bool canEditEmptyWeight = ticketPermissions.GetValueOrDefault("EmptyWeight_Sort");
            bool canEditGrossWeight = ticketPermissions.GetValueOrDefault("GrossWeight_Sort");
            if (!canEditTicketNo && !canEditEmptyWeight && !canEditGrossWeight)
            {
                TempData["Warning"] = "شما دسترسی ویرایش هیچ‌یک از فیلدهای قبض را ندارید.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            string ticketNo = canEditTicketNo ? (model.TicketNo ?? string.Empty).Trim() : ticket.TicketNo ?? string.Empty;
            int emptyWeight = canEditEmptyWeight ? model.EmptyWeight : ticket.EmptyWeight;
            int grossWeight = canEditGrossWeight ? model.GrossWeight : ticket.GrossWeight;

            if (ModelState.IsValid && !string.IsNullOrWhiteSpace(ticketNo) &&
                ticketNo.Length <= 30 && emptyWeight >= 0 && grossWeight > emptyWeight)
            {
                ticket.TicketNo = ticketNo;
                ticket.EmptyWeight = emptyWeight;
                ticket.GrossWeight = grossWeight;
                ticket.NetWeight = grossWeight - emptyWeight;
                await _db.SaveChangesAsync();
                TempData["Msg"] = "فیلدهای مجاز قبض باسکول ویرایش شد.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            if (string.IsNullOrWhiteSpace(ticketNo) || ticketNo.Length > 30)
                ModelState.AddModelError(nameof(model.TicketNo), "شماره قبض الزامی و حداکثر ۳۰ کاراکتر است.");
            if (emptyWeight < 0 || grossWeight <= emptyWeight)
                ModelState.AddModelError("", "وزن خالی نباید منفی باشد و وزن پر باید از وزن خالی بیشتر باشد.");

            ViewBag.PermitId = permitId;
            ViewBag.ReturnPage = page;
            ViewBag.TicketPermissions = ticketPermissions;
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTicket(int id, int permitId, int? page)
        {
            var permit = await _db.SortingExitPermits
                .Include(p => p.BasculTickets)
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == permitId);
            if (permit == null) return NotFound();

            if (permit.Items.Any())
            {
                TempData["Warning"] = "قبض‌های این مجوز باید از صفحه توزین ردیف‌های رقم/درجه حذف یا اصلاح شوند.";
                return RedirectToAction(nameof(Weigh), new { id = permitId });
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId) || !await CanManageLegacyTicketStructureAsync(userId, permit))
            {
                TempData["Warning"] = "برای حذف قبض، مجوز ویرایش شماره قبض و وزن‌های خالی و پر لازم است.";
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
            var permit = await _db.SortingExitPermits
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == permitId);
            if (permit == null) return NotFound();

            // قبض‌های برگه‌های چندردیفی باید از مسیر توزین هر ردیف ثبت شوند.
            if (permit.Items.Any())
            {
                TempData["Warning"] = "این مجوز دارای ردیف‌های رقم/درجه است؛ قبض را از صفحه توزین همان ردیف ثبت کنید.";
                return RedirectToAction(nameof(Weigh), new { id = permitId });
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId) || !await CanManageLegacyTicketStructureAsync(userId, permit))
            {
                TempData["Warning"] = "برای افزودن قبض، مجوز ویرایش شماره قبض و وزن‌های خالی و پر لازم است.";
                return RedirectToAction(nameof(Edit), new { id = permitId, page = page });
            }

            if (!string.IsNullOrWhiteSpace(ticketNo) && ticketNo.Trim().Length <= 30 &&
                emptyWeight >= 0 && grossWeight > emptyWeight)
            {
                var newTicket = new SortingBasculTicket
                {
                    SortingExitPermitId = permitId,
                    TicketNo = ticketNo.Trim(),
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
                TempData["Warning"] = "شماره قبض باید وارد شود و وزن خالی نامنفی و وزن پر بزرگ‌تر از وزن خالی باشد.";
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

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var ticketPermissions = await GetTicketPermissionsAsync(userId, permit);
            if (ticketPermissions == null ||
                !new[] { "TicketNo_Sort", "BasketCount_Sort", "EmptyWeight_Sort", "GrossWeight_Sort" }
                    .Any(field => ticketPermissions.GetValueOrDefault(field)))
            {
                TempData["Warning"] = "شما اجازه مشاهده و ویرایش اطلاعات توزین این برگه را ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId = permit.HarvestCycleId ?? 0, focusId = permit.Id });
            }

            // کنترل همه‌ی تغییرات در POST نیز دوباره انجام می‌شود.
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
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            bool isApproved = permit.Status == "تأیید فروش" || permit.Status == "تأیید شده توسط فروش";
            if (isApproved && !User.IsInRole("Manager"))
            {
                TempData["Warning"] = "این برگه تأیید شده است و فقط مدیر می‌تواند توزین آن را تغییر دهد.";
                return RedirectToAction(nameof(Index), new { cycleId = permit.HarvestCycleId ?? 0, focusId = permit.Id });
            }

            var perms = await _permissionService.GetEditableFieldsAsync(userId, "SortingExitPermits", permit);
            bool canEditTicketNo = perms.GetValueOrDefault("TicketNo_Sort");
            bool canEditBasketCount = perms.GetValueOrDefault("BasketCount_Sort");
            bool canEditEmptyWeight = perms.GetValueOrDefault("EmptyWeight_Sort");
            bool canEditGrossWeight = perms.GetValueOrDefault("GrossWeight_Sort");
            bool canEditAnyTicketField = canEditTicketNo || canEditBasketCount || canEditEmptyWeight || canEditGrossWeight;

            if (!canEditAnyTicketField)
            {
                TempData["Warning"] = "شما برای هیچ‌یک از فیلدهای قبض باسکول مجوز ویرایش ندارید.";
                return RedirectToAction(nameof(Index), new { cycleId = permit.HarvestCycleId ?? 0, focusId = permit.Id });
            }

            bool canCreateTicket = canEditTicketNo && canEditEmptyWeight && canEditGrossWeight;
            bool canManageTicketRows = canCreateTicket && canEditBasketCount;

            model.Items ??= new List<SortingExitPermitItem>();
            var submittedItems = model.Items.ToList();
            var validItemIds = permit.Items.Select(x => x.Id).ToHashSet();

            if (validItemIds.Count == 0)
            {
                TempData["Warning"] = "این برگ سورتینگ ردیف رقم/درجه ندارد و قابل توزین نیست.";
                return RedirectToAction(nameof(Weigh), new { id });
            }

            if (submittedItems.GroupBy(x => x.Id).Any(g => g.Count() > 1) ||
                submittedItems.Any(x => !validItemIds.Contains(x.Id)) ||
                !submittedItems.Select(x => x.Id).ToHashSet().SetEquals(validItemIds))
            {
                TempData["Warning"] = "ردیف‌های ارسالی با ردیف‌های همین برگ سورتینگ مطابقت ندارند. هیچ تغییری ذخیره نشد.";
                return RedirectToAction(nameof(Weigh), new { id });
            }

            var ticketIdsSeen = new HashSet<int>();
            var prepared = new List<(SortingExitPermitItem Item,
                List<(int Id, string TicketNo, int BasketCount, int EmptyWeight, int GrossWeight)> Tickets)>();

            foreach (var item in permit.Items)
            {
                var submittedItem = submittedItems.Single(x => x.Id == item.Id);
                var rawTickets = submittedItem.BasculTickets ?? new List<SortingBasculTicket>();
                var activeTickets = rawTickets
                    .Where(t => t.Id > 0 || !string.IsNullOrWhiteSpace(t.TicketNo) ||
                                t.GrossWeight > 0 || t.EmptyWeight > 0 || (t.BasketCount ?? 0) > 0)
                    .ToList();

                var existingTicketsById = item.BasculTickets.ToDictionary(t => t.Id);
                var postedExistingIds = activeTickets.Where(t => t.Id > 0).Select(t => t.Id).ToHashSet();

                if (activeTickets.Where(t => t.Id > 0).GroupBy(t => t.Id).Any(g => g.Count() > 1) ||
                    activeTickets.Any(t => t.Id > 0 &&
                        (!existingTicketsById.ContainsKey(t.Id) || !ticketIdsSeen.Add(t.Id))))
                {
                    TempData["Warning"] = "یکی از قبض‌ها متعلق به این ردیف نیست یا شناسه آن تکراری است. هیچ تغییری ذخیره نشد.";
                    return RedirectToAction(nameof(Weigh), new { id });
                }

                bool hasDeletedTickets = item.BasculTickets.Any(t => !postedExistingIds.Contains(t.Id));
                bool hasNewTickets = activeTickets.Any(t => t.Id == 0);
                if ((hasDeletedTickets || hasNewTickets && activeTickets.Count > 1) && !canManageTicketRows)
                {
                    TempData["Warning"] = "برای افزودن قبض اضافی یا حذف قبض، مجوز ویرایش شماره قبض، تعداد سبد و وزن‌ها لازم است. هیچ تغییری ذخیره نشد.";
                    return RedirectToAction(nameof(Weigh), new { id });
                }

                if (hasNewTickets && !canCreateTicket)
                {
                    TempData["Warning"] = "برای افزودن قبض جدید باید مجوز ویرایش شماره قبض، وزن خالی و وزن پر را داشته باشید.";
                    return RedirectToAction(nameof(Weigh), new { id });
                }

                if (activeTickets.Count == 0)
                {
                    TempData["Warning"] = $"برای درجه {item.Grade} حداقل یک قبض معتبر لازم است. هیچ تغییری ذخیره نشد.";
                    return RedirectToAction(nameof(Weigh), new { id });
                }

                var preparedTickets = new List<(int Id, string TicketNo, int BasketCount, int EmptyWeight, int GrossWeight)>();
                foreach (var posted in activeTickets)
                {
                    existingTicketsById.TryGetValue(posted.Id, out var current);

                    if (current != null)
                    {
                        if (!canEditTicketNo && !string.Equals(posted.TicketNo?.Trim(), current.TicketNo?.Trim(), StringComparison.Ordinal))
                        {
                            TempData["Warning"] = "شما مجوز ویرایش شماره قبض را ندارید. هیچ تغییری ذخیره نشد.";
                            return RedirectToAction(nameof(Weigh), new { id });
                        }
                        if (!canEditEmptyWeight && posted.EmptyWeight != current.EmptyWeight)
                        {
                            TempData["Warning"] = "شما مجوز ویرایش وزن خالی را ندارید. هیچ تغییری ذخیره نشد.";
                            return RedirectToAction(nameof(Weigh), new { id });
                        }
                        if (!canEditGrossWeight && posted.GrossWeight != current.GrossWeight)
                        {
                            TempData["Warning"] = "شما مجوز ویرایش وزن پر را ندارید. هیچ تغییری ذخیره نشد.";
                            return RedirectToAction(nameof(Weigh), new { id });
                        }
                        if (!canEditBasketCount && activeTickets.Count > 1 &&
                            posted.BasketCount != current.BasketCount)
                        {
                            TempData["Warning"] = "شما مجوز ویرایش تعداد سبد قبض را ندارید. هیچ تغییری ذخیره نشد.";
                            return RedirectToAction(nameof(Weigh), new { id });
                        }
                    }
                    else if (posted.Id != 0)
                    {
                        TempData["Warning"] = "شناسه قبض معتبر نیست. هیچ تغییری ذخیره نشد.";
                        return RedirectToAction(nameof(Weigh), new { id });
                    }

                    string ticketNo = current != null && !canEditTicketNo
                        ? current.TicketNo ?? string.Empty
                        : (posted.TicketNo ?? string.Empty).Trim();
                    int emptyWeight = current != null && !canEditEmptyWeight
                        ? current.EmptyWeight : posted.EmptyWeight;
                    int grossWeight = current != null && !canEditGrossWeight
                        ? current.GrossWeight : posted.GrossWeight;
                    int basketCount = activeTickets.Count == 1
                        ? item.BasketCount
                        : current != null && !canEditBasketCount
                            ? current.BasketCount ?? 0
                            : posted.BasketCount ?? 0;

                    if (string.IsNullOrWhiteSpace(ticketNo) || ticketNo.Length > 30 ||
                        emptyWeight < 0 || grossWeight <= emptyWeight || basketCount <= 0)
                    {
                        TempData["Warning"] = $"برای درجه {item.Grade} شماره قبض، تعداد سبد مثبت و وزن پر بزرگ‌تر از وزن خالی الزامی است. هیچ تغییری ذخیره نشد.";
                        return RedirectToAction(nameof(Weigh), new { id });
                    }

                    preparedTickets.Add((posted.Id, ticketNo, basketCount, emptyWeight, grossWeight));
                }

                if (preparedTickets.Sum(t => t.BasketCount) != item.BasketCount)
                {
                    TempData["Warning"] = $"مجموع تعداد سبد قبض‌های درجه {item.Grade} باید دقیقاً برابر {item.BasketCount} باشد. هیچ تغییری ذخیره نشد.";
                    return RedirectToAction(nameof(Weigh), new { id });
                }

                prepared.Add((item, preparedTickets));
            }

            var totalNetWeight = 0;
            foreach (var entry in prepared)
            {
                var item = entry.Item;
                var retainedIds = entry.Tickets.Where(t => t.Id > 0).Select(t => t.Id).ToHashSet();
                _db.SortingBasculTickets.RemoveRange(item.BasculTickets.Where(t => !retainedIds.Contains(t.Id)));

                foreach (var posted in entry.Tickets)
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
                    ticket.TicketNo = posted.TicketNo;
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
            var permit = await _db.SortingExitPermits.Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .Include(p => p.BasculTickets).FirstOrDefaultAsync(p => p.Id == id);
            if (permit == null) return NotFound();

            // ۴. بررسی وضعیت و اعتبارسنجی مجدد اطلاعات باسکول پیش از تأیید
            if (permit.Status == "توزین شده")
            {
                var validationError = ValidatePermitForSalesApproval(permit);
                if (validationError != null)
                {
                    TempData["Warning"] = $"این برگه قابل تأیید نیست: {validationError}";
                    return RedirectToAction(nameof(Index), new { cycleId = permit.HarvestCycleId ?? 0, focusId = permit.Id });
                }

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
            var permit = await _db.SortingExitPermits
                .Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .Include(p => p.BasculTickets)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (permit == null) return NotFound();
            return View(permit);
        }

        [HttpGet]
        public async Task<IActionResult> Export(int? cycleId)
        {
            var query = _db.SortingExitPermits.Include(p => p.Items).ThenInclude(x => x.Variety).ThenInclude(v => v.Product)
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets).Include(p => p.BasculTickets).AsQueryable();
            if (cycleId.HasValue) query = FilterByCycle(query, cycleId.Value);
            var permits = await query.OrderBy(p => p.Date).ThenBy(p => p.Id).ToListAsync();
            var cycles = await _db.HarvestCycles.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.CycleNumber).ToListAsync();
            using var wb = new XLWorkbook();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void WriteSheet(string title, List<SortingExitPermit> records)
            {
                var ws = wb.AddWorksheet(SafeSheetName(title, usedNames));
                string[] headers = ImportHeaders;
                for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
                int row = 2, sequence = 1;
                void WriteRow(SortingExitPermit p, SortingExitPermitItem? item, SortingBasculTicket? ticket)
                {
                    ws.Cell(row, 1).Value = sequence++; ws.Cell(row, 2).Value = p.PermitNumber; ws.Cell(row, 3).Value = p.Date; ws.Cell(row, 4).Value = p.FarmName;
                    ws.Cell(row, 5).Value = item?.Variety?.Product?.Name ?? p.ProductType; ws.Cell(row, 6).Value = item?.Variety?.Name;
                    ws.Cell(row, 7).Value = item?.Grade ?? p.Grade; ws.Cell(row, 8).Value = item?.BasketCount ?? p.BasketCount;
                    ws.Cell(row, 9).Value = p.Destination; ws.Cell(row, 10).Value = p.SaleType; ws.Cell(row, 11).Value = p.Source;
                    ws.Cell(row, 12).Value = p.SortingContractor; ws.Cell(row, 13).Value = p.VehiclePlate; ws.Cell(row, 14).Value = ticket?.TicketNo;
                    if (ticket?.BasketCount is int count) ws.Cell(row, 15).Value = count;
                    if (ticket != null) { ws.Cell(row, 16).Value = ticket.EmptyWeight; ws.Cell(row, 17).Value = ticket.GrossWeight; ws.Cell(row, 18).Value = ticket.NetWeight; }
                    ws.Cell(row, 19).Value = SortingMovement.ExitLabel(p.SaleType, p.MovementType); ws.Cell(row, 20).Value = p.Description; row++;
                }
                foreach (var p in records)
                {
                    if (p.Items?.Count > 0)
                        foreach (var item in p.Items.OrderBy(x => x.Id))
                        {
                            var tickets = item.BasculTickets?.OrderBy(t => t.Id).ToList() ?? new List<SortingBasculTicket>();
                            if (tickets.Count == 0) WriteRow(p, item, null); else foreach (var t in tickets) WriteRow(p, item, t);
                        }
                    else
                    {
                        var tickets = p.BasculTickets?.OrderBy(t => t.Id).ToList() ?? new List<SortingBasculTicket>();
                        if (tickets.Count == 0) WriteRow(p, null, null); else foreach (var t in tickets) WriteRow(p, null, t);
                    }
                }
                ws.RightToLeft = true; ws.SheetView.FreezeRows(1); ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true; ws.Columns().AdjustToContents();
            }
            foreach (var cycle in cycles) { var records = permits.Where(p => p.HarvestCycleId == cycle.Id).ToList(); if (records.Count == 0 && cycleId != cycle.Id) continue; WriteSheet(cycle.Name, records); }
            var legacy = permits.Where(p => p.HarvestCycleId == null).ToList(); if (legacy.Count > 0 || cycleId == 0) WriteSheet("بدون دوره", legacy);
            if (wb.Worksheets.Count == 0) WriteSheet("خروج سورتینگ", new List<SortingExitPermit>());
            using var ms = new MemoryStream(); wb.SaveAs(ms);
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

                // ستون‌های عددی؛ تاریخ و شماره‌ها به‌صورت متن حفظ می‌شوند.
                for (int col = 1; col <= ImportHeaders.Length; col++)
                {
                    bool numeric = col == 1 || col == 8 || col == 15 ||
                        col == 16 || col == 17 || col == 18;
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
        //  - قالب جدید ۲۰ ستونی همسان با خروجی و قالب قدیمی ۱۶ ستونی برای سازگاری
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
                    var cycles = await _db.HarvestCycles
                        .Include(c => c.HarvestCycleCategories)
                        .AsNoTracking()
                        .ToListAsync();
                    var cycleLookup = cycles
                        .GroupBy(x => Norm(x.Name))
                        .ToDictionary(g => g.Key, g => g.ToList());
                    var matchedCycleIds = new HashSet<int>();

                    foreach (var ws in wb.Worksheets)
                    {
                        var rep = new SheetImportReport { SheetName = ws.Name };
                        reports.Add(rep);

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

                        var rows = ParseSheet(ws, rep);
                        if (rep.HasErrors) continue;

                        var merged = MergeGroups(rows, rep);
                        if (rep.HasErrors) continue;

                        var seenPermitNumbers = (await _db.SortingExitPermits.AsNoTracking()
                                .Where(p => p.HarvestCycleId == cycle.Id)
                                .Select(p => p.PermitNumber)
                                .ToListAsync())
                            .Select(Norm)
                            .Where(number => !string.IsNullOrWhiteSpace(number))
                            .ToHashSet();

                        var categoryIds = cycle.HarvestCycleCategories.Select(x => x.CategoryId).ToArray();
                        if (categoryIds.Length == 0 && cycle.CategoryId.HasValue)
                            categoryIds = new[] { cycle.CategoryId.Value };

                        var allowedVarietiesQuery = _db.ProductVarieties
                            .AsNoTracking()
                            .Include(v => v.Product)
                            .AsQueryable();

                        if (categoryIds.Length > 0)
                        {
                            allowedVarietiesQuery = allowedVarietiesQuery.Where(v =>
                                v.Product != null && v.Product.CategoryId.HasValue &&
                                categoryIds.Contains(v.Product.CategoryId.Value));
                        }

                        var allowedVarieties = await allowedVarietiesQuery.ToListAsync();
                        var sheetPermits = new List<SortingExitPermit>();
                        int sheetTicketCount = 0;

                        foreach (var (first, permitRows) in merged)
                        {
                            var normalizedNumber = Norm(first.PermitNumber);
                            if (string.IsNullOrWhiteSpace(normalizedNumber))
                            {
                                rep.AddError("شماره مجوز تحویل الزامی است؛ این برگه ثبت نشد.");
                                continue;
                            }

                            if (!seenPermitNumbers.Add(normalizedNumber))
                            {
                                rep.Duplicates++;
                                continue;
                            }

                            int errorsBeforePermit = rep.Errors.Count + rep.HiddenErrorCount;
                            var permit = new SortingExitPermit
                            {
                                PermitNumber = first.PermitNumber,
                                Date = first.Date,
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

                            // قالب ۱۶ ستونی قدیمی و خروجی‌های قدیمیِ بدون رقم:
                            // برای این داده‌ها رقم حدس زده نمی‌شود و قبض‌ها در سطح قدیمی برگه می‌مانند.
                            if (first.IsLegacyFormat)
                            {
                                permit.ProductType = first.ProductType;
                                permit.Grade = first.Grade;
                                permit.BasketCount = first.BasketCount;

                                foreach (var ticketRow in permitRows.Where(x => !string.IsNullOrWhiteSpace(x.TicketNo)))
                                {
                                    if (ticketRow.TicketBasketCount.HasValue && ticketRow.TicketBasketCount.Value <= 0)
                                    {
                                        rep.AddError($"ردیف {ticketRow.RowNumber}: تعداد سبد قبض باید بیشتر از صفر باشد.");
                                        continue;
                                    }

                                    permit.BasculTickets.Add(new SortingBasculTicket
                                    {
                                        SortingExitPermit = permit,
                                        TicketNo = ticketRow.TicketNo,
                                        BasketCount = ticketRow.TicketBasketCount,
                                        EmptyWeight = ticketRow.EmptyWeight,
                                        GrossWeight = ticketRow.GrossWeight,
                                        NetWeight = ticketRow.GrossWeight - ticketRow.EmptyWeight,
                                        CreatedAt = DateTime.Now
                                    });
                                }

                                if (permit.BasculTickets.Any())
                                    permit.Status = "توزین شده";
                            }
                            else
                            {
                                // کلید ترکیبی به‌صورت tuple نگهداری می‌شود تا وجود کاراکتر «|»
                                // در نام محصول/رقم/درجه باعث ادغام اشتباه دو ردیف مستقل نشود.
                                var itemGroups = permitRows.GroupBy(x => (
                                    Product: Norm(x.ProductType),
                                    Variety: Norm(x.VarietyName),
                                    Grade: Norm(x.Grade))).ToList();

                                foreach (var itemGroup in itemGroups)
                                {
                                    var itemRows = itemGroup.ToList();
                                    var firstItemRow = itemRows[0];

                                    if (string.IsNullOrWhiteSpace(firstItemRow.ProductType) ||
                                        string.IsNullOrWhiteSpace(firstItemRow.VarietyName) ||
                                        string.IsNullOrWhiteSpace(firstItemRow.Grade))
                                    {
                                        rep.AddError($"برگه «{first.PermitNumber}»، ردیف {firstItemRow.RowNumber}: محصول، رقم و درجه برای هر ردیف الزامی است.");
                                        continue;
                                    }

                                    var basketValues = itemRows.Where(x => x.BasketCount.HasValue)
                                        .Select(x => x.BasketCount!.Value).Distinct().ToList();
                                    if (basketValues.Count != 1 || basketValues[0] <= 0)
                                    {
                                        rep.AddError($"برگه «{first.PermitNumber}»، ردیف‌های {string.Join("، ", itemRows.Select(x => x.RowNumber))}: تعداد سبد ردیف باید یک مقدار یکسان و بیشتر از صفر باشد.");
                                        continue;
                                    }

                                    var itemVarieties = allowedVarieties.Where(v =>
                                        v.Product != null &&
                                        Norm(v.Product.Name) == Norm(firstItemRow.ProductType) &&
                                        Norm(v.Name) == Norm(firstItemRow.VarietyName)).ToList();

                                    if (itemVarieties.Count != 1)
                                    {
                                        rep.AddError($"برگه «{first.PermitNumber}»، ردیف {firstItemRow.RowNumber}: ترکیب محصول «{firstItemRow.ProductType}» و رقم «{firstItemRow.VarietyName}» در گونه‌های این دوره پیدا نشد یا تکراری است.");
                                        continue;
                                    }

                                    var ticketRows = itemRows.Where(x => !string.IsNullOrWhiteSpace(x.TicketNo)).ToList();
                                    var ticketBasketCounts = new List<int>();
                                    if (ticketRows.Count == 1 && !ticketRows[0].TicketBasketCount.HasValue)
                                    {
                                        ticketBasketCounts.Add(basketValues[0]);
                                    }
                                    else if (ticketRows.Any(x => !x.TicketBasketCount.HasValue || x.TicketBasketCount.Value <= 0))
                                    {
                                        rep.AddError($"برگه «{first.PermitNumber}»، ردیف‌های {string.Join("، ", ticketRows.Select(x => x.RowNumber))}: تعداد سبد هر قبض الزامی و باید بیشتر از صفر باشد.");
                                        continue;
                                    }
                                    else if (ticketRows.Count > 0)
                                    {
                                        ticketBasketCounts.AddRange(ticketRows.Select(x => x.TicketBasketCount!.Value));
                                    }

                                    if (ticketRows.Count > 0 && ticketBasketCounts.Sum() != basketValues[0])
                                    {
                                        rep.AddError($"برگه «{first.PermitNumber}»، محصول «{firstItemRow.ProductType}» و رقم «{firstItemRow.VarietyName}»، درجه «{firstItemRow.Grade}»: مجموع سبد قبض‌ها ({ticketBasketCounts.Sum()}) باید دقیقاً برابر سبد ردیف ({basketValues[0]}) باشد.");
                                        continue;
                                    }

                                    var variety = itemVarieties[0];
                                    var detail = new SortingExitPermitItem
                                    {
                                        SortingExitPermit = permit,
                                        VarietyId = variety.Id,
                                        Grade = firstItemRow.Grade,
                                        BasketCount = basketValues[0]
                                    };

                                    // ارقام از query بدون Tracking آمده‌اند؛ فقط FKها ذخیره می‌شوند تا EF آن‌ها را دوباره Insert نکند.
                                    if (permit.Items.Count == 0)
                                    {
                                        permit.VarietyId = variety.Id;
                                        permit.ProductId = variety.ProductId;
                                        permit.ProductType = variety.Product?.Name;
                                        permit.Grade = firstItemRow.Grade;
                                    }

                                    for (int ticketIndex = 0; ticketIndex < ticketRows.Count; ticketIndex++)
                                    {
                                        var ticketRow = ticketRows[ticketIndex];
                                        detail.BasculTickets.Add(new SortingBasculTicket
                                        {
                                            SortingExitPermit = permit,
                                            SortingExitPermitItem = detail,
                                            TicketNo = ticketRow.TicketNo,
                                            BasketCount = ticketBasketCounts[ticketIndex],
                                            EmptyWeight = ticketRow.EmptyWeight,
                                            GrossWeight = ticketRow.GrossWeight,
                                            NetWeight = ticketRow.GrossWeight - ticketRow.EmptyWeight,
                                            CreatedAt = DateTime.Now
                                        });
                                    }

                                    permit.Items.Add(detail);
                                }

                                if (permit.Items.Count > 0)
                                {
                                    permit.BasketCount = permit.Items.Sum(x => x.BasketCount);
                                    permit.Status = permit.Items.All(x => x.BasculTickets.Any())
                                        ? "توزین شده"
                                        : "در انتظار توزین";
                                }
                            }

                            if (rep.Errors.Count + rep.HiddenErrorCount != errorsBeforePermit)
                                continue;

                            sheetTicketCount += first.IsLegacyFormat
                                ? permit.BasculTickets.Count
                                : permit.Items.Sum(x => x.BasculTickets.Count);
                            sheetPermits.Add(permit);
                        }

                        // تب به‌صورت یک واحد ثبت می‌شود؛ اگر یک برگه‌ی آن خطا داشته باشد،
                        // هیچ‌یک از برگه‌های همان تب ذخیره نمی‌شوند.
                        if (rep.HasErrors)
                        {
                            rep.Permits = 0;
                            rep.Tickets = 0;
                            rep.AddNote("به‌دلیل وجود خطا در این تب، هیچ‌یک از برگه‌های آن ذخیره نشد.");
                            continue;
                        }

                        toAdd.AddRange(sheetPermits);
                        rep.Permits = sheetPermits.Count;
                        rep.Tickets = sheetTicketCount;
                    }
                }

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

        // قالب جدید همسان با خروجی اکسل؛ یک سطر برای هر قبض و یک ردیف رقم/درجه
        private static readonly string[] ImportHeaders =
        {
            "ردیف", "شماره مجوز تحویل", "تاریخ", "مزرعه", "محصول", "رقم", "درجه",
            "تعداد سبد ردیف", "مقصد", "نوع فروش", "مبدا", "پیمانکار سورت", "پلاک",
            "شماره قبض", "تعداد سبد قبض", "وزن خالی (kg)", "وزن پر (kg)", "وزن خالص (kg)",
            "نوع خروج", "توضیحات"
        };

        // قالب ۱۶ ستونی قبلی فقط برای حفظ ورود داده‌های قدیمی پذیرفته می‌شود.
        private static readonly string[] LegacyImportHeaders =
        {
            "شماره مجوز تحویل", "تاریخ", "تعداد سبد", "نوع محصول", "درجه", "نام مزرعه",
            "شماره پلاک", "مقصد", "نوع فروش", "مبدا", "پیمانکار سورت", "توضیحات",
            "شماره قبض باسکول", "وزن خالی (kg)", "وزن پر (kg)", "نوع خروج"
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

        // خواندن قالب جدید ۲۰ ستونی یا قالب قدیمی ۱۶ ستونی
        private static List<ImportRow> ParseSheet(IXLWorksheet ws, SheetImportReport rep)
        {
            var rows = new List<ImportRow>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            if (lastRow == 0)
            {
                rep.AddError("تب خالی است.");
                return rows;
            }

            bool HeadersMatch(string[] expected)
            {
                for (int i = 0; i < expected.Length; i++)
                {
                    var actual = ws.Cell(1, i + 1).GetString();
                    if (expected == LegacyImportHeaders && i == 15 && string.IsNullOrWhiteSpace(actual))
                        continue;
                    if (Norm(actual) != Norm(expected[i]))
                        return false;
                }
                return true;
            }

            bool isNewFormat = HeadersMatch(ImportHeaders);
            bool isLegacyFormat = !isNewFormat && HeadersMatch(LegacyImportHeaders);
            if (!isNewFormat && !isLegacyFormat)
            {
                rep.AddError("سربرگ فایل با قالب جدید ۲۰ ستونی خروجی اکسل یا قالب قدیمی ۱۶ ستونی مطابقت ندارد.");
                return rows;
            }

            int colCount = isNewFormat ? ImportHeaders.Length : LegacyImportHeaders.Length;
            if (lastCol > colCount)
            {
                rep.AddError($"ساختار این تب باید دقیقاً {colCount} ستون داشته باشد؛ ستون اضافه پیدا شد.");
                return rows;
            }

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

            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                bool allEmpty = true;
                for (int col = 1; col <= colCount; col++)
                {
                    var cell = row.Cell(col);
                    if (cell.DataType != XLDataType.Blank &&
                        !(cell.DataType == XLDataType.Text && string.IsNullOrWhiteSpace(cell.GetString())))
                    {
                        allEmpty = false;
                        break;
                    }
                }
                if (allEmpty) continue;

                int errorsBefore = rep.Errors.Count + rep.HiddenErrorCount;
                var item = new ImportRow { RowNumber = r };
                int? sequence = null;
                int? basket;
                int? ticketBasket = null;
                int? emptyW;
                int? grossW;
                int? netW = null;
                string permitNumber, date, farmName, productType, varietyName = "", grade,
                    plate, destination, saleType, source, contractor, description,
                    ticketNo, movementText;

                if (isNewFormat)
                {
                    TryReadInt(row.Cell(1), ImportHeaders[0], r, rep, out sequence);
                    TryReadText(row.Cell(2), ImportHeaders[1], 15, r, rep, true, out permitNumber);
                    TryReadDate(row.Cell(3), r, rep, out date);
                    TryReadText(row.Cell(4), ImportHeaders[3], 100, r, rep, true, out farmName);
                    TryReadText(row.Cell(5), ImportHeaders[4], 100, r, rep, false, out productType);
                    TryReadText(row.Cell(6), ImportHeaders[5], 100, r, rep, false, out varietyName);
                    TryReadText(row.Cell(7), ImportHeaders[6], 50, r, rep, false, out grade);
                    TryReadInt(row.Cell(8), ImportHeaders[7], r, rep, out basket);
                    TryReadText(row.Cell(9), ImportHeaders[8], 100, r, rep, false, out destination);
                    TryReadText(row.Cell(10), ImportHeaders[9], 50, r, rep, false, out saleType);
                    TryReadText(row.Cell(11), ImportHeaders[10], 100, r, rep, false, out source);
                    TryReadText(row.Cell(12), ImportHeaders[11], 100, r, rep, false, out contractor);
                    TryReadText(row.Cell(13), ImportHeaders[12], 20, r, rep, false, out plate);
                    TryReadText(row.Cell(14), ImportHeaders[13], 30, r, rep, false, out ticketNo);
                    TryReadInt(row.Cell(15), ImportHeaders[14], r, rep, out ticketBasket);
                    TryReadInt(row.Cell(16), ImportHeaders[15], r, rep, out emptyW);
                    TryReadInt(row.Cell(17), ImportHeaders[16], r, rep, out grossW);
                    TryReadInt(row.Cell(18), ImportHeaders[17], r, rep, out netW);
                    TryReadText(row.Cell(19), ImportHeaders[18], 40, r, rep, false, out movementText);
                    TryReadText(row.Cell(20), ImportHeaders[19], 500, r, rep, false, out description);
                }
                else
                {
                    TryReadText(row.Cell(1), LegacyImportHeaders[0], 15, r, rep, true, out permitNumber);
                    TryReadDate(row.Cell(2), r, rep, out date);
                    TryReadInt(row.Cell(3), LegacyImportHeaders[2], r, rep, out basket);
                    TryReadText(row.Cell(4), LegacyImportHeaders[3], 50, r, rep, false, out productType);
                    TryReadText(row.Cell(5), LegacyImportHeaders[4], 50, r, rep, false, out grade);
                    TryReadText(row.Cell(6), LegacyImportHeaders[5], 100, r, rep, true, out farmName);
                    TryReadText(row.Cell(7), LegacyImportHeaders[6], 20, r, rep, false, out plate);
                    TryReadText(row.Cell(8), LegacyImportHeaders[7], 100, r, rep, false, out destination);
                    TryReadText(row.Cell(9), LegacyImportHeaders[8], 50, r, rep, false, out saleType);
                    TryReadText(row.Cell(10), LegacyImportHeaders[9], 100, r, rep, false, out source);
                    TryReadText(row.Cell(11), LegacyImportHeaders[10], 100, r, rep, false, out contractor);
                    TryReadText(row.Cell(12), LegacyImportHeaders[11], 500, r, rep, false, out description);
                    TryReadText(row.Cell(13), LegacyImportHeaders[12], 30, r, rep, false, out ticketNo);
                    TryReadInt(row.Cell(14), LegacyImportHeaders[13], r, rep, out emptyW);
                    TryReadInt(row.Cell(15), LegacyImportHeaders[14], r, rep, out grossW);
                    TryReadText(row.Cell(16), LegacyImportHeaders[15], 40, r, rep, false, out movementText);
                }

                string? saleCanon = null;
                if (saleType.Length > 0)
                {
                    saleCanon = SortingMovement.CanonicalSaleType(saleType);
                    if (saleCanon == null)
                        rep.AddError($"ردیف {r}، ستون «نوع فروش»: فقط یکی از «{string.Join("»، «", SortingMovement.SaleTypes)}» مجاز است (مقدار فایل: «{saleType}»).");
                }

                string? movement = null;
                if (movementText.Length > 0)
                {
                    movement = SortingMovement.CanonicalExitType(movementText);
                    if (movement == null)
                        rep.AddError($"ردیف {r}، ستون «نوع خروج»: مقدار واردشده معتبر نیست.");
                    else if (saleCanon == null)
                        rep.AddError($"ردیف {r}: برای ثبت نوع خروج، ستون «نوع فروش» هم باید پر باشد.");
                    else if (!SortingMovement.AllowedExitTypes(saleCanon).Contains(movement))
                        rep.AddError($"ردیف {r}: «{movement}» برای نوع فروش «{saleCanon}» مجاز نیست؛ گزینه‌ی مجاز: «{string.Join("»، «", SortingMovement.AllowedExitTypes(saleCanon))}».");
                }
                else if (saleCanon == SortingMovement.Transfer)
                {
                    rep.AddError($"ردیف {r}: برای «{SortingMovement.Transfer}» ستون «نوع خروج» الزامی است.");
                }

                if (basket.HasValue && basket.Value <= 0 &&
                    rep.Errors.Count + rep.HiddenErrorCount == errorsBefore)
                {
                    rep.AddError($"ردیف {r}: تعداد سبد برگه باید بیشتر از صفر باشد.");
                }

                bool anyTicket = ticketNo.Length > 0 || ticketBasket.HasValue ||
                    emptyW.HasValue || grossW.HasValue || netW.HasValue;
                if (anyTicket && rep.Errors.Count + rep.HiddenErrorCount == errorsBefore)
                {
                    if (ticketNo.Length == 0 || !emptyW.HasValue || !grossW.HasValue)
                    {
                        rep.AddError($"ردیف {r}: برای ثبت قبض باید شماره قبض، وزن خالی و وزن پر هر سه وارد شوند.");
                    }
                    else if (emptyW.Value < 0)
                    {
                        rep.AddError($"ردیف {r}: وزن خالی نمی‌تواند منفی باشد.");
                    }
                    else if (grossW.Value <= emptyW.Value)
                    {
                        rep.AddError($"ردیف {r}: وزن پر ({grossW.Value}) باید بزرگ‌تر از وزن خالی ({emptyW.Value}) باشد.");
                    }
                    else if (ticketBasket.HasValue && ticketBasket.Value <= 0)
                    {
                        rep.AddError($"ردیف {r}: تعداد سبد قبض باید بیشتر از صفر باشد.");
                    }
                    else if (netW.HasValue && netW.Value != grossW.Value - emptyW.Value)
                    {
                        rep.AddError($"ردیف {r}: وزن خالص باید برابر تفاضل وزن پر و وزن خالی باشد.");
                    }
                }

                if (rep.Errors.Count + rep.HiddenErrorCount != errorsBefore)
                    continue;

                item.SequenceNumber = sequence;
                // قالب ۲۰ ستونی فقط وقتی به‌صورت قدیمی تفسیر می‌شود که کل تب
                // خروجی قدیمیِ بدون ستون رقم باشد؛ خالی بودن رقم در یک ردیف منفرد
                // نباید آن ردیف را بی‌صدا به قبض‌های سطح برگه تبدیل کند.
                item.IsLegacyFormat = isLegacyFormat;
                item.PermitNumber = permitNumber;
                item.Date = date;
                item.FarmName = farmName;
                item.ProductType = productType;
                item.VarietyName = varietyName;
                item.Grade = grade;
                item.BasketCount = basket;
                item.VehiclePlate = plate;
                item.Destination = destination;
                item.SaleType = saleCanon ?? saleType;
                item.Source = source;
                item.SortingContractor = contractor;
                item.Description = description;
                item.MovementType = movement ?? "";
                item.TicketNo = ticketNo;
                item.TicketBasketCount = ticketBasket;
                item.EmptyWeight = emptyW ?? 0;
                item.GrossWeight = grossW ?? 0;
                item.NetWeight = netW;
                rows.Add(item);
            }

            if (isNewFormat && rows.Count > 0)
            {
                bool allRowsAreOldNoVarietyExport = rows.All(x =>
                    string.IsNullOrWhiteSpace(x.VarietyName) && x.SequenceNumber.HasValue);

                if (allRowsAreOldNoVarietyExport)
                {
                    // سازگاری با خروجی‌های ۲۰ ستونی قدیمی که هنوز ستون رقم نداشتند.
                    foreach (var row in rows)
                        row.IsLegacyFormat = true;
                }
                else
                {
                    foreach (var row in rows.Where(x => string.IsNullOrWhiteSpace(x.VarietyName)))
                        rep.AddError($"ردیف {row.RowNumber}: در قالب جدید ۲۰ ستونی، ستون «رقم» الزامی است؛ برای داده‌های قدیمی از قالب ۱۶ ستونی استفاده کنید.");
                }
            }

            if (!rep.HasErrors && rows.Count == 0)
                rep.AddError("تب هیچ ردیف داده‌ای ندارد.");

            return rows;
        }

        // هر شماره مجوز تحویل در یک دوره فقط یک برگه است؛ ردیف‌ها می‌توانند رقم/درجه‌های متفاوت داشته باشند.
        private static List<(ImportRow Head, List<ImportRow> Rows)> MergeGroups(List<ImportRow> rows, SheetImportReport rep)
        {
            var result = new List<(ImportRow, List<ImportRow>)>();

            foreach (var group in rows.GroupBy(x => Norm(x.PermitNumber)))
            {
                var list = group.ToList();
                var head = new ImportRow
                {
                    RowNumber = list[0].RowNumber,
                    PermitNumber = list[0].PermitNumber,
                    IsLegacyFormat = list[0].IsLegacyFormat
                };
                string label = $"برگه «{head.PermitNumber}»";

                if (list.Any(x => x.IsLegacyFormat != head.IsLegacyFormat))
                    rep.AddError($"{label}: قالب داده‌های این شماره مجوز با هم مخلوط شده است.");

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

                MergeText("تاریخ", x => x.Date, (h, v) => h.Date = v, true);
                MergeText("نام مزرعه", x => x.FarmName, (h, v) => h.FarmName = v, true);
                MergeText("شماره پلاک", x => x.VehiclePlate, (h, v) => h.VehiclePlate = v, false);
                MergeText("مقصد", x => x.Destination, (h, v) => h.Destination = v, false);
                MergeText("نوع فروش", x => x.SaleType, (h, v) => h.SaleType = v, false);
                MergeText("مبدا", x => x.Source, (h, v) => h.Source = v, false);
                MergeText("پیمانکار سورت", x => x.SortingContractor, (h, v) => h.SortingContractor = v, false);
                MergeText("توضیحات", x => x.Description, (h, v) => h.Description = v, false);
                MergeText("نوع خروج", x => x.MovementType, (h, v) => h.MovementType = v, false);

                if (head.IsLegacyFormat)
                {
                    MergeText("محصول", x => x.ProductType, (h, v) => h.ProductType = v, true);
                    MergeText("درجه", x => x.Grade, (h, v) => h.Grade = v, true);

                    var baskets = list.Where(x => x.BasketCount.HasValue)
                        .Select(x => x.BasketCount!.Value).Distinct().ToList();
                    if (baskets.Count > 1)
                        rep.AddError($"{label}: تعداد سبد در ردیف‌های یک برگه‌ی قدیمی یکسان نیست.");
                    else if (baskets.Count == 1)
                        head.BasketCount = baskets[0];
                }

                result.Add((head, list));
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

            var permits = await query
                .Include(p => p.Items).ThenInclude(x => x.BasculTickets)
                .Include(p => p.BasculTickets)
                .ToListAsync();

            if (!permits.Any())
            {
                TempData["Warning"] = "هیچ برگه‌ای در وضعیت «توزین شده» برای تأیید وجود ندارد.";
                return RedirectToAction(nameof(Index), new { cycleId = cycleId });
            }

            // فقط برگه‌هایی تأیید می‌شوند که اعتبار قبض‌ها و مجموع سبدهای آن‌ها تأیید شود.
            var validPermits = new List<SortingExitPermit>();
            var invalidPermits = new List<(int Id, string Reason)>();
            foreach (var permit in permits)
            {
                var validationError = ValidatePermitForSalesApproval(permit);
                if (validationError == null)
                    validPermits.Add(permit);
                else
                    invalidPermits.Add((permit.Id, validationError));
            }

            if (validPermits.Count == 0)
            {
                var firstErrors = string.Join("؛ ", invalidPermits.Take(3)
                    .Select(x => $"برگه {x.Id}: {x.Reason}"));
                TempData["Warning"] = $"هیچ برگه‌ای تأیید نشد. {invalidPermits.Count} برگه نقص دارد. {firstErrors}";
                return RedirectToAction(nameof(Index), new { cycleId = cycleId });
            }

            string approvalNote = $"✅ تأیید گروهی در {DateTime.Now:yyyy/MM/dd HH:mm} توسط {User.Identity?.Name}";
            foreach (var permit in validPermits)
            {
                permit.Status = "تأیید فروش";
                permit.Description = string.IsNullOrWhiteSpace(permit.Description)
                    ? approvalNote
                    : $"{permit.Description} | {approvalNote}";
            }

            await _db.SaveChangesAsync();

            var resultMessage = $"✅ {validPermits.Count} برگه تأیید شد.";
            if (invalidPermits.Count > 0)
            {
                var firstErrors = string.Join("؛ ", invalidPermits.Take(3)
                    .Select(x => $"برگه {x.Id}: {x.Reason}"));
                resultMessage += $" {invalidPermits.Count} برگه به دلیل نقص تأیید نشد: {firstErrors}";
            }
            TempData["Msg"] = resultMessage;
            return RedirectToAction(nameof(Index), new { cycleId = cycleId });
        }

        // اعتبارسنجی مشترک برگه‌های جدید و قدیمی پیش از تأیید فروش
        private static string? ValidatePermitForSalesApproval(SortingExitPermit permit)
        {
            if (permit.BasketCount is null or <= 0)
                return "تعداد کل سبد برگه باید بیشتر از صفر باشد.";

            if (permit.Items != null && permit.Items.Count > 0)
            {
                if (permit.Items.Any(item => item.BasketCount <= 0))
                    return "تعداد سبد یکی از ردیف‌های رقم/درجه معتبر نیست.";

                var itemBasketTotal = permit.Items.Sum(item => item.BasketCount);
                if (itemBasketTotal != permit.BasketCount.Value)
                    return $"مجموع سبد ردیف‌ها ({itemBasketTotal}) با تعداد سبد برگه ({permit.BasketCount}) برابر نیست.";

                foreach (var item in permit.Items)
                {
                    var tickets = item.BasculTickets ?? new List<SortingBasculTicket>();
                    if (tickets.Count == 0)
                        return $"برای ردیف «{item.Grade}» هیچ قبض باسکولی ثبت نشده است.";

                    if (tickets.Any(ticket => string.IsNullOrWhiteSpace(ticket.TicketNo)
                        || ticket.TicketNo.Trim().Length > 30))
                        return $"شماره قبض در ردیف «{item.Grade}» خالی یا نامعتبر است.";

                    if (tickets.Any(ticket => ticket.BasketCount is null or <= 0))
                        return $"تعداد سبد یکی از قبض‌های ردیف «{item.Grade}» معتبر نیست.";

                    if (tickets.Any(ticket => ticket.EmptyWeight < 0 || ticket.GrossWeight <= ticket.EmptyWeight))
                        return $"وزن خالی/پر یکی از قبض‌های ردیف «{item.Grade}» نامعتبر است.";

                    if (tickets.Any(ticket => ticket.NetWeight != ticket.GrossWeight - ticket.EmptyWeight))
                        return $"وزن خالص یکی از قبض‌های ردیف «{item.Grade}» با اختلاف وزن پر و خالی مطابقت ندارد.";

                    var ticketBasketTotal = tickets.Sum(ticket => ticket.BasketCount!.Value);
                    if (ticketBasketTotal != item.BasketCount)
                        return $"مجموع سبد قبض‌های ردیف «{item.Grade}» ({ticketBasketTotal}) با تعداد سبد ردیف ({item.BasketCount}) برابر نیست.";
                }

                return null;
            }

            // سازگاری با برگه‌های قدیمی که تعداد سبد هر قبض در آن‌ها ذخیره نشده است.
            var legacyTickets = permit.BasculTickets ?? new List<SortingBasculTicket>();
            if (legacyTickets.Count == 0)
                return "هیچ قبض باسکولی برای این برگه ثبت نشده است.";

            if (legacyTickets.Any(ticket => string.IsNullOrWhiteSpace(ticket.TicketNo)
                || ticket.TicketNo.Trim().Length > 30))
                return "شماره یکی از قبض‌های باسکول خالی یا نامعتبر است.";

            if (legacyTickets.Any(ticket => ticket.EmptyWeight < 0 || ticket.GrossWeight <= ticket.EmptyWeight))
                return "وزن خالی/پر یکی از قبض‌های باسکول نامعتبر است.";

            if (legacyTickets.Any(ticket => ticket.NetWeight != ticket.GrossWeight - ticket.EmptyWeight))
                return "وزن خالص یکی از قبض‌ها با اختلاف وزن پر و خالی مطابقت ندارد.";

            var hasTicketBasketCounts = legacyTickets.Any(ticket => ticket.BasketCount.HasValue);
            if (hasTicketBasketCounts)
            {
                if (legacyTickets.Any(ticket => ticket.BasketCount is null or <= 0))
                    return "تعداد سبد یکی از قبض‌های قدیمی ناقص است.";
                var legacyBasketTotal = legacyTickets.Sum(ticket => ticket.BasketCount!.Value);
                if (legacyBasketTotal != permit.BasketCount.Value)
                    return $"مجموع سبد قبض‌ها ({legacyBasketTotal}) با تعداد سبد برگه ({permit.BasketCount}) برابر نیست.";
            }

            return null;
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
            public int? SequenceNumber { get; set; }
            public bool IsLegacyFormat { get; set; }
            public string PermitNumber { get; set; } = "";
            public string Date { get; set; } = "";
            public int? BasketCount { get; set; }
            public string ProductType { get; set; } = "";
            public string VarietyName { get; set; } = "";
            public string Grade { get; set; } = "";
            public string FarmName { get; set; } = "";
            public string VehiclePlate { get; set; } = "";
            public string Destination { get; set; } = "";
            public string SaleType { get; set; } = "";
            public string Source { get; set; } = "";
            public string SortingContractor { get; set; } = "";
            public string Description { get; set; } = "";
            public string TicketNo { get; set; } = "";
            public int? TicketBasketCount { get; set; }
            public int EmptyWeight { get; set; }
            public int GrossWeight { get; set; }
            public int? NetWeight { get; set; }
            public string MovementType { get; set; } = "";
        }
    }
}