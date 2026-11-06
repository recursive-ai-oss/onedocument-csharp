using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>Worksheet features that cross between SpreadsheetML and the binary form: written to records, read back, and written as markup again.</summary>
public class XlsbFeatureTests
{
    private const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"" + TestBook.Main + "\" xmlns:r=\"" + TestBook.R + "\">";

    /// <summary>The markup of sheet 1 after SpreadsheetML → binary → SpreadsheetML, and the markup the binary workbook offers to the hub.</summary>
    private static (string Hub, string Back, LossReport Loss) RoundTrip(string sheetBody)
    {
        string xml = Head + "<sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData>" + sheetBody + "</worksheet>";
        var loss = new LossReport();
        byte[] bin;
        using (Workbook source = TestBook.Open(TestBook.Bytes(xml)))
        {
            using var ms = new MemoryStream();
            source.SaveAs(ms, OfficeFormat.Xlsb, loss);
            bin = ms.ToArray();
        }

        using Workbook wb = Workbook.Open(new MemoryStream(bin));
        string hub = Encoding.UTF8.GetString(wb.Package.FindPart("/xl/worksheets/sheet1.xml")!.GetBytes().Span);
        using var xlsx = new MemoryStream();
        wb.SaveAs(xlsx, OfficeFormat.Xlsx, new LossReport());
        using Workbook back = Workbook.Open(new MemoryStream(xlsx.ToArray()));
        return (hub, Encoding.UTF8.GetString(back.Package.FindPart("/xl/worksheets/sheet1.xml")!.GetBytes().Span), loss);
    }

    [Fact]
    public void DataValidationsSurviveTheBinaryForm()
    {
        const string dv = "<dataValidations count=\"3\" disablePrompts=\"1\">"
            + "<dataValidation type=\"list\" allowBlank=\"1\" showErrorMessage=\"1\" errorTitle=\"Bad\" error=\"Pick one\" sqref=\"A1:A5 C1\"><formula1>\"a,b,c\"</formula1></dataValidation>"
            + "<dataValidation type=\"whole\" operator=\"between\" errorStyle=\"warning\" showInputMessage=\"1\" promptTitle=\"P\" prompt=\"1 to 9\" sqref=\"B2:B9\"><formula1>1</formula1><formula2>9</formula2></dataValidation>"
            + "<dataValidation type=\"custom\" sqref=\"D1\"><formula1>AND(D1&gt;0,ISNUMBER(D1))</formula1></dataValidation>"
            + "</dataValidations>";
        (string hub, string back, LossReport loss) = RoundTrip(dv);
        Assert.DoesNotContain("xb:r", hub);
        Assert.DoesNotContain(loss.Items, i => i.Code == "xlsb.sheet-element");
        foreach (string xml in new[] { hub, back })
        {
            Assert.Contains("<dataValidations count=\"3\" disablePrompts=\"1\">", xml);
            Assert.Contains("type=\"list\"", xml);
            Assert.Contains("<formula1>\"a,b,c\"</formula1>", xml);
            Assert.Contains("sqref=\"A1:A5 C1\"", xml);
            Assert.Contains("errorStyle=\"warning\"", xml);
            Assert.Contains("<formula2>9</formula2>", xml);
            Assert.Contains("<formula1>AND(D1&gt;0,ISNUMBER(D1))</formula1>", xml);
            Assert.Contains("errorTitle=\"Bad\"", xml);
            Assert.Contains("prompt=\"1 to 9\"", xml);
        }
    }

    [Fact]
    public void SheetProtectionSurvivesTheBinaryForm()
    {
        const string prot = "<sheetProtection password=\"CC1A\" sheet=\"1\" objects=\"1\" scenarios=\"0\" formatCells=\"0\" formatColumns=\"1\" formatRows=\"1\" insertColumns=\"1\" insertRows=\"1\" insertHyperlinks=\"1\" deleteColumns=\"1\" deleteRows=\"1\" selectLockedCells=\"0\" sort=\"0\" autoFilter=\"1\" pivotTables=\"1\" selectUnlockedCells=\"1\"/>";
        (string hub, string back, _) = RoundTrip(prot);
        Assert.DoesNotContain("xb:r", hub);
        foreach (string xml in new[] { hub, back })
        {
            Assert.Contains("password=\"CC1A\"", xml);
            Assert.Contains("sheet=\"1\"", xml);
            Assert.Contains("objects=\"1\"", xml);
            Assert.Contains("scenarios=\"0\"", xml);
            Assert.Contains("formatCells=\"0\"", xml);
            Assert.Contains("formatRows=\"1\"", xml);
            Assert.Contains("sort=\"0\"", xml);
            Assert.Contains("selectUnlockedCells=\"1\"", xml);
        }
    }
}
