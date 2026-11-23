namespace FruitBilling.Web.Models
{
    // برآورد تولید: دوره × رقم (و اختیاری: قطعه)
    public class YieldEstimate
    {
        public int Id { get; set; }

        public int HarvestCycleId { get; set; }
        public HarvestCycle? HarvestCycle { get; set; }

        public int VarietyId { get; set; }
        public ProductVariety? Variety { get; set; }

        public int? PlotId { get; set; }
        public Plot? Plot { get; set; }

        // برآورد به کیلوگرم
        public decimal EstimatedKg { get; set; }

        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
