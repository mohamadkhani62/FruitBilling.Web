using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace FruitBilling.Web.Models
{
    public class UserImportPermission
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public IdentityUser? User { get; set; }

        [Required]
        [StringLength(100)]
        public string ResourceName { get; set; } = ""; // "SortingExitPermits" یا "ExitPermits"

        public bool CanImport { get; set; } = true;
    }
}