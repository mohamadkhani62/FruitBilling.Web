using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class SortingExitPermit
    {
        public int Id { get; set; }

        [Display(Name = "شماره برگ خروج")]
        [Required(ErrorMessage = "وارد کردن شماره برگ خروج الزامی است")]
        [StringLength(15)]
        public string PermitNumber { get; set; } = string.Empty;

        [Display(Name = "تاریخ")]
        [StringLength(10)]
        public string? Date { get; set; }

        [Display(Name = "تعداد سبد")]
        public int? BasketCount { get; set; }

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

        [Display(Name = "درجه")]
        [StringLength(50)]
        public string? Grade { get; set; }

        [Display(Name = "نام مزرعه")]
        [StringLength(100)]
        public string? FarmName { get; set; }

        [Display(Name = "شماره پلاک")]
        [StringLength(20)]
        public string? VehiclePlate { get; set; }

        [Display(Name = "مقصد")]
        [StringLength(100)]
        public string? Destination { get; set; }

        [Display(Name = "نوع فروش")]
        [StringLength(50)]
        public string? SaleType { get; set; }

        [Display(Name = "مبدا")]
        [StringLength(100)]
        public string? Source { get; set; }

        [Display(Name = "پیمانکار سورت")]
        [StringLength(100)]
        public string? SortingContractor { get; set; }

        // نوع خروج: وابسته به نوع فروش (نقدی/میدان ← خروج برای فروش، هدیه ← خروج هدایا،
        // جابجایی ← خروج برای سورت یا خروج برای ذخیره‌سازی)
        [Display(Name = "نوع خروج")]
        [StringLength(40)]
        public string? MovementType { get; set; }

        [Display(Name = "توضیحات")]
        [StringLength(500)]
        public string? Description { get; set; }

        [Display(Name = "وضعیت")]
        public string Status { get; set; } = "در انتظار توزین";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ارتباط یک به چند با قبض‌های باسکول
        public List<SortingBasculTicket> BasculTickets { get; set; } = new();
    }
}