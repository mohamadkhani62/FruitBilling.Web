namespace FruitBilling.Web.Models;

public class Road
{
    public int Id { get; set; }

    // 🆕 جاده می‌تواند داخلی (داخل مزرعه) یا بیرونی (بین مزارع) باشد
    public string RoadType { get; set; } = "internal"; // internal | external

    // 🆕 جاده می‌تواند به مزرعه یا قطعه یا هیچکدام (بیرونی) تعلق داشته باشد
    public int? FarmId { get; set; }
    public Farm? Farm { get; set; }
    public int? PlotId { get; set; }
    public Plot? Plot { get; set; }

    public string Name { get; set; } = "";
    public string CoordinatesJson { get; set; } = "[]"; // مسیر جاده (چند نقطه)

    // 🆕 عرض جاده (متر) — برای رسم به‌صورت polygon
    public double WidthMeters { get; set; } = 3.0;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}