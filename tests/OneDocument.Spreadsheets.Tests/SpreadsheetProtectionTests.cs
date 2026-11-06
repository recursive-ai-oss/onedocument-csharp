using System.Text;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Protection;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public sealed class SpreadsheetProtectionTests
{
    private static string Part(byte[] file, string name)
    {
        using OpcPackage p = OpcPackage.Open(new MemoryStream(file));
        return Encoding.UTF8.GetString(p.FindPart(name)!.GetBytes().Span);
    }

    [Fact]
    public void TheSixteenBitHashMatchesTheKnownValue()
    {
        // "password" is stored as 83AF by Excel's sheet protection
        Assert.Equal("83AF", PasswordVerifier.FormatExcelHash(PasswordVerifier.ExcelHash("password")));
        Assert.Equal(PasswordCheck.Match, PasswordVerifier.CheckExcelHash("password", "83af"));
        Assert.Equal(PasswordCheck.Mismatch, PasswordVerifier.CheckExcelHash("Password", "83AF"));
    }

    [Fact]
    public void SheetProtectionRoundTripsWithAllowedActions()
    {
        using Workbook wb = TestBook.Open();
        Worksheet ws = wb.Sheets["Data"];
        Assert.False(ws.Protection.IsProtected);
        SheetActions allowed = SheetActions.FormatCells | SheetActions.InsertRows | SheetActions.Sort | SheetActions.EditObjects | SheetActions.SelectLockedCells;
        ws.Protection.Protect(allowed, "pw", new ProtectionOptions { SpinCount = 300, Algorithm = ProtectionHash.Sha256 });
        byte[] file = TestBook.Save(wb);
        string xml = Part(file, "/xl/worksheets/sheet1.xml");
        Assert.Contains("algorithmName=\"SHA-256\"", xml);
        Assert.Contains("spinCount=\"300\"", xml);
        Assert.Contains(" sheet=\"1\"", xml);
        Assert.Contains("formatCells=\"0\"", xml);
        Assert.Contains("insertRows=\"0\"", xml);
        Assert.Contains("selectUnlockedCells=\"1\"", xml);
        // an action that is locked, as the schema defaults say, is not written at all
        Assert.DoesNotContain("formatColumns", xml);
        Assert.DoesNotContain("objects=\"1\"", xml);
        // schema order: after sheetData, before mergeCells
        Assert.True(xml.IndexOf("</sheetData>", StringComparison.Ordinal) < xml.IndexOf("<sheetProtection", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<sheetProtection", StringComparison.Ordinal) < xml.IndexOf("<mergeCells", StringComparison.Ordinal));

        using Workbook back = TestBook.Open(file);
        SheetProtection p = back.Sheets["Data"].Protection;
        Assert.True(p.IsProtected);
        Assert.Equal(allowed, p.Allowed);
        Assert.True(p.HasPassword);
        Assert.Equal(PasswordCheck.Match, p.Verify("pw"));
        Assert.Equal(PasswordCheck.Mismatch, p.Verify("PW"));
        Assert.Equal(PasswordCheck.Mismatch, p.TryRemove("PW"));
        Assert.True(p.IsProtected);
        Assert.Equal(PasswordCheck.Match, p.TryRemove("pw"));
        Assert.False(p.IsProtected);
    }

    [Fact]
    public void DefaultProtectionAllowsSelectionOnly()
    {
        using Workbook wb = TestBook.Open();
        wb.Sheets["Data"].Protection.Protect();
        Assert.Equal(SheetActions.SelectLockedCells | SheetActions.SelectUnlockedCells, wb.Sheets["Data"].Protection.Allowed);
        Assert.False(wb.Sheets["Data"].Protection.HasPassword);
        Assert.Equal(PasswordCheck.Match, wb.Sheets["Data"].Protection.Verify("x"));
        wb.Sheets["Data"].Protection.Protect(SheetActions.None);
        Assert.Contains("selectLockedCells=\"1\"", wb.Sheets["Data"].LayoutMarkup.OuterXml);
        Assert.Equal(SheetActions.None, wb.Sheets["Data"].Protection.Allowed);
    }

    [Fact]
    public void LegacyHashIsReadAndVerified()
    {
        string sheet = "<worksheet xmlns=\"" + TestBook.Main + "\"><sheetData/><sheetProtection password=\"83AF\" sheet=\"1\" objects=\"1\" scenarios=\"1\"/></worksheet>";
        using Workbook wb = TestBook.Open(TestBook.Bytes(sheet1: sheet));
        SheetProtection p = wb.Sheets["Data"].Protection;
        Assert.True(p.IsProtected);
        Assert.True(p.HasPassword);
        Assert.Null(p.Verifier);
        Assert.Equal(PasswordCheck.Match, p.Verify("password"));
        Assert.Equal(PasswordCheck.Mismatch, p.Verify("passwor"));
        Assert.Equal(SheetActions.SelectLockedCells | SheetActions.SelectUnlockedCells, p.Allowed);
    }

    [Fact]
    public void ProtectedRangesAreAddedListedVerifiedAndRemoved()
    {
        using Workbook wb = TestBook.Open();
        SheetProtection p = wb.Sheets["Data"].Protection;
        p.Protect();
        p.AddProtectedRange("Inputs", "A1:B2 D4", "range-pw", new ProtectionOptions { SpinCount = 10 });
        p.AddProtectedRange("Open", "C1");
        Assert.Throws<ArgumentException>(() => p.AddProtectedRange("Open", "C2"));
        Assert.Throws<ArgumentException>(() => p.AddProtectedRange("Bad", "not a ref"));
        IReadOnlyList<ProtectedRange> list = p.ProtectedRanges();
        Assert.Equal(["Inputs", "Open"], list.Select(r => r.Name).ToArray());
        Assert.Equal("A1:B2 D4", list[0].Ranges);
        Assert.True(list[0].HasPassword);
        Assert.False(list[1].HasPassword);
        Assert.Equal(PasswordCheck.Match, p.VerifyRange("Inputs", "range-pw"));
        Assert.Equal(PasswordCheck.Mismatch, p.VerifyRange("Inputs", "x"));
        byte[] file = TestBook.Save(wb);
        string xml = Part(file, "/xl/worksheets/sheet1.xml");
        Assert.True(xml.IndexOf("<sheetProtection", StringComparison.Ordinal) < xml.IndexOf("<protectedRanges>", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<protectedRanges>", StringComparison.Ordinal) < xml.IndexOf("<mergeCells", StringComparison.Ordinal));
        using Workbook back = TestBook.Open(file);
        Assert.Equal(2, back.Sheets["Data"].Protection.ProtectedRanges().Count);
        Assert.True(back.Sheets["Data"].Protection.RemoveProtectedRange("Inputs"));
        Assert.True(back.Sheets["Data"].Protection.RemoveProtectedRange("Open"));
        Assert.DoesNotContain("protectedRanges", back.Sheets["Data"].LayoutMarkup.OuterXml);
    }

    [Fact]
    public void WorkbookStructureProtectionAndFileSharing()
    {
        using Workbook wb = TestBook.Open();
        Assert.False(wb.Protection.LockStructure);
        wb.Protection.Protect(lockStructure: true, lockWindows: true, "wb", new ProtectionOptions { SpinCount = 50 });
        wb.FileSharing.Set("reserve", readOnlyRecommended: true, userName: "Ann", new ProtectionOptions { SpinCount = 60, Algorithm = ProtectionHash.Sha1 });
        byte[] file = TestBook.Save(wb);
        string xml = Part(file, "/xl/workbook.xml");
        int fileSharing = xml.IndexOf("<fileSharing", StringComparison.Ordinal);
        int protection = xml.IndexOf("<workbookProtection", StringComparison.Ordinal);
        int views = xml.IndexOf("<bookViews", StringComparison.Ordinal);
        Assert.True(fileSharing >= 0 && fileSharing < protection && protection < views);
        using Workbook back = TestBook.Open(file);
        Assert.True(back.Protection.LockStructure);
        Assert.True(back.Protection.LockWindows);
        Assert.True(back.Protection.HasPassword);
        Assert.Equal(PasswordCheck.Match, back.Protection.Verify("wb"));
        Assert.Equal(PasswordCheck.Mismatch, back.Protection.Verify("wB"));
        Assert.True(back.FileSharing.ReadOnlyRecommended);
        Assert.Equal("Ann", back.FileSharing.UserName);
        Assert.Equal(PasswordCheck.Match, back.FileSharing.Verify("reserve"));
        Assert.Equal(PasswordCheck.Mismatch, back.FileSharing.Verify("x"));
        Assert.Equal(PasswordCheck.Match, back.Protection.TryRemove("wb"));
        Assert.False(back.Protection.LockStructure);
        Assert.True(back.FileSharing.Remove());
        Assert.False(back.FileSharing.IsPresent);
    }

    [Fact]
    public void LegacyWorkbookAndReservationHashesAndRevisionsPasswordSurvive()
    {
        string book = "<workbook xmlns=\"" + TestBook.Main + "\" xmlns:r=\"" + TestBook.R + "\"><fileSharing reservationPassword=\"83AF\"/><workbookProtection workbookPassword=\"83AF\" lockStructure=\"1\" revisionsPassword=\"CC3D\" lockRevision=\"1\"/><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes(book));
        p.SetRelationships("/xl/workbook.xml", [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml")]);
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Encoding.UTF8.GetBytes("<worksheet xmlns=\"" + TestBook.Main + "\"><sheetData/></worksheet>"));
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        using Workbook wb = TestBook.Open(ms.ToArray());
        Assert.Equal(PasswordCheck.Match, wb.Protection.Verify("password"));
        Assert.Equal(PasswordCheck.Match, wb.FileSharing.Verify("password"));
        Assert.True(wb.Protection.HasRevisionsPassword);
        wb.Protection.Protect(true, false, "new", new ProtectionOptions { SpinCount = 5 });
        string xml = Part(TestBook.Save(wb), "/xl/workbook.xml");
        Assert.Contains("revisionsPassword=\"CC3D\"", xml);
        Assert.DoesNotContain("workbookPassword=", xml);
        Assert.Equal(PasswordCheck.Match, wb.Protection.Verify("new"));
    }
}
