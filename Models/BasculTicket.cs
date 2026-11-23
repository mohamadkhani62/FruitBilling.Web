using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class BasculTicket
    {
        public int Id { get; set; }

        public int FruitExitPermitId { get; set; }
        [ForeignKey("FruitExitPermitId")]
        public FruitExitPermit? FruitExitPermit { get; set; }

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