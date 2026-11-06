namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class HtmlExportTests
{
    [Fact]
    public void Sheets_become_tables_with_displayed_values_merges_and_a_loss_report()
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        s["A1"].SetString("Name <b>");
        s["B1"].SetString("Amount");
        s["A2"].SetString("Rent");
        s["B2"].SetNumber(1234.5);
        s["B2"].SetStyle(new CellStyleSpec { NumberFormatCode = "#,##0.00" });
        s["A3"].SetBoolean(true);
        s["B3"].Formula = "B2*2";
        s.Merge(new CellRange(new CellAddress(4, 1), new CellAddress(5, 2)));
        s["A4"].SetString("merged");
        Worksheet hidden = wb.Sheets.Add("Hidden");
        hidden["A1"].SetString("secret");
        hidden.State = SheetState.Hidden;

        string html = wb.ToHtml();
        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Sheet1</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<td>Name &lt;b&gt;</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"n\">1,234.50</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>TRUE</td>", html, StringComparison.Ordinal);
        Assert.Contains("colspan=\"2\" rowspan=\"2\">merged</td>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", html, StringComparison.Ordinal);
        Assert.Equal(1, wb.ExportLoss!.CountOf("loss.html-hidden-sheets"));
        Assert.Equal(1, wb.ExportLoss.CountOf("loss.formulas"));
        Assert.DoesNotContain("<html", wb.ToHtml(new HtmlExportOptions { Fragment = true }), StringComparison.Ordinal);

        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Html);
        Assert.Equal(html, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }
}
