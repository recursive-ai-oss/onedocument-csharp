using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class MarkdownWorkbookTests
{
    [Fact]
    public void SheetsBecomeHeadedPipeTablesWithDisplayedNumbers()
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        s["A1"].SetString("Name");
        s["B1"].SetString("Qty");
        s["A2"].SetString("a|b");
        s["B2"].SetNumber(1.5);
        s["A3"].SetString("two\nlines *x*");
        s["B3"].Formula = "1+1";
        s["B3"].SetNumber(2);
        wb.Sheets.Add("Empty");
        string md = wb.ToMarkdown();
        Assert.Equal(
            "## Sheet1\n\n| Name | Qty |\n| --- | ---: |\n| a\\|b | 1.5 |\n| two<br>lines \\*x\\* | 2 |\n\n## Empty\n\n(empty)\n", md);
        Assert.Equal(1, wb.ExportLoss!.CountOf("loss.formulas"));
    }

    [Fact]
    public void SaveAsMarkdownGoesThroughTheRegistry()
    {
        SpreadsheetFormats.Register(Formats.FormatRegistry.Default);
        using Workbook wb = Workbook.Create();
        wb.Sheets[0]["A1"].SetString("x");
        wb.Sheets[0]["A2"].SetString("y");
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Markdown);
        Assert.StartsWith("## Sheet1", Encoding.UTF8.GetString(ms.ToArray()), StringComparison.Ordinal);
    }
}
