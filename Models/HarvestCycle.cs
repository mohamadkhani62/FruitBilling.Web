using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class HarvestCycle
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "عنوان دوره الزامی است")]
        [StringLength(100)]
        [Display(Name = "عنوان دوره (مثلاً دوره اول ۱۴۰۳)")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "سال زراعی")]
        public int Year { get; set; }

        [Display(Name = "شماره دوره (۱ تا ۳)")]
        [Range(1, 3, ErrorMessage = "شماره دوره باید بین ۱ تا ۳ باشد")]
        public int CycleNumber { get; set; } = 1;

        // 🆕 ارتباط با گونه (نه محصول)
        public int? CategoryId { get; set; }
        [ForeignKey("CategoryId")]
        [Display(Name = "گونه مربوطه")]
        public Category? Category { get; set; }

        [StringLength(10)]
        [Display(Name = "تاریخ شروع (شمسی)")]
        public string? StartDate { get; set; }

        [StringLength(10)]
        [Display(Name = "تاریخ پایان (شمسی)")]
        public string? EndDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<HarvestCycleCategory> HarvestCycleCategories { get; set; } = new();

        // رابطه قدیمی تک‌گونه برای سازگاری با داده‌ها و کدهای فعلی حفظ شده است.
        public List<FruitExitPermit> FruitExitPermits { get; set; } = new();
        public List<SortingExitPermit> SortingExitPermits { get; set; } = new();
    }
}