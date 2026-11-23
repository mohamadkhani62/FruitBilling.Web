using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class RolePermission
    {
        public int Id { get; set; }

        [Required]
        public string RoleId { get; set; } = ""; // 👈 به AspNetRoles متصل می‌شود

        [Required]
        [StringLength(100)]
        public string ResourceName { get; set; } = ""; // مثلاً "ExitPermits"

        [StringLength(100)]
        public string? FieldName { get; set; } // مثلاً "Destination" (اگر null باشد، یعنی کل صفحه)

        // سطح دسترسی: "None", "View", "Edit"
        [Required]
        [StringLength(20)]
        public string AccessLevel { get; set; } = "None";

        // 🆕 لیست شرط‌های سطح سطر (ارتباط یک به چند)
        public List<RowCondition> RowConditions { get; set; } = new();
    }
}