public class MapFeature
{
    public int Id { get; set; }
    public string Name { get; set; } = ""; // نام عارضه (مثلاً «مزرعه شماره ۱»)
    public string FeatureType { get; set; } = ""; // نوع: Farm, Tree, Building, Road, Mountain, River, Utility, Well

    // مختصات به صورت JSON ذخیره می‌شود تا هم نقطه و هم چندضلعی پشتیبانی شود
    // مثال: [{"lat":28.54,"lng":57.85},{"lat":28.55,"lng":57.86}]
    public string CoordinatesJson { get; set; } = "[]";

    // فقط برای درخت‌ها و ساختمان‌ها (مدل سه‌بعدی)
    public string? ModelUrl { get; set; } // مسیر فایل glb
    public double? Height { get; set; } // ارتفاع (برای ساختمان‌ها)

    // استایل (رنگ، شفافیت)
    public string? ColorHex { get; set; } // مثلاً "#FF0000"
    public double? Opacity { get; set; } // 0.0 تا 1.0
}