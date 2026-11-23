using System.Collections.Generic;

namespace FruitBilling.Web.Models
{
    // 🆕 مدل اصلی داشبورد
    public class DashboardViewModels
    {
        public List<ProductSummary> TotalByProduct { get; set; } = new();
        public List<DaySummary> DaysByProduct { get; set; } = new();

        public decimal GrandTotalWeight { get; set; }
        public int TotalDistinctDays { get; set; }

        // برای فیلترها
        public string? FromDate { get; set; }
        public string? ToDate { get; set; }
        public string? Product { get; set; }
        public string? FarmName { get; set; }
        public string? Destination { get; set; }

        public List<string> AllProducts { get; set; } = new();
        public List<string> AllSections { get; set; } = new();
        public List<string> AllDestinations { get; set; } = new();

        // برای نمودارها
        public string TotalByDateJson { get; set; } = "[]";
        public string DatesJson { get; set; } = "[]";
        public string ProductSeriesJson { get; set; } = "[]";
    }

    // 🆕 کلاس خلاصه محصول (جدول ۱)
    public class ProductSummary
    {
        public string Product { get; set; } = "";
        public decimal TotalWeight { get; set; }
    }

    // 🆕 کلاس خلاصه روزها (جدول ۲)
    public class DaySummary
    {
        public string Product { get; set; } = "";
        public int DistinctDays { get; set; }
    }

    // 🆕 کلاس داده‌های نمودار ۱
    public class DateSeries
    {
        public string Date { get; set; } = "";
        public decimal TotalWeight { get; set; }
    }

    // 🆕 کلاس داده‌های نمودار ۲
    public class ProductSeriesItem
    {
        public string Product { get; set; } = "";
        public List<decimal> Data { get; set; } = new();
    }
}