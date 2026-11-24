using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class BasculTicket
    {
        public int Id { get; set; }

        // FK قدیمی تا زمان انتقال کامل فرم‌ها و داده‌ها حفظ می‌شود.
        public int FruitExitPermitId { get; set; }
        [ForeignKey("FruitExitPermitId")]
        public FruitExitPermit? FruitExitPermit { get; set; }

        // در ساختار جدید، هر قبض به یک ردیف محصول/رقم تعلق دارد.
        public int? FruitExitPermitItemId { get; set; }
        [ForeignKey(nameof(FruitExitPermitItemId))]
        public FruitExitPermitItem? FruitExitPermitItem { get; set; }

        [Display(Name = "تعداد سبد قبض")]
        public int? BasketCount { get; set; }

        [Display(Name = "شماره قبض باسکول")]
        [StringLength(30)]
        public string? TicketNo { get; set; }

        [Display(Name = "وزن خالی (kg)")]
        public int EmptyWeight { get; set; }

        [Display(Name = "وزن پر (kg)")]
        public int GrossWeight { get; set; }

        [Display(Name = "وزن خالص (kg)")]
        public int NetWeight { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}