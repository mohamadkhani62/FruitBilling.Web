using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    // هر ردیف رقم/درجه در یک برگ خروج سورتینگ
    public class SortingExitPermitItem
    {
        public int Id { get; set; }

        [Required]
        public int SortingExitPermitId { get; set; }
        [ForeignKey(nameof(SortingExitPermitId))]
        public SortingExitPermit SortingExitPermit { get; set; } = null!;

        [Required]
        [Display(Name = "رقم")]
        public int VarietyId { get; set; }
        [ForeignKey(nameof(VarietyId))]
        public ProductVariety Variety { get; set; } = null!;

        [Required]
        [StringLength(50)]
        [Display(Name = "درجه")]
        public string Grade { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "تعداد سبد باید بیشتر از صفر باشد")]
        [Display(Name = "تعداد سبد")]
        public int BasketCount { get; set; }

        public List<SortingBasculTicket> BasculTickets { get; set; } = new();
    }
}
