using ClosedXML.Excel;
using FruitBilling.Web.Services;

namespace FruitBilling.Web.Tests;

public class ExcelImportHelperTests
{
    [Fact]
    public void ExtractRowNumbers_ParsesPersianDigitsAndLists()
    {
        var rows = SheetImportReport.ExtractRowNumbers(
            "برگه نمونه، ردیف‌های ۲، ۳ و ۴: تعداد سبد قبض‌ها نادرست است.");

        Assert.Equal(new[] { 2, 3, 4 }, rows);
    }

    [Fact]
    public void ExtractRowNumbers_ParsesArabicDigitsAndIgnoresHeaderMessages()
    {
        var rows = SheetImportReport.ExtractRowNumbers(
            "ردیف ٢، ستون «وزن پر»: مقدار نامعتبر است.");

        Assert.Equal(new[] { 2 }, rows);
        Assert.Empty(SheetImportReport.ExtractRowNumbers("سربرگ فایل با قالب مورد انتظار مطابقت ندارد."));
    }

    [Fact]
    public void AddError_AssociatesMessageWithEveryMentionedRow()
    {
        var report = new SheetImportReport();
        const string message = "ردیف‌های ۲ و ۵: مجموع سبد قبض‌ها برابر نیست.";

        report.AddError(message);

        Assert.True(report.HasErrors);
        Assert.Contains(message, report.Errors);
        Assert.Contains(message, report.RowErrors[2]);
        Assert.Contains(message, report.RowErrors[5]);
    }

    [Fact]
    public void Norm_NormalizesPersianArabicLettersDigitsAndWhitespace()
    {
        Assert.Equal("یک12", ExcelImportHelper.Norm("ي ك ۱۲"));
    }

    [Fact]
    public void BuildAnnotatedWorkbook_WritesRowErrorAndSummarySheet()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("دوره آزمایشی");
        sheet.Cell(1, 1).Value = "شماره برگ";
        sheet.Cell(1, 2).Value = "تعداد سبد";
        sheet.Cell(2, 1).Value = "۱۰۱";
        sheet.Cell(2, 2).Value = 12;

        var report = new SheetImportReport { SheetName = sheet.Name };
        report.AddError("ردیف ۲، ستون «تعداد سبد»: مقدار باید بیشتر از صفر باشد.");

        var bytes = ExcelImportHelper.BuildAnnotatedWorkbook(
            workbook, new[] { report });

        using var stream = new MemoryStream(bytes);
        using var annotated = new XLWorkbook(stream);
        var annotatedSheet = annotated.Worksheet("دوره آزمایشی");

        Assert.Contains("ردیف ۲", annotatedSheet.Cell(2, 3).GetString());
        var summary = annotated.Worksheet("گزارش بررسی");
        Assert.Equal("خطا", summary.Cell(2, 3).GetString());
        Assert.Contains("تعداد سبد", summary.Cell(2, 4).GetString());
    }
}
