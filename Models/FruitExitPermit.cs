using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class FruitExitPermit
    {
        public int Id { get; set; }

        [Display(Name = "شماره برگ خروج")]
        [Required(ErrorMessage = "وارد کردن شماره برگ خروج الزامی است")]
        [StringLength(15, ErrorMessage = "شماره برگ خروج نمی‌تواند بیشتر از ۱۵ رقم باشد")]
        public string PermitNumber { get; set; } = string.Empty;

        [Display(Name = "تاریخ")]
        [StringLength(10)]
        public string? Date { get; set; }

        public int? FarmId { get; set; }
        [ForeignKey("FarmId")]
        public Farm? Farm { get; set; }

        public int? PlotId { get; set; }
        [ForeignKey("PlotId")]
        public Plot? Plot { get; set; }

        // 👈 فیلد قدیمی (برای حفظ داده‌های قبلی و اکسل)
        [Display(Name = "نوع محصول")]
        [StringLength(50)]
        public string? ProductType { get; set; }

        // 🆕 فیلدهای جدید ارتباطی
        public int? ProductId { get; set; }
        [ForeignKey("ProductId")]
        public Product? Product { get; set; }

        public int? VarietyId { get; set; }
        [ForeignKey("VarietyId")]
        public ProductVariety? Variety { get; set; }

        public int? HarvestCycleId { get; set; }
        [ForeignKey("HarvestCycleId")]
        public HarvestCycle? HarvestCycle { get; set; }

        [Display(Name = "روش برداشت")]
        [StringLength(50)]
        public string? HarvestMethod { get; set; }

        [Display(Name = "تعداد سبد")]
        public int? BasketCount { get; set; }

        [Display(Name = "نوع سبد")]
        [StringLength(50)]
        public string? BasketType { get; set; }

        [Display(Name = "مقصد")]
        [StringLength(100)]
        public string? Destination { get; set; }

        [Display(Name = "شماره پلاک وسیله نقلیه")]
        [StringLength(20)]
        public string? VehiclePlate { get; set; }

        [Display(Name = "نام راننده")]
        [StringLength(100)]
        public string? DriverName { get; set; }

        [Display(Name = "نام پیمانکار برداشت")]
        [StringLength(100)]
        public string? ContractorName { get; set; }

        [Display(Name = "توضیحات")]
        [StringLength(500)]
        public string? Description { get; set; }

        [Display(Name = "وضعیت")]
        public string Status { get; set; } = "در انتظار توزین";

        [Display(Name = "تاریخ ثبت سیستمی")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ردیف‌های محصول/رقم در این برگ
        public List<FruitExitPermitItem> Items { get; set; } = new();

        // ارتباط قدیمی قبض‌ها برای سازگاری تا زمان مهاجرت کامل کنترلرها
        public List<BasculTicket> BasculTickets { get; set; } = new();
    }
}