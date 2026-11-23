using System.ComponentModel.DataAnnotations;

namespace FruitBilling.Web.Models
{
    public class FieldLock
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string ResourceName { get; set; } = "";

        [Required]
        [StringLength(100)]
        public string FieldName { get; set; } = "";

        // شرط قفل: "Status=Confirmed" یا "Status=تأیید شده"
        [Required]
        [StringLength(200)]
        public string LockCondition { get; set; } = "";

        // آیا این قفل فعال است؟
        public bool IsActive { get; set; } = true;
    }
}