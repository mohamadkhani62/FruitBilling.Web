using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using static FruitBilling.Web.Services.ExcelImportHelper;

namespace FruitBilling.Web.Controllers
{
    [Authorize(Roles = "Manager,SalesManager")]
    public class DashboardController : Controller
    {
        private const string PendingStatus = "در انتظار توزین";
        private const string WeighedStatus = "توزین شده";
        private const string Unknown = "نامشخص";
        private const string ArrivalConfirmed = "تأیید شده در مقصد";
        private const string SaleConfirmed = "تأیید فروش";

        private static readonly string[] MonthNames =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        };

        private static readonly PersianCalendar Pc = new PersianCalendar();
        private static readonly string HallKey = Norm("سورتینگ");
        private static readonly string WasteKey = Norm("ضایعات");

        private static readonly (string Variant, string Title)[] Variants =
        {
            ("daily", "روزانه"), ("weekly", "هفتگی"), ("monthly", "ماهانه")
        };

        private readonly AppDbContext _db;

        public DashboardController(AppDbContext db)
        {
            _db = db;
        }

        // ------------------------------------------------------------
        // ردیف‌های خوانده‌شده از دیتابیس
        // ------------------------------------------------------------
        private sealed class FRow
        {
            public int Id { get; set; }
            public string Number { get; set; } = "";
            public string? Date { get; set; }
            public int? FarmId { get; set; }
            public string? Farm { get; set; }
            public int? PlotId { get; set; }
            public string? Plot { get; set; }
            public string? Category { get; set; }
            public int? ProductId { get; set; }
            public string? Product { get; set; }
            public int? VarietyId { get; set; }
            public string? Variety { get; set; }
            public int? CycleId { get; set; }
            public string? Method { get; set; }
            public int? Baskets { get; set; }
            public string? Dest { get; set; }
            public string? Plate { get; set; }
            public string? Driver { get; set; }
            public string? Contractor { get; set; }
            public string Status { get; set; } = "";
            public int Kg { get; set; }
            public bool HasDate { get; set; }
            public DateTime G { get; set; }
            public string D { get; set; } = "";
        }

        private sealed class SRow
        {
            public int Id { get; set; }
            public string Number { get; set; } = "";
            public string? Date { get; set; }
            public string? Product { get; set; }
            public string? Grade { get; set; }
            public string? Farm { get; set; }
            public string? Plate { get; set; }
            public string? Dest { get; set; }
            public string? SaleType { get; set; }
            public string? Source { get; set; }
            public string? Movement { get; set; }
            public string? Contractor { get; set; }
            public int? Baskets { get; set; }
            public int? CycleId { get; set; }
            public string Status { get; set; } = "";
            public int Kg { get; set; }
            public bool HasDate { get; set; }
            public DateTime G { get; set; }
            public string D { get; set; } = "";
        }

        private sealed class EstRow
        {
            public int CycleId { get; set; }
            public int VarietyId { get; set; }
            public int? PlotId { get; set; }
            public decimal Kg { get; set; }
            public string Product { get; set; } = "";
            public string Variety { get; set; } = "";
            public string? Plot { get; set; }
            public string? Farm { get; set; }
        }

        // نام‌های هم‌ارز (ی/ک عربی، فاصله، ...) را به یک برچسب واحد تبدیل می‌کند
        private sealed class Canon
        {
            private readonly Dictionary<string, string> _map = new();

            public string Of(string? s)
            {
                var t = Nz(s);
                var k = Norm(t);
                if (!_map.TryGetValue(k, out var v))
                {
                    v = t;
                    _map[k] = v;
                }
                return v;
            }
        }

