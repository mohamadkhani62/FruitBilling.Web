using System.ComponentModel.DataAnnotations;

namespace FruitBilling.Web.Models
{
    public class Category
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "نام گونه الزامی است")]
        [StringLength(100)]
        [Display(Name = "نام گونه (مثلاً مرکبات، صیفی‌جات)")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        [Display(Name = "توضیحات")]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ارتباط با محصولات و دوره‌ها
        public List<Product> Products { get; set; } = new();
        public List<HarvestCycle> HarvestCycles { get; set; } = new();
    }
}