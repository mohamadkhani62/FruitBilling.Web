using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    // هر ردیف محصول/رقم در یک برگ خروج مزرعه
    public class FruitExitPermitItem
    {
        public int Id { get; set; }

        [Required]
        public int FruitExitPermitId { get; set; }
        [ForeignKey(nameof(FruitExitPermitId))]
        public FruitExitPermit FruitExitPermit { get; set; } = null!;

        [Required]
        [Display(Name = "رقم")]
        public int VarietyId { get; set; }
        [ForeignKey(nameof(VarietyId))]
        public ProductVariety Variety { get; set; } = null!;

        [Range(1, int.MaxValue, ErrorMessage = "تعداد سبد باید بیشتر از صفر باشد")]
        [Display(Name = "تعداد سبد")]
        public int BasketCount { get; set; }

        [StringLength(50)]
        [Display(Name = "نوع سبد")]
        public string? BasketType { get; set; }

        [StringLength(50)]
        [Display(Name = "روش برداشت")]
        public string? HarvestMethod { get; set; }

        public List<BasculTicket> BasculTickets { get; set; } = new();
    }
}
