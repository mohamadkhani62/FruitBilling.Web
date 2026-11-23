namespace FruitBilling.Web.Models;

public class Plot
{
    public int Id { get; set; }
    public int FarmId { get; set; }
    public Farm? Farm { get; set; }

    public string Name { get; set; } = "";
    public string CoordinatesJson { get; set; } = "[]";  // مرز قطعه
    public decimal? AreaHectare { get; set; }

    // 👇 تنظیمات قسمت‌بندی (برای مسیر عبور ادوات)
    public int SectionCount { get; set; } = 1;           // تعداد قسمت‌ها
    public double SectionGapMeters { get; set; } = 0;    // عرض مسیر عبور (متر)

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<TreeRow> TreeRows { get; set; } = new();
}