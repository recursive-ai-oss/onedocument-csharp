using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests.Biff;

/// <summary>Document properties and the embedded object of an Excel binary file written by a spreadsheet program (testdata/ole).</summary>
public sealed class XlsOleTests
{
    private static string Fixture([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../../../testdata/ole/ole-props.xls"));

    private static Workbook Open(DefectCollection? defects = null) =>
        XlsConverter.ToWorkbook(ByteSource.FromMemory(File.ReadAllBytes(Fixture())), new OpenContext(defects: defects));

    private static string Text(Workbook wb, string part) => System.Text.Encoding.UTF8.GetString(wb.Package.FindPart(part)!.GetBytes().Span);

    [Fact]
    public void PropertiesAreCarried()
    {
        using Workbook wb = Open();
        var props = new RecursiveAI.OneDocument.Properties.DocumentProperties(wb.Package);
        Assert.Equal("Fixture ods", props.Core.Title);
        Assert.Equal("Ada Lovelace", props.Core.Creator);
        Assert.Equal("Grace Hopper", props.Core.LastModifiedBy);
        Assert.Equal("Atlas", props.Custom.Find("Project")!.Value.AsString);
        Assert.Equal(0, wb.ImportLoss!.CountOf("loss.document-properties"));
    }

    [Fact]
    public void TheEmbeddedObjectIsCarriedWithItsPreviewAndAnchor()
    {
        var defects = new DefectCollection();
        using Workbook wb = Open(defects);
        Assert.Equal(0, wb.ImportLoss!.CountOf("loss.ole-objects"));
        Assert.Equal(0, wb.ImportLoss!.CountOf("loss.pictures"));
        string sheet = wb.Sheets[0].LayoutMarkup.OuterXml;
        Assert.Contains("<oleObject progId=\"Excel.Sheet.5\" shapeId=\"1025\"", sheet, StringComparison.Ordinal);
        Assert.Contains("<legacyDrawing", sheet, StringComparison.Ordinal);

        string vml = Text(wb, "/xl/drawings/vmlDrawing1.vml");
        Assert.Contains("o:ole=\"\"", vml, StringComparison.Ordinal);
        Assert.Contains("<x:ClientData ObjectType=\"Pict\">", vml, StringComparison.Ordinal);
        Assert.Contains("<v:imagedata o:relid=\"rId1\"", vml, StringComparison.Ordinal);
        Assert.Contains(wb.Package.GetRelationships("/xl/drawings/vmlDrawing1.vml"), r => r.Type.EndsWith("/image", StringComparison.Ordinal) && r.Target == "../media/image1.png");
        Assert.Contains(wb.Package.GetRelationships(wb.Sheets[0].PartName!), r => r.Type.EndsWith("/oleObject", StringComparison.Ordinal) && r.Target == "../embeddings/oleObject1.bin");

        // The object is no longer a plain picture of the sheet.
        Assert.Null(wb.Package.FindPart("/xl/drawings/drawing1.xml"));

        IPackagePart bin = wb.Package.FindPart("/xl/embeddings/oleObject1.bin")!;
        Assert.Equal("application/vnd.openxmlformats-officedocument.oleObject", bin.ContentType);
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bin.GetBytes().ToArray()));
        Assert.Equal(new Guid("00020810-0000-0000-C000-000000000046"), cfb.RootClassId);
        using CfbPackage source = CfbPackage.Open(ByteSource.FromFile(Fixture()));
        OleStorageImage embedded = Assert.Single(OleStorageSet.CaptureByPrefix(source, "MBD", new OpenContext()));
        Assert.Equal(embedded.Find("Workbook"), cfb.FindPart("/Workbook")!.GetBytes().ToArray());
        Assert.DoesNotContain(defects.ToArray(), d => d.Code.StartsWith("ole.", StringComparison.Ordinal) || d.Code.StartsWith("biff.object", StringComparison.Ordinal));
    }
}
