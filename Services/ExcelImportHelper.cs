using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;

namespace FruitBilling.Web.Services
{
    // گزارش ورود اکسل برای هر تب
    public class SheetImportReport
    {
        public string SheetName { get; set; } = "";
        public int? CycleId { get; set; }
        public string? CycleName { get; set; }
        public int Permits { get; set; }
        public int Tickets { get; set; }
        public int Duplicates { get; set; }
        public int HiddenErrorCount { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> Notes { get; } = new();
        public Dictionary<int, List<string>> RowErrors { get; } = new();
        public Dictionary<int, List<string>> RowNotes { get; } = new();
        public bool HasErrors => Errors.Count > 0 || HiddenErrorCount > 0;

        public static IEnumerable<int> ExtractRowNumbers(string message)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(
                message ?? "", @"ردیف(?:‌های|های)?\s*([0-9۰-۹]+(?:\s*[،,و]\s*[0-9۰-۹]+)*)");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                foreach (var token in System.Text.RegularExpressions.Regex.Split(match.Groups[1].Value, @"\s*[،,و]\s*"))
                {
                    var digits = ExcelImportHelper.ToLatinDigits(token.Trim());
                    if (int.TryParse(digits, out var row) && row >= 2)
                        yield return row;
                }
            }
        }

        public void AddError(string message)
        {
            foreach (var row in ExtractRowNumbers(message))
            {
                if (!RowErrors.TryGetValue(row, out var messages))
                    RowErrors[row] = messages = new List<string>();
                if (messages.Count < 20 && !messages.Contains(message))
                    messages.Add(message);
            }

            if (Errors.Count < 100) Errors.Add(message);
            else HiddenErrorCount++;
        }

