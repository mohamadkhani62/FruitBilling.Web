namespace FruitBilling.Web.Models
{
    // گونه‌های انتخاب‌شده برای هر دوره برداشت
    public class HarvestCycleCategory
    {
        public int HarvestCycleId { get; set; }
        public HarvestCycle HarvestCycle { get; set; } = null!;

        public int CategoryId { get; set; }
        public Category Category { get; set; } = null!;
    }
}