        // ------------------------------------------------------------
        // Index
        // ------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> Index(int? cycleId, string? fromDate, string? toDate, string? product, string? farm)
        {
            var cycles = await _db.HarvestCycles.AsNoTracking()
                .OrderByDescending(c => c.Year)
                .ThenByDescending(c => c.CycleNumber)
                .ToListAsync();
            var cycleById = cycles.ToDictionary(c => c.Id);

            // پیش‌فرض: آخرین دوره؛ 0 = همه‌ی دوره‌ها
            int cid = cycleId ?? (cycles.FirstOrDefault()?.Id ?? 0);

            string? fromN = TryParseFa(fromDate, out _, out var fn) ? fn : null;
            string? toN = TryParseFa(toDate, out _, out var tn) ? tn : null;
            string? prodKey = string.IsNullOrWhiteSpace(product) ? null : Norm(product);
            string? farmKey = string.IsNullOrWhiteSpace(farm) ? null : Norm(farm);

            // ---------- بارگذاری ----------
            var farmAll = await _db.FruitExitPermits.AsNoTracking().Select(p => new FRow
            {
                Id = p.Id,
                Number = p.PermitNumber,
                Date = p.Date,
                FarmId = p.FarmId,
                Farm = p.Farm != null ? p.Farm.Name : null,
                PlotId = p.PlotId,
                Plot = p.Plot != null ? p.Plot.Name : null,
                Category = p.Product != null && p.Product.Category != null ? p.Product.Category.Name : null,
                ProductId = p.ProductId,
                Product = p.Product != null ? p.Product.Name : p.ProductType,
                VarietyId = p.VarietyId,
                Variety = p.Variety != null ? p.Variety.Name : null,
                CycleId = p.HarvestCycleId,
                Method = p.HarvestMethod,
                Baskets = p.BasketCount,
                Dest = p.Destination,
                Plate = p.VehiclePlate,
                Driver = p.DriverName,
                Contractor = p.ContractorName,
                Status = p.Status,
                Kg = p.BasculTickets.Sum(t => (int?)t.NetWeight) ?? 0
            }).ToListAsync();

            var sortAll = await _db.SortingExitPermits.AsNoTracking().Select(p => new SRow
            {
                Id = p.Id,
                Number = p.PermitNumber,
                Date = p.Date,
                Product = p.Product != null ? p.Product.Name : p.ProductType,
                Grade = p.Grade,
                Farm = p.FarmName,
                Plate = p.VehiclePlate,
                Dest = p.Destination,
                SaleType = p.SaleType,
                Source = p.Source,
                Movement = p.MovementType,
                Contractor = p.SortingContractor,
                Baskets = p.BasketCount,
                CycleId = p.HarvestCycleId,
                Status = p.Status,
                Kg = p.BasculTickets.Sum(t => (int?)t.NetWeight) ?? 0
            }).ToListAsync();

            int invalidDates = 0;
            foreach (var r in farmAll)
            {
                r.HasDate = TryParseFa(r.Date, out var g, out var d);
                r.G = g;
                r.D = d;
                if (!r.HasDate) invalidDates++;
            }
            foreach (var r in sortAll)
            {
                r.HasDate = TryParseFa(r.Date, out var g, out var d);
                r.G = g;
                r.D = d;
                if (!r.HasDate) invalidDates++;
            }

            // ---------- فیلترها ----------
            bool InDate(bool has, string d) =>
                (fromN == null && toN == null)
                || (has
                    && (fromN == null || string.CompareOrdinal(d, fromN) >= 0)
                    && (toN == null || string.CompareOrdinal(d, toN) <= 0));
            bool ProdOk(string? p) => prodKey == null || Norm(p) == prodKey;
            bool FarmOk(string? f) => farmKey == null || Norm(f) == farmKey;

            var farm1 = farmAll.Where(r => (cid == 0 || r.CycleId == cid) && InDate(r.HasDate, r.D) && ProdOk(r.Product) && FarmOk(r.Farm)).ToList();
            var sort1 = sortAll.Where(r => (cid == 0 || r.CycleId == cid) && InDate(r.HasDate, r.D) && ProdOk(r.Product) && FarmOk(r.Farm)).ToList();
            var wf = farm1.Where(r => r.Kg > 0).ToList();   // برگ‌های مزرعه‌ی توزین‌شده
            var ws = sort1.Where(r => r.Kg > 0).ToList();   // برگ‌های سورتینگ توزین‌شده (همه‌ی حرکت‌ها)
            var wsSale = ws.Where(r => !SortingMovement.IsInternal(r.SaleType, r.Movement)).ToList();     // فروش نقدی، میدان و هدیه
            var wsInternal = ws.Where(r => SortingMovement.IsInternal(r.SaleType, r.Movement)).ToList();  // جابجایی داخلی

            // همه‌ی دوره‌ها (فقط فیلتر محصول و مزرعه) برای مقایسه‌ی دوره‌ها
            var allW = farmAll.Where(r => r.Kg > 0 && ProdOk(r.Product) && FarmOk(r.Farm)).ToList();
            // بدون فیلتر تاریخ، برای مقایسه با برآورد
            var cmp = farmAll.Where(r => r.Kg > 0 && (cid == 0 || r.CycleId == cid) && ProdOk(r.Product) && FarmOk(r.Farm)).ToList();

            // ---------- کاتالوگ‌ها ----------
            var plotArea = (await _db.Plots.AsNoTracking().Select(p => new { p.Id, p.AreaHectare }).ToListAsync())
                .ToDictionary(x => x.Id, x => x.AreaHectare);
            var plotTrees = (await _db.TreeRows.AsNoTracking()
                    .GroupBy(t => t.PlotId)
                    .Select(g => new { PlotId = g.Key, N = g.Sum(t => t.TreeCount) })
                    .ToListAsync())
                .ToDictionary(x => x.PlotId, x => x.N);

            // ---------- برآورد ----------
            List<EstRow> est = new();
            string? estNotice = null;
            try
            {
                est = await _db.YieldEstimates.AsNoTracking()
                    .Where(e => cid == 0 || e.HarvestCycleId == cid)
                    .Select(e => new EstRow
                    {
                        CycleId = e.HarvestCycleId,
                        VarietyId = e.VarietyId,
                        PlotId = e.PlotId,
                        Kg = e.EstimatedKg,
                        Product = e.Variety!.Product!.Name,
                        Variety = e.Variety.Name,
                        Plot = e.Plot != null ? e.Plot.Name : null,
                        Farm = e.Plot != null && e.Plot.Farm != null ? e.Plot.Farm.Name : null
                    }).ToListAsync();
                est = est.Where(e => ProdOk(e.Product)).ToList();
            }
            catch (Exception)
            {
                estNotice = "جدول برآورد هنوز در دیتابیس ساخته نشده است. مایگریشن را اجرا کنید (Add-Migration و سپس Update-Database).";
            }

            // ---------- مدل صفحه ----------
            string cycleName = "همه‌ی دوره‌ها";
            if (cid != 0)
                cycleName = cycleById.TryGetValue(cid, out var curCycle) ? curCycle.Name : "";

            var model = new DashboardPageModel
            {
                CycleId = cid,
                CycleName = cycleName,
                FromDate = fromDate,
                ToDate = toDate,
                Product = product,
                Farm = farm,
                InvalidDateRows = invalidDates
            };

            model.Cycles.Add(new SelectListItem("همه‌ی دوره‌ها", "0", cid == 0));
            foreach (var c in cycles)
                model.Cycles.Add(new SelectListItem(c.Name, c.Id.ToString(), c.Id == cid));

            var prodCanon = new Canon();
            model.ProductNames = farmAll.Select(r => r.Product).Concat(sortAll.Select(r => r.Product))
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => prodCanon.Of(s)).Distinct().OrderBy(s => s).ToList();
            var farmCanon = new Canon();
            model.FarmNames = farmAll.Select(r => r.Farm).Concat(sortAll.Select(r => r.Farm))
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => farmCanon.Of(s)).Distinct().OrderBy(s => s).ToList();

            // ---------- شاخص‌های کلیدی ----------
            decimal farmKg = wf.Sum(r => (decimal)r.Kg);
            decimal farmUnconfirmed = wf.Where(r => r.Status != ArrivalConfirmed).Sum(r => (decimal)r.Kg);
            decimal hallIn = wf.Where(r => IsHall(r.Dest)).Sum(r => (decimal)r.Kg);
            decimal salesKg = wsSale.Sum(r => (decimal)r.Kg);
            decimal salesUnconfirmed = wsSale.Where(r => r.Status != SaleConfirmed).Sum(r => (decimal)r.Kg);
            decimal internalKg = wsInternal.Sum(r => (decimal)r.Kg);
            decimal wasteKg = wsSale.Where(r => IsWaste(r.Grade)).Sum(r => (decimal)r.Kg);
            decimal shrink = hallIn - salesKg;
            int pendingCount = farm1.Count(r => r.Status == PendingStatus && r.Kg == 0) + sort1.Count(r => r.Status == PendingStatus && r.Kg == 0);

            model.Kpis.Add(new DashKpi { Title = "برداشت کل", Value = Tn(farmKg) + " تن", Sub = F(wf.Count) + " برگه" + (farmUnconfirmed > 0 ? " — " + Tn(farmUnconfirmed) + " تن تأییدنشده" : ""), Css = "success" });
            model.Kpis.Add(new DashKpi { Title = "ورود به سالن سورت", Value = Tn(hallIn) + " تن", Sub = Pct(hallIn, farmKg) + " از برداشت", Css = "primary" });
            model.Kpis.Add(new DashKpi { Title = "خروج از سورتینگ (فروش)", Value = Tn(salesKg) + " تن", Sub = F(wsSale.Count) + " برگه" + (salesUnconfirmed > 0 ? " — " + Tn(salesUnconfirmed) + " تن تأییدنشده" : ""), Css = "info" });
            model.Kpis.Add(new DashKpi { Title = "جابجایی داخلی", Value = Tn(internalKg) + " تن", Sub = "در خروجی کل حساب نشده", Css = "secondary" });
            model.Kpis.Add(new DashKpi { Title = "کسری سالن", Value = Tn(shrink) + " تن", Sub = Pct(shrink, hallIn) + " از ورودی", Css = shrink > 0 ? "warning" : "secondary" });
            model.Kpis.Add(new DashKpi { Title = "ضایعات", Value = Tn(wasteKg) + " تن", Sub = Pct(wasteKg, salesKg) + " از خروجی", Css = "danger" });
            model.Kpis.Add(new DashKpi { Title = "منتظر توزین", Value = F(pendingCount), Sub = "برگه", Css = pendingCount > 0 ? "warning" : "secondary" });

            // ---------- تب‌ها ----------
            model.Tabs.Add(BuildProduction(wf, allW, cycles, plotArea, plotTrees));
            model.Tabs.Add(BuildEstimates(est, cmp, estNotice, cid));
            model.Tabs.Add(BuildHarvest(wf));
            model.Tabs.Add(BuildSorting(ws));
            model.Tabs.Add(BuildSales(wsSale));
            model.Tabs.Add(BuildTrucks(wf, ws));
            model.Tabs.Add(BuildContractors(wf, ws));
            model.Tabs.Add(BuildHall(wf, wsSale, wsInternal));
            model.Tabs.Add(BuildAlerts(farm1, sort1));

            return View(model);
        }

        // ------------------------------------------------------------
        // کمکی‌های عمومی
        // ------------------------------------------------------------

        private static string Nz(string? s) => string.IsNullOrWhiteSpace(s) ? Unknown : s.Trim();
        private static string F(decimal v, int dec = 0) => v.ToString("N" + dec, CultureInfo.InvariantCulture);
        private static string Tn(decimal kg) => F(kg / 1000m, 2);
        private static decimal TnD(decimal kg) => Math.Round(kg / 1000m, 2);
        private static string Pct(decimal part, decimal total) => total <= 0 ? "—" : F(part * 100m / total, 1) + "%";
        private static bool IsHall(string? dest) => !string.IsNullOrWhiteSpace(dest) && Norm(dest).Contains(HallKey);
        private static bool IsWaste(string? grade) => !string.IsNullOrWhiteSpace(grade) && Norm(grade).Contains(WasteKey);

        private static bool TryParseFa(string? s, out DateTime g, out string norm)
        {
            g = default;
            norm = "";
            if (string.IsNullOrWhiteSpace(s)) return false;

            var parts = ToLatinDigits(s.Trim()).Split('/');
            if (parts.Length != 3
                || !int.TryParse(parts[0], out var y)
                || !int.TryParse(parts[1], out var m)
                || !int.TryParse(parts[2], out var d))
                return false;

            try
            {
                g = Pc.ToDateTime(y, m, d, 0, 0, 0, 0);
            }
            catch
            {
                return false;
            }

            norm = $"{y:0000}/{m:00}/{d:00}";
            return true;
        }

        private static (string Sort, string Label) Bucket(string variant, DateTime g, string d)
        {
            switch (variant)
            {
                case "weekly":
                    var start = g.AddDays(-(((int)g.DayOfWeek + 1) % 7));   // شروع هفته: شنبه
                    var weekStart = $"{Pc.GetYear(start):0000}/{Pc.GetMonth(start):00}/{Pc.GetDayOfMonth(start):00}";
                    return (weekStart, "هفته‌ی " + weekStart);
                case "monthly":
                    var year = d.Substring(0, 4);
                    var month = int.Parse(d.Substring(5, 2));
                    return (d.Substring(0, 7), MonthNames[month - 1] + " " + year);
                default:
                    return (d, d);
            }
        }

        private static decimal Median(List<decimal> v)
        {
            v.Sort();
            int n = v.Count;
            return n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2m;
        }

        private static DashChart MkChart(string id, string title, string type, List<string> labels, List<DashDataset> sets, bool stacked = false, string? unit = "تن")
            => new DashChart { Id = id, Title = title, Type = type, Labels = labels, Datasets = sets, Stacked = stacked, Unit = unit };

        private static DashDataset Ds(string label, IEnumerable<decimal> data, string? type = null)
            => new DashDataset { Label = label, Data = data.ToList(), Type = type };

        private static DashBlock Blk(DashChart c) => new DashBlock { Chart = c };
        private static DashBlock Blk(DashTable t) => new DashBlock { Table = t };

        private static DashTable Tbl(string title, string[] cols, List<List<string>> rows, string? note = null)
            => new DashTable { Title = title, Columns = cols.ToList(), Rows = rows, Note = note };

        // جدول تقاطعی: سطرها × ستون‌ها = وزن (تن)
        private static DashTable Pivot<T>(string title, string rowTitle, List<T> items,
            Func<T, string> rowSel, Func<T, string> colSel, Func<T, decimal> kgSel,
            Func<string, bool>? wasteCol = null, int maxCols = 8)
        {
            var colOrder = items.GroupBy(colSel).OrderByDescending(g => g.Sum(kgSel)).Select(g => g.Key).ToList();
            var shown = colOrder.Take(maxCols).ToList();
            bool hasOther = colOrder.Count > maxCols;
            string ColOf(string c) => shown.Contains(c) ? c : "سایر";

            var seriesCols = new List<string>(shown);
            if (hasOther) seriesCols.Add("سایر");

            var cols = new List<string> { rowTitle };
            cols.AddRange(seriesCols);
            cols.Add("جمع (تن)");
            if (wasteCol != null) cols.Add("ضایعات %");

            var rows = new List<List<string>>();
            foreach (var rg in items.GroupBy(rowSel).OrderByDescending(g => g.Sum(kgSel)))
            {
                var row = new List<string> { rg.Key };
                foreach (var c in seriesCols)
                    row.Add(Tn(rg.Where(x => ColOf(colSel(x)) == c).Sum(kgSel)));
                var total = rg.Sum(kgSel);
                row.Add(Tn(total));
                if (wasteCol != null)
                    row.Add(Pct(rg.Where(x => wasteCol(colSel(x))).Sum(kgSel), total));
                rows.Add(row);
            }

            return Tbl(title, cols.ToArray(), rows);
        }

        // سری زمانی روزانه/هفتگی/ماهانه (انباشته بر اساس یک بُعد) + جدول
        private static List<DashBlock> SeriesBlocks(string group, string title,
            List<(DateTime G, string D, string Key, decimal Kg)> items, string keyTitle, bool cumulativeDaily)
        {
            var blocks = new List<DashBlock>();
            if (items.Count == 0) return blocks;

            const int MaxKeys = 6;
            var topKeys = items.GroupBy(i => i.Key).OrderByDescending(g => g.Sum(x => x.Kg)).Select(g => g.Key).ToList();
            var shown = topKeys.Take(MaxKeys).ToList();
            bool hasOther = topKeys.Count > MaxKeys;
            string KeyOf(string k) => shown.Contains(k) ? k : "سایر";

            var seriesKeys = new List<string>(shown);
            if (hasOther) seriesKeys.Add("سایر");

            foreach (var (variant, vTitle) in Variants)
            {
                var buckets = items
                    .Select(i => new { B = Bucket(variant, i.G, i.D), i.Key, i.Kg })
                    .GroupBy(x => x.B.Sort)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => new { Label = g.First().B.Label, Rows = g.ToList() })
                    .ToList();

                var labels = buckets.Select(b => b.Label).ToList();
                var datasets = seriesKeys
                    .Select(k => Ds(k, buckets.Select(b => TnD(b.Rows.Where(r => KeyOf(r.Key) == k).Sum(r => r.Kg)))))
                    .ToList();

                blocks.Add(new DashBlock
                {
                    Group = group,
                    Variant = variant,
                    Chart = MkChart($"{group}-{variant}", $"{title} — {vTitle} (به تفکیک {keyTitle})", "bar", labels, datasets, stacked: true)
                });

                var cols = new List<string> { "بازه" };
                cols.AddRange(seriesKeys);
                cols.Add("جمع (تن)");

                var rows = new List<List<string>>();
                foreach (var b in buckets)
                {
                    var row = new List<string> { b.Label };
                    foreach (var k in seriesKeys)
                        row.Add(Tn(b.Rows.Where(r => KeyOf(r.Key) == k).Sum(r => r.Kg)));
                    row.Add(Tn(b.Rows.Sum(r => r.Kg)));
                    rows.Add(row);
                }

                var totalRow = new List<string> { "جمع کل" };
                foreach (var k in seriesKeys)
                    totalRow.Add(Tn(items.Where(i => KeyOf(i.Key) == k).Sum(i => i.Kg)));
                totalRow.Add(Tn(items.Sum(i => i.Kg)));
                rows.Add(totalRow);

                blocks.Add(new DashBlock
                {
                    Group = group,
                    Variant = variant,
                    Table = Tbl($"{title} — {vTitle} (جدول)", cols.ToArray(), rows)
                });

                if (cumulativeDaily && variant == "daily")
                {
                    decimal run = 0;
                    var cum = new List<decimal>();
                    foreach (var b in buckets)
                    {
                        run += b.Rows.Sum(r => r.Kg);
                        cum.Add(TnD(run));
                    }
                    blocks.Add(new DashBlock
                    {
                        Group = group,
                        Variant = "daily",
                        Chart = MkChart($"{group}-cum", $"{title} — مجموع تجمعی", "line", labels, new List<DashDataset> { Ds("مجموع تجمعی (تن)", cum) })
                    });
                }
            }

            return blocks;
        }

        // ------------------------------------------------------------
        // تب ۱: تولید
        // ------------------------------------------------------------
        private DashTab BuildProduction(List<FRow> wf, List<FRow> allW, List<HarvestCycle> cycles,
            Dictionary<int, decimal?> plotArea, Dictionary<int, int> plotTrees)
        {
            var tab = new DashTab { Id = "production", Title = "تولید" };
            var cycleById = cycles.ToDictionary(c => c.Id);
            HarvestCycle? Cy(int? id) => id.HasValue && cycleById.TryGetValue(id.Value, out var c) ? c : null;

            // --- مقایسه‌ی دوره‌ها و تولید سالانه (همه‌ی دوره‌ها) ---
            var byCycle = allW.GroupBy(r => r.CycleId)
                .Select(g => new
                {
                    Name = Cy(g.Key)?.Name ?? "بدون دوره",
                    Year = Cy(g.Key)?.Year ?? 0,
                    Num = Cy(g.Key)?.CycleNumber ?? 0,
                    Kg = g.Sum(x => (decimal)x.Kg),
                    Cnt = g.Count()
                })
                .OrderBy(x => x.Year).ThenBy(x => x.Num)
                .ToList();

            if (byCycle.Count > 0)
            {
                tab.Blocks.Add(Blk(MkChart("prod-cycles", "تولید هر دوره (همه‌ی دوره‌ها)", "bar",
                    byCycle.Select(x => x.Name).ToList(),
                    new List<DashDataset> { Ds("تولید (تن)", byCycle.Select(x => TnD(x.Kg))) })));

                tab.Blocks.Add(Blk(Tbl("تولید هر دوره", new[] { "دوره", "سال", "تعداد برگه", "تولید (تن)" },
                    byCycle.Select(x => new List<string> { x.Name, x.Year == 0 ? "—" : x.Year.ToString(), F(x.Cnt), Tn(x.Kg) }).ToList())));

                var byYear = byCycle.GroupBy(x => x.Year).OrderBy(g => g.Key)
                    .Select(g => new List<string> { g.Key == 0 ? "—" : g.Key.ToString(), F(g.Count()), Tn(g.Sum(x => x.Kg)) }).ToList();
                tab.Blocks.Add(Blk(Tbl("تولید سالانه (جمع دوره‌های هر سال)", new[] { "سال", "تعداد دوره", "تولید (تن)" }, byYear)));
            }

            if (wf.Count == 0)
            {
                tab.Notice = "برای فیلتر انتخاب‌شده برگه‌ی توزین‌شده‌ای وجود ندارد.";
                return tab;
            }

            decimal total = wf.Sum(r => (decimal)r.Kg);

            // --- به تفکیک گونه ---
            var catC = new Canon();
            var byCat = wf.GroupBy(r => catC.Of(r.Category)).OrderByDescending(g => g.Sum(x => x.Kg)).ToList();
            tab.Blocks.Add(Blk(MkChart("prod-cat", "سهم هر گونه از تولید", "doughnut",
                byCat.Select(g => g.Key).ToList(),
                new List<DashDataset> { Ds("تولید (تن)", byCat.Select(g => TnD(g.Sum(x => (decimal)x.Kg)))) })));
            tab.Blocks.Add(Blk(Tbl("تولید به تفکیک گونه", new[] { "گونه", "تعداد برگه", "تولید (تن)", "سهم" },
                byCat.Select(g => new List<string> { g.Key, F(g.Count()), Tn(g.Sum(x => (decimal)x.Kg)), Pct(g.Sum(x => (decimal)x.Kg), total) }).ToList())));

            // --- به تفکیک محصول و رقم ---
            var prodC = new Canon();
            var varC = new Canon();
            var byVar = wf.GroupBy(r => (Product: prodC.Of(r.Product), Variety: string.IsNullOrWhiteSpace(r.Variety) ? "بدون رقم" : varC.Of(r.Variety)))
                .OrderByDescending(g => g.Sum(x => x.Kg)).ToList();
            var topVar = byVar.Take(12).ToList();
            tab.Blocks.Add(Blk(MkChart("prod-var", "تولید هر رقم (۱۲ رقم برتر)", "bar",
                topVar.Select(g => g.Key.Product + " - " + g.Key.Variety).ToList(),
                new List<DashDataset> { Ds("تولید (تن)", topVar.Select(g => TnD(g.Sum(x => (decimal)x.Kg)))) })));
            tab.Blocks.Add(Blk(Tbl("تولید به تفکیک محصول و رقم", new[] { "محصول", "رقم", "تعداد برگه", "تولید (تن)", "سهم" },
                byVar.Select(g => new List<string> { g.Key.Product, g.Key.Variety, F(g.Count()), Tn(g.Sum(x => (decimal)x.Kg)), Pct(g.Sum(x => (decimal)x.Kg), total) }).ToList())));

            // --- به تفکیک مزرعه ---
            var farmC = new Canon();
            var byFarm = wf.GroupBy(r => farmC.Of(r.Farm)).OrderByDescending(g => g.Sum(x => x.Kg)).ToList();
            tab.Blocks.Add(Blk(MkChart("prod-farm", "تولید هر مزرعه", "bar",
                byFarm.Select(g => g.Key).ToList(),
                new List<DashDataset> { Ds("تولید (تن)", byFarm.Select(g => TnD(g.Sum(x => (decimal)x.Kg)))) })));

            // --- به تفکیک قطعه (با مساحت و تعداد درخت) ---
            var plotRows = wf.GroupBy(r => (r.PlotId, Farm: farmC.Of(r.Farm), Plot: string.IsNullOrWhiteSpace(r.Plot) ? "بدون قطعه" : r.Plot!))
                .Select(g =>
                {
                    decimal kg = g.Sum(x => (decimal)x.Kg);
                    decimal? area = g.Key.PlotId.HasValue && plotArea.TryGetValue(g.Key.PlotId.Value, out var a) ? a : null;
                    int trees = g.Key.PlotId.HasValue && plotTrees.TryGetValue(g.Key.PlotId.Value, out var t) ? t : 0;
                    return new { g.Key.Farm, g.Key.Plot, Kg = kg, Area = area, Trees = trees };
                })
                .OrderByDescending(x => x.Kg)
                .ToList();

            tab.Blocks.Add(Blk(MkChart("prod-plot", "تولید هر قطعه (۱۵ قطعه‌ی برتر)", "bar",
                plotRows.Take(15).Select(x => x.Farm + " / " + x.Plot).ToList(),
                new List<DashDataset> { Ds("تولید (تن)", plotRows.Take(15).Select(x => TnD(x.Kg))) })));
            tab.Blocks.Add(Blk(Tbl("تولید به تفکیک قطعه",
                new[] { "مزرعه", "قطعه", "مساحت (هکتار)", "تعداد درخت", "تولید (تن)", "کیلو بر هکتار", "کیلو بر درخت" },
                plotRows.Select(x => new List<string>
                {
                    x.Farm, x.Plot,
                    x.Area.HasValue && x.Area.Value > 0 ? F(x.Area.Value, 2) : "—",
                    x.Trees > 0 ? F(x.Trees) : "—",
                    Tn(x.Kg),
                    x.Area.HasValue && x.Area.Value > 0 ? F(x.Kg / x.Area.Value) : "—",
                    x.Trees > 0 ? F(x.Kg / x.Trees, 1) : "—"
                }).ToList(),
                "کیلو بر هکتار و بر درخت فقط وقتی محاسبه می‌شود که مساحت قطعه و تعداد درخت ردیف‌ها ثبت شده باشد.")));

            return tab;
        }

        // ------------------------------------------------------------
        // تب ۲: برآورد در برابر واقعی
        // ------------------------------------------------------------
        private DashTab BuildEstimates(List<EstRow> est, List<FRow> cmp, string? notice, int cid)
        {
            var tab = new DashTab
            {
                Id = "estimate",
                Title = "برآورد و واقعی",
                Notice = notice,
                ActionUrl = Url.Action("Index", "YieldEstimates", new { cycleId = cid == 0 ? (int?)null : cid }),
                ActionText = "ثبت و ویرایش برآورد"
            };
            if (notice != null) return tab;

            var actualByVar = cmp.Where(r => r.VarietyId.HasValue)
                .GroupBy(r => r.VarietyId!.Value)
                .ToDictionary(g => g.Key, g => (Kg: g.Sum(x => (decimal)x.Kg), Product: g.First().Product, Variety: g.First().Variety));
            var estByVar = est.GroupBy(e => e.VarietyId)
                .ToDictionary(g => g.Key, g => (Kg: g.Sum(x => x.Kg), Product: g.First().Product, Variety: g.First().Variety));

            var ids = estByVar.Keys.Union(actualByVar.Keys).ToList();
            if (ids.Count == 0)
            {
                tab.Notice = "هنوز برآوردی ثبت نشده و برگه‌ی توزین‌شده‌ای با رقم مشخص هم وجود ندارد.";
                return tab;
            }

            var rows = new List<(string Label, string Product, string Variety, decimal Est, decimal Act, bool HasEst)>();
            foreach (var id in ids)
            {
                var hasE = estByVar.TryGetValue(id, out var e);
                var hasA = actualByVar.TryGetValue(id, out var a);
                var product = (hasE ? e.Product : null) ?? (hasA ? a.Product : null) ?? "—";
                var variety = (hasE ? e.Variety : null) ?? (hasA ? a.Variety : null) ?? "—";
                rows.Add((product + " - " + variety, product, variety, hasE ? e.Kg : 0m, hasA ? a.Kg : 0m, hasE));
            }
            rows = rows.OrderByDescending(r => Math.Max(r.Est, r.Act)).ToList();

            tab.Blocks.Add(Blk(MkChart("est-var", "برآورد در برابر تولید واقعی هر رقم", "bar",
                rows.Select(r => r.Label).ToList(),
                new List<DashDataset>
                {
                    Ds("برآورد (تن)", rows.Select(r => TnD(r.Est))),
                    Ds("واقعی (تن)", rows.Select(r => TnD(r.Act)))
                })));

            tab.Blocks.Add(Blk(Tbl("مقایسه‌ی برآورد و تولید واقعی به تفکیک رقم",
                new[] { "محصول", "رقم", "برآورد (تن)", "واقعی (تن)", "اختلاف (تن)", "درصد تحقق" },
                rows.Select(r => new List<string>
                {
                    r.Product, r.Variety,
                    r.HasEst ? Tn(r.Est) : "—",
                    Tn(r.Act),
                    r.HasEst ? Tn(r.Act - r.Est) : "—",
                    r.HasEst && r.Est > 0 ? Pct(r.Act, r.Est) : "—"
                }).ToList(),
                "واقعی = وزن خالص برگ‌های خروج از مزرعه‌ی توزین‌شده‌ی همان رقم در دوره‌ی انتخاب‌شده (بدون فیلتر تاریخ).")));

            // --- سطح قطعه (فقط اگر برآورد قطعه‌ای ثبت شده باشد) ---
            var plotEst = est.Where(e => e.PlotId.HasValue).ToList();
            if (plotEst.Count > 0)
            {
                var actualByPlotVar = cmp.Where(r => r.PlotId.HasValue && r.VarietyId.HasValue)
                    .GroupBy(r => (r.PlotId!.Value, r.VarietyId!.Value))
                    .ToDictionary(g => g.Key, g => g.Sum(x => (decimal)x.Kg));

                var plotRows = plotEst.GroupBy(e => (e.PlotId!.Value, e.VarietyId))
                    .Select(g =>
                    {
                        var first = g.First();
                        var key = (g.Key.Item1, g.Key.VarietyId);
                        actualByPlotVar.TryGetValue(key, out var act);
                        return new { first.Farm, first.Plot, first.Product, first.Variety, Est = g.Sum(x => x.Kg), Act = act };
                    })
                    .OrderBy(x => x.Farm).ThenBy(x => x.Plot)
                    .ToList();

                tab.Blocks.Add(Blk(Tbl("مقایسه‌ی برآورد و واقعی به تفکیک قطعه و رقم",
                    new[] { "مزرعه", "قطعه", "محصول", "رقم", "برآورد (تن)", "واقعی (تن)", "درصد تحقق" },
                    plotRows.Select(x => new List<string>
                    {
                        x.Farm ?? "—", x.Plot ?? "—", x.Product, x.Variety, Tn(x.Est), Tn(x.Act),
                        x.Est > 0 ? Pct(x.Act, x.Est) : "—"
                    }).ToList())));
            }

            return tab;
        }

        // ------------------------------------------------------------
        // تب ۳: برداشت
        // ------------------------------------------------------------
        private DashTab BuildHarvest(List<FRow> wf)
        {
            var tab = new DashTab { Id = "harvest", Title = "برداشت" };
            var pc = new Canon();
            var items = wf.Where(r => r.HasDate)
                .Select(r => (G: r.G, D: r.D, Key: pc.Of(r.Product), Kg: (decimal)r.Kg))
                .ToList();

            if (items.Count == 0)
            {
                tab.Notice = "برای فیلتر انتخاب‌شده برداشتی با تاریخ معتبر وجود ندارد.";
                return tab;
            }

            tab.Blocks.AddRange(SeriesBlocks("harvest", "برداشت", items, "محصول", cumulativeDaily: true));
            return tab;
        }

        // ------------------------------------------------------------
        // تب ۴: سورت
        // ------------------------------------------------------------
        private DashTab BuildSorting(List<SRow> ws)
        {
            var tab = new DashTab { Id = "sorting", Title = "سورت" };
            if (ws.Count == 0)
            {
                tab.Notice = "برای فیلتر انتخاب‌شده برگه‌ی سورتینگ توزین‌شده‌ای وجود ندارد.";
                return tab;
            }

            var pc = new Canon();
            var gc = new Canon();

            var items = ws.Where(r => r.HasDate)
                .Select(r => (G: r.G, D: r.D, Key: pc.Of(r.Product), Kg: (decimal)r.Kg))
                .ToList();
            var moveRows = ws.GroupBy(r => SortingMovement.ExitLabel(r.SaleType, r.Movement))
                .OrderBy(g => g.Key)
                .Select(g => new List<string>
                {
                    g.Key, F(g.Count()), Tn(g.Sum(x => (decimal)x.Kg)),
                    Tn(g.Where(x => x.Status != SaleConfirmed).Sum(x => (decimal)x.Kg))
                }).ToList();
            tab.Blocks.Add(Blk(Tbl("حرکت‌های سورتینگ به تفکیک نوع خروج",
                new[] { "نوع خروج", "تعداد برگه", "وزن (تن)", "تأییدنشده (تن)" }, moveRows,
                "فقط خروج برای فروش (نقدی و میدان) و خروج هدایا در خروجی کل و فروش حساب می‌شود؛ «جابجایی» (خروج برای سورت و خروج برای ذخیره‌سازی) جزو عملکرد سورتینگ است ولی دوباره شمرده نمی‌شود. وزن‌ها بر اساس باسکول است و «تأییدنشده» یعنی هنوز تأیید فروش نشده.")));

            tab.Blocks.AddRange(SeriesBlocks("sorting", "سورت", items, "محصول", cumulativeDaily: false));

            // کیفیت: توزیع درجه‌ها
            decimal total = ws.Sum(r => (decimal)r.Kg);
            var byGrade = ws.GroupBy(r => gc.Of(r.Grade)).OrderByDescending(g => g.Sum(x => x.Kg)).ToList();
            tab.Blocks.Add(Blk(MkChart("sort-grade", "سهم هر درجه از خروجی سورت", "doughnut",
                byGrade.Select(g => g.Key).ToList(),
                new List<DashDataset> { Ds("وزن (تن)", byGrade.Select(g => TnD(g.Sum(x => (decimal)x.Kg)))) })));
            tab.Blocks.Add(Blk(Tbl("توزیع درجه‌ها", new[] { "درجه", "تعداد برگه", "وزن (تن)", "سهم" },
                byGrade.Select(g => new List<string> { g.Key, F(g.Count()), Tn(g.Sum(x => (decimal)x.Kg)), Pct(g.Sum(x => (decimal)x.Kg), total) }).ToList())));

            var rowsForPivot = ws.Select(r => (Product: pc.Of(r.Product), Grade: gc.Of(r.Grade), Kg: (decimal)r.Kg)).ToList();
            tab.Blocks.Add(Blk(Pivot("درجه‌ها به تفکیک محصول", "محصول", rowsForPivot,
                x => x.Product, x => x.Grade, x => x.Kg, wasteCol: g => IsWaste(g))));

            return tab;
        }

        // ------------------------------------------------------------
        // تب ۵: فروش (نقدی / میدان / هدیه)
        // ------------------------------------------------------------
        private DashTab BuildSales(List<SRow> ws)
        {
            var tab = new DashTab { Id = "sales", Title = "فروش" };
            if (ws.Count == 0)
            {
                tab.Notice = "برای فیلتر انتخاب‌شده برگه‌ی سورتینگ توزین‌شده‌ای وجود ندارد.";
                return tab;
            }

            var pc = new Canon();
            var sc = new Canon();
            decimal total = ws.Sum(r => (decimal)r.Kg);

            var bySale = ws.GroupBy(r => sc.Of(r.SaleType)).OrderByDescending(g => g.Sum(x => x.Kg)).ToList();
            tab.Blocks.Add(Blk(MkChart("sale-type", "سهم هر نوع فروش", "doughnut",
                bySale.Select(g => g.Key).ToList(),
                new List<DashDataset> { Ds("وزن (تن)", bySale.Select(g => TnD(g.Sum(x => (decimal)x.Kg)))) })));
            tab.Blocks.Add(Blk(Tbl("فروش به تفکیک نوع", new[] { "نوع فروش", "تعداد برگه", "وزن (تن)", "سهم" },
                bySale.Select(g => new List<string> { g.Key, F(g.Count()), Tn(g.Sum(x => (decimal)x.Kg)), Pct(g.Sum(x => (decimal)x.Kg), total) }).ToList(),
                "تاریخ فروش همان تاریخ برگ خروج از سورتینگ است؛ همه‌ی درجه‌ها (از جمله ضایعات) حساب شده است.")));

            var pivotItems = ws.Select(r => (Product: pc.Of(r.Product), Sale: sc.Of(r.SaleType), Kg: (decimal)r.Kg)).ToList();
            tab.Blocks.Add(Blk(Pivot("نوع فروش به تفکیک محصول", "محصول", pivotItems, x => x.Product, x => x.Sale, x => x.Kg)));

            var series = ws.Where(r => r.HasDate)
                .Select(r => (G: r.G, D: r.D, Key: sc.Of(r.SaleType), Kg: (decimal)r.Kg))
                .ToList();
            tab.Blocks.AddRange(SeriesBlocks("sales", "فروش", series, "نوع فروش", cumulativeDaily: false));

            return tab;
        }

        // ------------------------------------------------------------
        // تب ۶: حمل و کامیون‌ها
        // ------------------------------------------------------------
        private DashTab BuildTrucks(List<FRow> wf, List<SRow> ws)
        {
            var tab = new DashTab { Id = "trucks", Title = "حمل" };
            var plateC = new Canon();
            var destC = new Canon();
            var drvC = new Canon();

            if (wf.Count > 0)
            {
                var rows = wf.Select(r => new
                {
                    Plate = plateC.Of(r.Plate),
                    Dest = destC.Of(r.Dest),
                    Driver = string.IsNullOrWhiteSpace(r.Driver) ? null : drvC.Of(r.Driver),
                    Kg = (decimal)r.Kg
                }).ToList();

                var byPlate = rows.GroupBy(x => x.Plate)
                    .Select(g => new
                    {
                        Plate = g.Key,
                        Trips = g.Count(),
                        Kg = g.Sum(x => x.Kg),
                        Drivers = string.Join("، ", g.Where(x => x.Driver != null).Select(x => x.Driver!).Distinct()),
                        Dests = string.Join("، ", g.Select(x => x.Dest).Distinct())
                    })
                    .OrderByDescending(x => x.Kg).ToList();

                var top = byPlate.Take(10).ToList();
                tab.Blocks.Add(Blk(MkChart("truck-farm", "تناژ حمل از مزرعه — ۱۰ کامیون برتر", "bar",
                    top.Select(x => x.Plate).ToList(),
                    new List<DashDataset> { Ds("تناژ (تن)", top.Select(x => TnD(x.Kg))) })));

                tab.Blocks.Add(Blk(Tbl("حمل از مزرعه — خلاصه‌ی هر کامیون",
                    new[] { "پلاک", "رانندگان", "تعداد سفر", "تناژ کل (تن)", "میانگین هر سفر (تن)", "مقصدها" },
                    byPlate.Select(x => new List<string> { x.Plate, x.Drivers.Length > 0 ? x.Drivers : "—", F(x.Trips), Tn(x.Kg), Tn(x.Kg / x.Trips), x.Dests }).ToList())));

                var detail = rows.GroupBy(x => (x.Plate, x.Dest))
                    .Select(g => new
                    {
                        g.Key.Plate,
                        g.Key.Dest,
                        Drivers = string.Join("، ", g.Where(x => x.Driver != null).Select(x => x.Driver!).Distinct()),
                        Trips = g.Count(),
                        Kg = g.Sum(x => x.Kg)
                    })
                    .OrderBy(x => x.Plate).ThenByDescending(x => x.Kg).ToList();
                tab.Blocks.Add(Blk(Tbl("حمل از مزرعه — هر کامیون به تفکیک مقصد",
                    new[] { "پلاک", "مقصد", "راننده", "تعداد سفر", "تناژ (تن)" },
                    detail.Select(x => new List<string> { x.Plate, x.Dest, x.Drivers.Length > 0 ? x.Drivers : "—", F(x.Trips), Tn(x.Kg) }).ToList())));

                var byDest = rows.GroupBy(x => x.Dest).OrderByDescending(g => g.Sum(x => x.Kg)).ToList();
                tab.Blocks.Add(Blk(MkChart("truck-dest", "تناژ حمل از مزرعه به تفکیک مقصد", "bar",
                    byDest.Select(g => g.Key).ToList(),
                    new List<DashDataset> { Ds("تناژ (تن)", byDest.Select(g => TnD(g.Sum(x => x.Kg)))) })));
            }

            if (ws.Count > 0)
            {
                var rows = ws.Select(r => new { Plate = plateC.Of(r.Plate), Dest = destC.Of(r.Dest), Kg = (decimal)r.Kg }).ToList();

                var detail = rows.GroupBy(x => (x.Plate, x.Dest))
                    .Select(g => new { g.Key.Plate, g.Key.Dest, Trips = g.Count(), Kg = g.Sum(x => x.Kg) })
                    .OrderBy(x => x.Plate).ThenByDescending(x => x.Kg).ToList();

                var topPlates = rows.GroupBy(x => x.Plate)
                    .Select(g => new { Plate = g.Key, Kg = g.Sum(x => x.Kg) })
                    .OrderByDescending(x => x.Kg).Take(10).ToList();
                tab.Blocks.Add(Blk(MkChart("truck-sort", "تناژ حمل از سورتینگ — ۱۰ کامیون برتر", "bar",
                    topPlates.Select(x => x.Plate).ToList(),
                    new List<DashDataset> { Ds("تناژ (تن)", topPlates.Select(x => TnD(x.Kg))) })));

                tab.Blocks.Add(Blk(Tbl("حمل از سورتینگ — هر کامیون به تفکیک مقصد",
                    new[] { "پلاک", "مقصد", "تعداد سفر", "تناژ (تن)" },
                    detail.Select(x => new List<string> { x.Plate, x.Dest, F(x.Trips), Tn(x.Kg) }).ToList(),
                    "در برگ سورتینگ نام راننده ثبت نمی‌شود؛ این جدول بر اساس پلاک است.")));

                var byDest = rows.GroupBy(x => x.Dest).OrderByDescending(g => g.Sum(x => x.Kg)).ToList();
                tab.Blocks.Add(Blk(Tbl("تناژ خروجی سورتینگ به تفکیک مقصد", new[] { "مقصد", "تعداد برگه", "تناژ (تن)" },
                    byDest.Select(g => new List<string> { g.Key, F(g.Count()), Tn(g.Sum(x => x.Kg)) }).ToList())));
            }

            if (tab.Blocks.Count == 0)
                tab.Notice = "برای فیلتر انتخاب‌شده برگه‌ی توزین‌شده‌ای وجود ندارد.";

            return tab;
        }

        // ------------------------------------------------------------
        // تب ۷: پیمانکاران
        // ------------------------------------------------------------
        private DashTab BuildContractors(List<FRow> wf, List<SRow> ws)
        {
            var tab = new DashTab { Id = "contractors", Title = "پیمانکاران" };
            var conC = new Canon();
            var prodC = new Canon();
            var hallC = new Canon();
            var methC = new Canon();

            if (ws.Count > 0)
            {
                var items = ws.Select(r => (Contractor: conC.Of(r.Contractor), Hall: hallC.Of(r.Source), Product: prodC.Of(r.Product), Kg: (decimal)r.Kg)).ToList();

                // نمودار: پیمانکار × محصول (انباشته)
                var contractors = items.GroupBy(x => x.Contractor).OrderByDescending(g => g.Sum(x => x.Kg)).Select(g => g.Key).ToList();
                var topProducts = items.GroupBy(x => x.Product).OrderByDescending(g => g.Sum(x => x.Kg)).Select(g => g.Key).Take(6).ToList();
                var datasets = topProducts
                    .Select(p => Ds(p, contractors.Select(c => TnD(items.Where(x => x.Contractor == c && x.Product == p).Sum(x => x.Kg)))))
                    .ToList();
                var otherSet = contractors.Select(c => TnD(items.Where(x => x.Contractor == c && !topProducts.Contains(x.Product)).Sum(x => x.Kg))).ToList();
                if (otherSet.Any(v => v > 0)) datasets.Add(Ds("سایر", otherSet));

                tab.Blocks.Add(Blk(MkChart("con-sort", "سورت هر پیمانکار به تفکیک محصول", "bar", contractors, datasets, stacked: true)));
                tab.Blocks.Add(Blk(Pivot("سورت پیمانکاران به تفکیک محصول", "پیمانکار سورت", items, x => x.Contractor, x => x.Product, x => x.Kg)));

                var detail = items.GroupBy(x => (x.Contractor, x.Hall, x.Product))
                    .Select(g => new { g.Key.Contractor, g.Key.Hall, g.Key.Product, Kg = g.Sum(x => x.Kg), Cnt = g.Count() })
                    .OrderBy(x => x.Contractor).ThenBy(x => x.Hall).ThenByDescending(x => x.Kg).ToList();
                tab.Blocks.Add(Blk(Tbl("سورت پیمانکاران به تفکیک سورتینگ (مبدا) و محصول",
                    new[] { "پیمانکار سورت", "سورتینگ (مبدا)", "محصول", "تعداد برگه", "وزن (تن)" },
                    detail.Select(x => new List<string> { x.Contractor, x.Hall, x.Product, F(x.Cnt), Tn(x.Kg) }).ToList())));
            }

            if (wf.Count > 0)
            {
                var items = wf.Select(r => (Contractor: conC.Of(r.Contractor), Method: methC.Of(r.Method), Kg: (decimal)r.Kg)).ToList();
                var byCon = items.GroupBy(x => x.Contractor).OrderByDescending(g => g.Sum(x => x.Kg)).ToList();

                tab.Blocks.Add(Blk(MkChart("con-harvest", "برداشت هر پیمانکار", "bar",
                    byCon.Select(g => g.Key).ToList(),
                    new List<DashDataset> { Ds("برداشت (تن)", byCon.Select(g => TnD(g.Sum(x => x.Kg)))) })));
                tab.Blocks.Add(Blk(Pivot("برداشت پیمانکاران به تفکیک روش برداشت", "پیمانکار برداشت", items, x => x.Contractor, x => x.Method, x => x.Kg)));
            }

            if (tab.Blocks.Count == 0)
                tab.Notice = "برای فیلتر انتخاب‌شده برگه‌ی توزین‌شده‌ای وجود ندارد.";

            return tab;
        }

        // ------------------------------------------------------------
        // تب ۸: سالن سورت (ورودی در برابر خروجی)
        // ------------------------------------------------------------
        private DashTab BuildHall(List<FRow> wf, List<SRow> ws, List<SRow> wsInternal)
        {
            var tab = new DashTab { Id = "hall", Title = "سالن سورت" };

            var intake = wf.Where(r => IsHall(r.Dest) && r.HasDate).Select(r => (G: r.G, D: r.D, Kg: (decimal)r.Kg)).ToList();
            var output = ws.Where(r => r.HasDate).Select(r => (G: r.G, D: r.D, Kg: (decimal)r.Kg)).ToList();

            if (intake.Count == 0 && output.Count == 0)
            {
                tab.Notice = "ورودی به سالن (برگ‌های مزرعه با مقصد «سورتینگ») یا خروجی سورتینگ با تاریخ معتبر وجود ندارد.";
                return tab;
            }

            foreach (var (variant, vTitle) in Variants)
            {
                var map = new SortedDictionary<string, (string Label, decimal In, decimal Out)>(StringComparer.Ordinal);
                foreach (var i in intake)
                {
                    var b = Bucket(variant, i.G, i.D);
                    map.TryGetValue(b.Sort, out var cur);
                    map[b.Sort] = (b.Label, cur.In + i.Kg, cur.Out);
                }
                foreach (var o in output)
                {
                    var b = Bucket(variant, o.G, o.D);
                    map.TryGetValue(b.Sort, out var cur);
                    map[b.Sort] = (b.Label, cur.In, cur.Out + o.Kg);
                }

                var labels = new List<string>();
                var ins = new List<decimal>();
                var outs = new List<decimal>();
                var bal = new List<decimal>();
                var rows = new List<List<string>>();
                decimal run = 0;
                foreach (var kv in map)
                {
                    run += kv.Value.In - kv.Value.Out;
                    labels.Add(kv.Value.Label);
                    ins.Add(TnD(kv.Value.In));
                    outs.Add(TnD(kv.Value.Out));
                    bal.Add(TnD(run));
                    rows.Add(new List<string> { kv.Value.Label, Tn(kv.Value.In), Tn(kv.Value.Out), Tn(kv.Value.In - kv.Value.Out), Tn(run) });
                }

                tab.Blocks.Add(new DashBlock
                {
                    Group = "hall",
                    Variant = variant,
                    Chart = MkChart($"hall-{variant}", $"ورود و خروج سالن سورت — {vTitle}", "bar", labels, new List<DashDataset>
                    {
                        Ds("ورودی از مزرعه (تن)", ins),
                        Ds("خروجی از سورتینگ (تن)", outs),
                        Ds("مانده‌ی تجمعی (تن)", bal, "line")
                    })
                });
                tab.Blocks.Add(new DashBlock
                {
                    Group = "hall",
                    Variant = variant,
                    Table = Tbl($"ورود و خروج سالن سورت — {vTitle} (جدول)",
                        new[] { "بازه", "ورودی (تن)", "خروجی (تن)", "تفاوت (تن)", "مانده‌ی تجمعی (تن)" }, rows,
                        "ورودی = برگ‌های خروج از مزرعه که مقصدشان «سورتینگ» است (تاریخ ورود = تاریخ برداشت)؛ خروجی = برگ‌های سورتینگ به‌جز «جابجایی» (جابجایی داخلی جدا نشان داده می‌شود). مانده‌ی تجمعی = ورودی منهای خروجی؛ شامل ضایعات و کسری سالن است.")
                });
            }

            // مقایسه به تفکیک سورتینگ (نام مقصد مزرعه با «مبدا» سورتینگ)
            var hallC = new Canon();
            var inByHall = wf.Where(r => IsHall(r.Dest)).GroupBy(r => hallC.Of(r.Dest)).ToDictionary(g => g.Key, g => g.Sum(x => (decimal)x.Kg));
            var outByHall = ws.GroupBy(r => hallC.Of(r.Source)).ToDictionary(g => g.Key, g => g.Sum(x => (decimal)x.Kg));
            var halls = inByHall.Keys.Union(outByHall.Keys).OrderBy(k => k).ToList();
            if (halls.Count > 0)
            {
                tab.Blocks.Add(Blk(Tbl("مقایسه به تفکیک هر سورتینگ",
                    new[] { "سورتینگ", "ورودی از مزرعه (تن)", "خروجی (تن)", "کسری (تن)", "کسری %" },
                    halls.Select(h =>
                    {
                        inByHall.TryGetValue(h, out var vin);
                        outByHall.TryGetValue(h, out var vout);
                        return new List<string> { h, Tn(vin), Tn(vout), Tn(vin - vout), Pct(vin - vout, vin) };
                    }).ToList(),
                    "اگر نام مقصد در برگ مزرعه و «مبدا» در برگ سورتینگ یکسان نوشته شود، مقایسه‌ی هر سورتینگ دقیق است؛ در غیر این صورت ردیف‌ها جدا می‌افتند.")));
            }

            // جابجایی‌های داخلی (انتقال به سالن سورت و برگشت به سردخانه)
            var internalItems = wsInternal.Where(r => r.HasDate)
                .Select(r => (G: r.G, D: r.D, Key: SortingMovement.ExitLabel(r.SaleType, r.Movement), Kg: (decimal)r.Kg))
                .ToList();
            if (internalItems.Count > 0)
                tab.Blocks.AddRange(SeriesBlocks("hallint", "جابجایی داخلی سردخانه و سالن سورت (در خروجی کل حساب نمی‌شود)", internalItems, "نوع خروج", cumulativeDaily: false));

            return tab;
        }

        // ------------------------------------------------------------
        // تب ۹: هشدارها
        // ------------------------------------------------------------
        private DashTab BuildAlerts(List<FRow> farm1, List<SRow> sort1)
        {
            var tab = new DashTab { Id = "alerts", Title = "هشدارها" };

            var fPending = farm1.Where(r => r.Status == PendingStatus && r.Kg == 0).ToList();
            var fWeighed = farm1.Where(r => r.Status == WeighedStatus).ToList();
            var sPending = sort1.Where(r => r.Status == PendingStatus && r.Kg == 0).ToList();
            var sWeighed = sort1.Where(r => r.Status == WeighedStatus).ToList();

            tab.Blocks.Add(Blk(Tbl("برگه‌های منتظر اقدام", new[] { "وضعیت", "تعداد" }, new List<List<string>>
            {
                new() { "برگ مزرعه — منتظر توزین", F(fPending.Count) },
                new() { "برگ مزرعه — توزین‌شده و منتظر تأیید مقصد", F(fWeighed.Count) },
                new() { "برگ سورتینگ — منتظر توزین", F(sPending.Count) },
                new() { "برگ سورتینگ — توزین‌شده و منتظر تأیید فروش", F(sWeighed.Count) }
            })));

            var pendingRows = new List<List<string>>();
            pendingRows.AddRange(fPending.OrderByDescending(r => r.Id).Take(20)
                .Select(r => new List<string> { "مزرعه", r.Date ?? "—", r.Number, Nz(r.Farm), Nz(r.Product) }));
            pendingRows.AddRange(sPending.OrderByDescending(r => r.Id).Take(20)
                .Select(r => new List<string> { "سورتینگ", r.Date ?? "—", r.Number, Nz(r.Farm), Nz(r.Product) }));
            if (pendingRows.Count > 0)
                tab.Blocks.Add(Blk(Tbl("برگه‌های منتظر توزین (حداکثر ۲۰ مورد اخیر از هر نوع)",
                    new[] { "نوع", "تاریخ", "شماره برگه", "مزرعه", "محصول" }, pendingRows)));

            var anomalies = new List<List<string>>();
            anomalies.AddRange(Anomalies("مزرعه", farm1.Where(r => r.Baskets.HasValue)
                .Select(r => (r.Number, r.Date, Nz(r.Product), r.Kg, r.Baskets!.Value))));
            anomalies.AddRange(Anomalies("سورتینگ", sort1.Where(r => r.Baskets.HasValue)
                .Select(r => (r.Number, r.Date, Nz(r.Product), r.Kg, r.Baskets!.Value))));
            if (anomalies.Count > 0)
                tab.Blocks.Add(Blk(Tbl("کیلوگرم بر سبد غیرعادی (بیش از ۴۰٪ اختلاف با میانه‌ی همان محصول)",
                    new[] { "نوع", "تاریخ", "شماره برگه", "محصول", "وزن (kg)", "تعداد سبد", "کیلو بر سبد", "میانه‌ی محصول" }, anomalies,
                    "برای بررسی خطای ثبت وزن یا تعداد سبد؛ فقط محصول‌هایی که حداقل ۵ برگه دارند بررسی می‌شوند.")));

            return tab;
        }

        private static List<List<string>> Anomalies(string type, IEnumerable<(string Number, string? Date, string Product, int Kg, int Baskets)> src)
        {
            var list = src.Where(r => r.Baskets > 0 && r.Kg > 0).ToList();
            var res = new List<(decimal Dev, List<string> Row)>();

            foreach (var g in list.GroupBy(r => Norm(r.Product)))
            {
                var items = g.ToList();
                if (items.Count < 5) continue;

                var med = Median(items.Select(r => (decimal)r.Kg / r.Baskets).ToList());
                if (med <= 0) continue;

                foreach (var r in items)
                {
                    var ratio = (decimal)r.Kg / r.Baskets;
                    var dev = Math.Abs(ratio / med - 1m);
                    if (dev > 0.4m)
                        res.Add((dev, new List<string> { type, r.Date ?? "—", r.Number, r.Product, F(r.Kg), F(r.Baskets), F(ratio, 1), F(med, 1) }));
                }
            }

            return res.OrderByDescending(x => x.Dev).Take(15).Select(x => x.Row).ToList();
        }
    }
}
