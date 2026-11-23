using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class ProductVariety
    {
        public int Id { get; set; }

        [Required]
        public int ProductId { get; set; }
        [ForeignKey("ProductId")]
        public Product? Product { get; set; }

        [Required(ErrorMessage = "نام رقم الزامی است")]
        [StringLength(100)]
        [Display(Name = "نام رقم")]
        public string Name { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ارتباط با برگه‌ها
        public List<FruitExitPermit> FruitExitPermits { get; set; } = new();
        public List<SortingExitPermit> SortingExitPermits { get; set; } = new();
    }
}