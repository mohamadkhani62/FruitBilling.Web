using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class SortingBasculTicket
    {
        public int Id { get; set; }

        // FK قدیمی تا زمان انتقال کامل فرم‌ها و داده‌ها حفظ می‌شود.
        public int SortingExitPermitId { get; set; }
        [ForeignKey("SortingExitPermitId")]
        public SortingExitPermit? SortingExitPermit { get; set; }

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