        public void AddNote(string message)
        {
            foreach (var row in ExtractRowNumbers(message))
            {
                if (!RowNotes.TryGetValue(row, out var messages))
                    RowNotes[row] = messages = new List<string>();
                if (messages.Count < 20 && !messages.Contains(message))
                    messages.Add(message);
            }

            if (Notes.Count < 50) Notes.Add(message);
        }
    }

    // قواعد مشترک ورود اکسل (سورتینگ و برگ خروج از مزرعه)
    public static class ExcelImportHelper
    {
        public static string ToLatinDigits(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
                else if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        // یکسان‌سازی برای مقایسه: ی/ک عربی و فارسی، ارقام، فاصله و نیم‌فاصله، حروف بزرگ و کوچک
        public static string Norm(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch0 in ToLatinDigits(s))
            {
                char ch = ch0 == '\u064A' ? '\u06CC' : ch0 == '\u0643' ? '\u06A9' : ch0;
                if (ch == '\u200C' || ch == '\u200E' || ch == '\u200F' || char.IsWhiteSpace(ch)) continue;
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        // نام تب اکسل حداکثر ۳۱ کاراکتر است و نباید شامل  : \ / ? * [ ]  باشد
        public static bool IsValidSheetName(string? name)
            => !string.IsNullOrWhiteSpace(name)
               && name.Trim().Length <= 31
               && name.IndexOfAny(new[] { ':', '\\', '/', '?', '*', '[', ']' }) < 0;

        // متن: فقط رشته یا عدد صحیح پذیرفته می‌شود (تاریخ/بولی/خطا و ... رد می‌شود)
        public static bool TryReadText(IXLCell cell, string title, int maxLen, int rowNum,
            SheetImportReport rep, bool required, out string value)
        {
            value = "";
            switch (cell.DataType)
            {
                case XLDataType.Blank:
                    break;
                case XLDataType.Text:
                    value = cell.GetString().Trim();
                    break;
                case XLDataType.Number:
                    {
                        var d = cell.GetDouble();
                        if (d != Math.Floor(d) || Math.Abs(d) > 1e15)
                        {
                            rep.AddError($"ردیف {rowNum}، ستون «{title}»: مقدار باید متن یا عدد صحیح باشد.");
                            return false;
                        }
                        value = ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    }
                default:
                    rep.AddError($"ردیف {rowNum}، ستون «{title}»: نوع مقدار مجاز نیست (فقط متن یا عدد پذیرفته می‌شود).");
                    return false;
            }

            if (value.Length == 0)
            {
                if (required)
                {
                    rep.AddError($"ردیف {rowNum}، ستون «{title}»: وارد کردن مقدار الزامی است.");
                    return false;
                }
                return true;
            }

            if (value.Length > maxLen)
            {
                rep.AddError($"ردیف {rowNum}، ستون «{title}»: حداکثر {maxLen} کاراکتر مجاز است.");
                return false;
            }
            return true;
        }

        // عدد صحیح غیرمنفی: سلول عددی، یا متنی که فقط ارقام (فارسی یا لاتین) باشد
        public static bool TryReadInt(IXLCell cell, string title, int rowNum,
            SheetImportReport rep, out int? value)
        {
            value = null;
            switch (cell.DataType)
            {
                case XLDataType.Blank:
                    return true;
                case XLDataType.Number:
                    {
                        var d = cell.GetDouble();
                        if (d < 0 || d != Math.Floor(d) || d > int.MaxValue)
                        {
                            rep.AddError($"ردیف {rowNum}، ستون «{title}»: مقدار باید یک عدد صحیح غیرمنفی باشد.");
                            return false;
                        }
                        value = (int)d;
                        return true;
                    }
                case XLDataType.Text:
                    {
                        var s = ToLatinDigits(cell.GetString().Trim());
                        if (s.Length == 0) return true;
                        if (int.TryParse(s, System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out var n))
                        {
                            value = n;
                            return true;
                        }
                        rep.AddError($"ردیف {rowNum}، ستون «{title}»: مقدار باید عدد باشد (مقدار فایل: «{s}»).");
                        return false;
                    }
                default:
                    rep.AddError($"ردیف {rowNum}، ستون «{title}»: مقدار باید عدد باشد.");
                    return false;
            }
        }

        // تاریخ شمسی به‌صورت متن: yyyy/MM/dd
        public static bool TryReadDate(IXLCell cell, int rowNum, SheetImportReport rep, out string value)
        {
            value = "";
            if (cell.DataType == XLDataType.DateTime)
            {
                rep.AddError($"ردیف {rowNum}، ستون «تاریخ»: تاریخ باید به‌صورت متن شمسی مثل 1403/05/12 باشد، نه تاریخ میلادی اکسل.");
                return false;
            }
            if (!TryReadText(cell, "تاریخ", 10, rowNum, rep, required: false, out var raw)) return false;
            if (raw.Length == 0) return true;

            var s = ToLatinDigits(raw);
            var parts = s.Split('/');
            if (parts.Length == 3
                && parts[0].Length == 4 && parts[1].Length == 2 && parts[2].Length == 2
                && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var d)
                && y >= 1300 && y <= 1500 && m >= 1 && m <= 12 && d >= 1 && d <= (m <= 6 ? 31 : 30))
            {
                value = s;
                return true;
            }

            rep.AddError($"ردیف {rowNum}، ستون «تاریخ»: قالب تاریخ باید yyyy/MM/dd شمسی باشد، مثل 1403/05/12 (مقدار فایل: «{raw}»).");
            return false;
        }

        // نام تب امن برای خروجی اکسل: نویسه‌های ممنوع حذف، حداکثر ۳۱ کاراکتر، یکتا
        public static string SafeSheetName(string? raw, HashSet<string> usedNames)
        {
            var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            var name = new string((raw ?? "").Select(ch => invalid.Contains(ch) ? ' ' : ch).ToArray()).Trim().Trim('\'').Trim();
            if (name.Length == 0) name = "Sheet";
            if (name.Length > 31) name = name.Substring(0, 31).TrimEnd();

            var candidate = name;
            int n = 2;
            while (!usedNames.Add(candidate))
            {
                var suffix = $" ({n++})";
                candidate = name.Substring(0, Math.Min(name.Length, 31 - suffix.Length)) + suffix;
            }
            return candidate;
        }

        // ساخت نسخه علامت‌گذاری‌شده از همان فایل اصلی؛ هیچ تغییری در داده‌های ورودی انجام نمی‌شود.
        public static byte[] BuildAnnotatedWorkbook(XLWorkbook workbook, IReadOnlyCollection<SheetImportReport> reports)
        {
            foreach (var report in reports)
            {
                var ws = workbook.Worksheets.FirstOrDefault(x =>
                    string.Equals(x.Name, report.SheetName, StringComparison.OrdinalIgnoreCase));
                if (ws == null) continue;

                int lastColumn = Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 0, 1);
                int messageColumn = lastColumn + 1;
                ws.Cell(1, messageColumn).Value = "گزارش بررسی";
                ws.Cell(1, messageColumn).Style.Font.Bold = true;
                ws.Cell(1, messageColumn).Style.Fill.BackgroundColor = XLColor.LightGray;
                ws.Column(messageColumn).Width = 55;

                foreach (var entry in report.RowErrors)
                {
                    if (entry.Key > (ws.LastRowUsed()?.RowNumber() ?? 0)) continue;
                    var row = ws.Row(entry.Key);
                    row.Style.Fill.BackgroundColor = XLColor.LightPink;
                    row.Style.Font.FontColor = XLColor.DarkRed;
                    ws.Cell(entry.Key, messageColumn).Value = string.Join(" | ", entry.Value);
                    ws.Cell(entry.Key, messageColumn).Style.Alignment.WrapText = true;
                }

                foreach (var entry in report.RowNotes)
                {
                    if (report.RowErrors.ContainsKey(entry.Key) ||
                        entry.Key > (ws.LastRowUsed()?.RowNumber() ?? 0)) continue;
                    ws.Row(entry.Key).Style.Fill.BackgroundColor = XLColor.LightYellow;
                    ws.Cell(entry.Key, messageColumn).Value = string.Join(" | ", entry.Value);
                    ws.Cell(entry.Key, messageColumn).Style.Alignment.WrapText = true;
                }

                ws.SheetView.FreezeRows(1);
            }

            var usedSheetNames = workbook.Worksheets.Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var summary = workbook.Worksheets.Add(SafeSheetName("گزارش بررسی", usedSheetNames));
            summary.RightToLeft = true;
            summary.Cell(1, 1).Value = "نام تب";
            summary.Cell(1, 2).Value = "شماره ردیف";
            summary.Cell(1, 3).Value = "نوع";
            summary.Cell(1, 4).Value = "شرح";
            summary.Range(1, 1, 1, 4).Style.Font.Bold = true;
            summary.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.LightGray;

            int targetRow = 2;
            foreach (var report in reports)
            {
                foreach (var error in report.Errors)
                {
                    var rowNumber = SheetImportReport.ExtractRowNumbers(error).FirstOrDefault();
                    summary.Cell(targetRow, 1).Value = report.SheetName;
                    summary.Cell(targetRow, 2).Value = rowNumber == 0 ? "—" : rowNumber.ToString();
                    summary.Cell(targetRow, 3).Value = "خطا";
                    summary.Cell(targetRow, 4).Value = error;
                    summary.Row(targetRow).Style.Fill.BackgroundColor = XLColor.LightPink;
                    targetRow++;
                }
                foreach (var note in report.Notes)
                {
                    var rowNumber = SheetImportReport.ExtractRowNumbers(note).FirstOrDefault();
                    summary.Cell(targetRow, 1).Value = report.SheetName;
                    summary.Cell(targetRow, 2).Value = rowNumber == 0 ? "—" : rowNumber.ToString();
                    summary.Cell(targetRow, 3).Value = "هشدار";
                    summary.Cell(targetRow, 4).Value = note;
                    summary.Row(targetRow).Style.Fill.BackgroundColor = XLColor.LightYellow;
                    targetRow++;
                }
            }

            summary.Column(1).Width = 28;
            summary.Column(2).Width = 14;
            summary.Column(3).Width = 14;
            summary.Column(4).Width = 85;
            summary.Column(4).Style.Alignment.WrapText = true;
            summary.SheetView.FreezeRows(1);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // بررسی فایل آپلودی (فقط .xlsx، حداکثر ۲۰ مگابایت، امضای zip) و باز کردن آن
        public static async Task<(XLWorkbook? Workbook, string? Error)> OpenWorkbookAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return (null, "فایل انتخاب نشده است.");
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                return (null, "فقط فایل با فرمت .xlsx مجاز است.");
            if (file.Length > 20 * 1024 * 1024)
                return (null, "حجم فایل نباید بیشتر از ۲۰ مگابایت باشد.");

            var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            // فایل xlsx یک zip است و باید با PK شروع شود
            var bytes = ms.ToArray();
            if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
                return (null, "فایل یک xlsx معتبر نیست.");

            ms.Position = 0;
            try
            {
                return (new XLWorkbook(ms), null);
            }
            catch
            {
                return (null, "فایل قابل خواندن نیست یا یک xlsx معتبر نیست.");
            }
        }

        // قالب اکسل: یک تب به‌ازای هر نام دوره (نام تب = نام دوره). اگر هیچ نام معتبری نباشد null
        public static byte[]? BuildTemplate(IEnumerable<string> cycleNames, string[] headers, int[] numericColumns)
        {
            using var wb = new XLWorkbook();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in cycleNames)
            {
                var sheetName = (raw ?? "").Trim();
                if (!IsValidSheetName(sheetName) || !usedNames.Add(sheetName)) continue;

                var ws = wb.Worksheets.Add(sheetName);
                ws.RightToLeft = true;

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(1, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                    ws.Column(i + 1).Width = 18;
                }

                // ستون‌های عددی عدد، بقیه متن (تا تاریخ شمسی و شماره‌ها تغییر نکنند)
                for (int col = 1; col <= headers.Length; col++)
                    ws.Column(col).Style.NumberFormat.Format = numericColumns.Contains(col) ? "0" : "@";

                ws.SheetView.FreezeRows(1);
            }

            if (wb.Worksheets.Count == 0) return null;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }
}
