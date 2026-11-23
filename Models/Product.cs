using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class Product
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "نام محصول الزامی است")]
        [StringLength(100)]
        [Display(Name = "نام محصول")]
        public string Name { get; set; } = string.Empty;

        // 🆕 ارتباط با گونه
        public int? CategoryId { get; set; }
        [ForeignKey("CategoryId")]
        [Display(Name = "گونه")]
        public Category? Category { get; set; }

        [StringLength(500)]
        [Display(Name = "توضیحات")]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<ProductVariety> Varieties { get; set; } = new();
    }
}