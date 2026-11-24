using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace FruitBilling.Web.Services
{
    public interface IPermissionService
    {
        Task<bool> CanViewFieldAsync(string userId, string resource, string field, object? record = null);
        Task<bool> CanEditFieldAsync(string userId, string resource, string field, object? record = null, object? newValue = null);
        Task<bool> CanApproveAsync(string userId, string resource);
        Task<Dictionary<string, bool>> GetEditableFieldsAsync(string userId, string resource, object record);
        Task<IQueryable<T>> FilterQueryAsync<T>(IQueryable<T> query, string userId, string resource) where T : class;
        Task<bool> CanUserImportAsync(string userId, string resource);
    }

    public class PermissionService : IPermissionService
    {
        private readonly AppDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public PermissionService(AppDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // ==========================================
        // 🆕 آیا کاربر اجازه تأیید رکورد را دارد؟
        // ==========================================
        public async Task<bool> CanApproveAsync(string userId, string resource)
        {
            // مدیر همیشه دسترسی تأیید دارد (پشتیبان)
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Contains("Manager")) return true;
            }

            // بررسی دسترسی از جدول RolePermissions
            var access = await GetAccessLevelAsync(userId, resource, "Action_Approve");
            return access == "Edit"; // اگر سطح دسترسی Edit باشد، یعنی مجاز به تأیید است
        }
        public async Task<bool> CanViewFieldAsync(string userId, string resource, string field, object? record = null)
        {
            var access = await GetAccessLevelAsync(userId, resource, field);
            if (access == "None") return false;

            if (record != null)
            {
                var conditions = await GetRowConditionsAsync(userId, resource, field);
                if (conditions.Any() && !EvaluateConditions(conditions, record)) return false;
            }
            return true;
        }

        public async Task<bool> CanEditFieldAsync(string userId, string resource, string field, object? record = null, object? newValue = null)
        {
            var access = await GetAccessLevelAsync(userId, resource, field);
            if (access != "Edit") return false;

            if (record != null)
            {
                var conditions = await GetRowConditionsAsync(userId, resource, field);
                if (conditions.Any() && !EvaluateConditions(conditions, record)) return false;

                if (newValue != null && !EvaluateConditionsWithNewValue(conditions, record, field, newValue)) return false;

                if (await IsFieldLockedAsync(resource, field, record)) return false;
            }
            return true;
        }

        public async Task<Dictionary<string, bool>> GetEditableFieldsAsync(string userId, string resource, object record)
        {
            var result = new Dictionary<string, bool>();
            var fields = resource switch
            {
                "ExitPermits" => new[] {
                    "PermitNumber", "Date", "FarmId", "PlotId", "ProductType", "HarvestMethod",
                    "BasketCount", "BasketType", "Destination", "VehiclePlate", "DriverName", "ContractorName", "Description",
                    "TicketNo_Mزرعه", "EmptyWeight_Mزرعه", "GrossWeight_Mزرعه", "NetWeight_Mزرعه",
                    "Action_Approve" // 🆕
                },
                "SortingExitPermits" => new[] {
                    "PermitNumber", "Date", "BasketCount", "ProductType", "Grade", "FarmName",
                    "VehiclePlate", "Destination", "SaleType", "Source", "SortingContractor", "MovementType", "Description",
                    "TicketNo_Sort", "BasketCount_Sort", "EmptyWeight_Sort", "GrossWeight_Sort",
                    "Action_Approve" // وزن خالص فقط محاسبه می‌شود
                },
                _ => Array.Empty<string>()
            };

            foreach (var field in fields)
            {
                result[field] = await CanEditFieldAsync(userId, resource, field, record);
            }
            return result;
        }

        public async Task<IQueryable<T>> FilterQueryAsync<T>(IQueryable<T> query, string userId, string resource) where T : class
        {
            return query; // در صورت نیاز به فیلتر سطرها، اینجا پیاده‌سازی کنید
        }

        private async Task<string> GetAccessLevelAsync(string userId, string resource, string field)
        {
            var roleId = await GetUserRoleIdAsync(userId);
            if (roleId == null) return "None";
            var perm = await _db.RolePermissions.FirstOrDefaultAsync(p => p.RoleId == roleId && p.ResourceName == resource && p.FieldName == field);
            return perm?.AccessLevel ?? "None";
        }

        private async Task<string?> GetUserRoleIdAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return null;
            var roles = await _userManager.GetRolesAsync(user);
            if (!roles.Any()) return null;
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roles.First());
            return role?.Id;
        }

        private async Task<List<RowCondition>> GetRowConditionsAsync(string userId, string resource, string field)
        {
            var roleId = await GetUserRoleIdAsync(userId);
            if (roleId == null) return new List<RowCondition>();
            var perm = await _db.RolePermissions.Include(p => p.RowConditions).FirstOrDefaultAsync(p => p.RoleId == roleId && p.ResourceName == resource && p.FieldName == field);
            return perm?.RowConditions ?? new List<RowCondition>();
        }

        public async Task<bool> CanUserImportAsync(string userId, string resource)
        {
            if (string.IsNullOrEmpty(userId)) return false;

            // مدیر همیشه مجاز است
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Manager")) return true;

            // بررسی مجوز اختصاصی کاربر
            return await _db.UserImportPermissions
                .AnyAsync(p => p.UserId == userId && p.ResourceName == resource && p.CanImport);
        }

        private bool EvaluateConditions(List<RowCondition> conditions, object record)
        {
            var result = true;
            string currentLogic = "AND";
            foreach (var cond in conditions)
            {
                var value = GetPropertyValue(record, cond.FieldName);
                var condResult = EvaluateCondition(value, cond.Operator, cond.Value);
                result = currentLogic == "AND" ? (result && condResult) : (result || condResult);
                currentLogic = cond.LogicType;
            }
            return result;
        }

        private bool EvaluateConditionsWithNewValue(List<RowCondition> conditions, object record, string field, object newValue)
        {
            var recordType = record.GetType();
            var newRecord = Activator.CreateInstance(recordType)!;
            foreach (var prop in recordType.GetProperties())
            {
                var val = prop.Name == field ? newValue : prop.GetValue(record);
                prop.SetValue(newRecord, val);
            }
            return EvaluateConditions(conditions, newRecord);
        }

        private bool EvaluateCondition(object? value, string op, string? condValue)
        {
            var strValue = value?.ToString() ?? "";
            var strCond = condValue ?? "";
            switch (op)
            {
                case "Equals": return strValue == strCond;
                case "NotEquals": return strValue != strCond;
                case "Contains": return strValue.Contains(strCond);
                case "In": return strCond.Split(',').Select(s => s.Trim()).Contains(strValue);
                case "NotIn": return !strCond.Split(',').Select(s => s.Trim()).Contains(strValue);
                case "IsNull": return value == null;
                case "IsNotNull": return value != null;
                default: return true;
            }
        }

        private async Task<bool> IsFieldLockedAsync(string resource, string field, object record)
        {
            var locks = await _db.FieldLocks.Where(l => l.ResourceName == resource && l.FieldName == field && l.IsActive).ToListAsync();
            foreach (var lockItem in locks)
                if (EvaluateConditionString(lockItem.LockCondition, record)) return true;
            return false;
        }

        private bool EvaluateConditionString(string condition, object record)
        {
            var parts = condition.Split('=', 2);
            if (parts.Length != 2) return false;
            var actual = GetPropertyValue(record, parts[0].Trim())?.ToString() ?? "";
            return parts[1].Split(',').Select(s => s.Trim()).Contains(actual);
        }

        private object? GetPropertyValue(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return prop?.GetValue(obj);
        }
    }
}