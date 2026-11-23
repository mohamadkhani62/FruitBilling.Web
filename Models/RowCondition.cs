using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FruitBilling.Web.Models
{
    public class RowCondition
    {
        public int Id { get; set; }

        public int RolePermissionId { get; set; }
        [ForeignKey("RolePermissionId")]
        public RolePermission? RolePermission { get; set; }

        // نام فیلد شرط (مثلاً "Destination" یا "Status")
        [Required]
        [StringLength(100)]
        public string FieldName { get; set; } = "";

        // عملگر: "Equals", "NotEquals", "In", "NotIn", "Contains", "GreaterThan", "LessThan", "IsNull", "IsNotNull"
        [Required]
        [StringLength(30)]
        public string Operator { get; set; } = "";

        // مقدار شرط (مثلاً "بازار,فروش" یا "Confirmed")
        [StringLength(500)]
        public string? Value { get; set; }

        // نوع ترکیب: "AND" یا "OR" (برای ترکیب با شرط‌های دیگر)
        [StringLength(10)]
        public string LogicType { get; set; } = "AND";
    }
}