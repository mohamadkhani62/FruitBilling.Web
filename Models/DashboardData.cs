using Microsoft.AspNetCore.Mvc.Rendering;

namespace FruitBilling.Web.Models
{
    public class DashTable
    {
        public string Title { get; set; } = "";
        public string? Note { get; set; }
        public List<string> Columns { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
    }

    public class DashDataset
    {
        public string Label { get; set; } = "";
        public List<decimal> Data { get; set; } = new();
        public string? Type { get; set; }
    }

    public class DashChart
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Type { get; set; } = "bar";
        public List<string> Labels { get; set; } = new();
        public List<DashDataset> Datasets { get; set; } = new();
        public bool Stacked { get; set; }
        public string? Unit { get; set; }
    }

    // یک بلوک نمودار یا جدول؛ Group/Variant برای سوییچ روزانه/هفتگی/ماهانه
    public class DashBlock
    {
        public string? Group { get; set; }
        public string? Variant { get; set; }
        public DashChart? Chart { get; set; }
        public DashTable? Table { get; set; }
    }

    public class DashKpi
    {
        public string Title { get; set; } = "";
        public string Value { get; set; } = "";
        public string? Sub { get; set; }
        public string Css { get; set; } = "primary";
    }

    public class DashTab
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Notice { get; set; }
        public string? ActionUrl { get; set; }
        public string? ActionText { get; set; }
        public List<DashBlock> Blocks { get; set; } = new();
    }

    public class DashboardPageModel
    {
        public int CycleId { get; set; }
        public string CycleName { get; set; } = "";
        public string? FromDate { get; set; }
        public string? ToDate { get; set; }
        public string? Product { get; set; }
        public string? Farm { get; set; }
        public int InvalidDateRows { get; set; }

        public List<SelectListItem> Cycles { get; set; } = new();
        public List<string> ProductNames { get; set; } = new();
        public List<string> FarmNames { get; set; } = new();

        public List<DashKpi> Kpis { get; set; } = new();
        public List<DashTab> Tabs { get; set; } = new();
    }
}
