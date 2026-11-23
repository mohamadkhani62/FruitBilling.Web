using static FruitBilling.Web.Services.ExcelImportHelper;

namespace FruitBilling.Web.Models
{
    // نوع فروش و نوع خروج برگ سورتینگ
    //   نقدی / میدان  ← خروج برای فروش
    //   هدیه          ← خروج هدایا
    //   جابجایی       ← خروج برای سورت یا خروج برای ذخیره‌سازی (جابجایی داخلی)
    public static class SortingMovement
    {
        public const string Cash = "نقدی";
        public const string Market = "میدان";
        public const string Gift = "هدیه";
        public const string Transfer = "جابجایی";

        public static readonly string[] SaleTypes = { Cash, Market, Gift, Transfer };

        public const string ExitForSale = "خروج برای فروش";
        public const string ExitGift = "خروج هدایا";
        public const string ExitForSorting = "خروج برای سورت";
        public const string ExitForStorage = "خروج برای ذخیره‌سازی";

        public static readonly string[] AllExitTypes = { ExitForSale, ExitGift, ExitForSorting, ExitForStorage };

        // مقدارهای نسخه‌ی اول (برای داده‌ی قدیمی)
        private const string LegacyToSorting = "انتقال به سالن سورت";
        private const string LegacyReturn = "برگشت به سردخانه";

        public static string? CanonicalSaleType(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            foreach (var t in SaleTypes)
                if (Norm(t) == Norm(s)) return t;
            return null;
        }

        public static string? CanonicalExitType(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            foreach (var t in AllExitTypes)
                if (Norm(t) == Norm(s)) return t;
            if (Norm(s) == Norm(LegacyToSorting)) return ExitForSorting;
            if (Norm(s) == Norm(LegacyReturn)) return ExitForStorage;
            return null;
        }

        // گزینه‌های مجاز نوع خروج برای یک نوع فروش
        public static string[] AllowedExitTypes(string? saleType)
        {
            switch (CanonicalSaleType(saleType))
            {
                case Cash:
                case Market:
                    return new[] { ExitForSale };
                case Gift:
                    return new[] { ExitGift };
                case Transfer:
                    return new[] { ExitForSorting, ExitForStorage };
                default:
                    return Array.Empty<string>();
            }
        }

        // نوع خروج سازگار با نوع فروش؛ اگر فقط یک گزینه‌ی مجاز باشد خودکار پر می‌شود
        public static string? Resolve(string? saleType, string? requestedExit)
        {
            var allowed = AllowedExitTypes(saleType);
            if (allowed.Length == 0) return null;

            var req = CanonicalExitType(requestedExit);
            if (req != null && allowed.Contains(req)) return req;

            return allowed.Length == 1 ? allowed[0] : null;
        }

        // جابجایی داخلی: در خروجی کل و فروش دوباره حساب نمی‌شود
        public static bool IsInternal(string? saleType, string? exitType)
        {
            if (CanonicalSaleType(saleType) == Transfer) return true;
            var e = CanonicalExitType(exitType);
            return e == ExitForSorting || e == ExitForStorage;
        }

        // برچسب نوع خروج؛ برای داده‌ی قدیمی بدون نوع خروج از روی نوع فروش حدس زده می‌شود
        public static string ExitLabel(string? saleType, string? exitType)
        {
            var e = CanonicalExitType(exitType);
            if (e != null) return e;

            switch (CanonicalSaleType(saleType))
            {
                case Gift:
                    return ExitGift;
                case Transfer:
                    return "جابجایی (نوع خروج نامشخص)";
                default:
                    return ExitForSale;
            }
        }
    }
}
