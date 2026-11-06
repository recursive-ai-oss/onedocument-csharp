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
    public void PropertiesSurviveWritingAnXls()
    {
        using Workbook wb = Workbook.Create();
        var props = new RecursiveAI.OneDocument.Properties.DocumentProperties(wb.Package);
        props.Core.Title = "Quarterly";
        props.Core.Creator = "Ada";
        props.Extended.Company = "Analytical Engines";
        props.Custom.SetString("Project", "Atlas");
        props.Commit();
        byte[] bytes = XlsWriterTests.Save(wb, out LossReport loss);
        Assert.Equal(0, loss.CountOf("loss.document-properties"));

        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bytes));
        Assert.NotNull(cfb.FindPart("/" + OlePropertySets.SummaryInformationStreamName));
        Assert.NotNull(cfb.FindPart("/" + OlePropertySets.DocumentSummaryInformationStreamName));

        using Workbook back = XlsWriterTests.Reopen(bytes);
        var read = new RecursiveAI.OneDocument.Properties.DocumentProperties(back.Package);
        Assert.Equal("Quarterly", read.Core.Title);
        Assert.Equal("Ada", read.Core.Creator);
        Assert.Equal("Analytical Engines", read.Extended.Company);
        Assert.Equal("Atlas", read.Custom.Find("Project")!.Value.AsString);
    }

    // The fixture with its streams copied into a new compound file, through a callback that may change or drop each.
    private static byte[] Rewritten(Func<string, byte[], byte[]?> edit)
    {
        using CfbPackage source = CfbPackage.Open(ByteSource.FromFile(Fixture()));
        using CfbPackage copy = CfbPackage.Create();
        copy.RootClassId = source.RootClassId;
        foreach (CfbStreamPart part in source.Streams)
        {
            if (edit(part.Name, part.GetBytes().ToArray()) is { } bytes)
            {
                copy.AddPart(part.Name, string.Empty, bytes);
            }
        }

        using var ms = new MemoryStream();
        copy.WriteTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void AnObjectWhoseStorageIsMissingKeepsItsPictureAndIsReported()
    {
        byte[] file = Rewritten((name, data) => name.StartsWith("/MBD", StringComparison.Ordinal) ? null : data);
        var defects = new DefectCollection();
        using Workbook wb = XlsConverter.ToWorkbook(ByteSource.FromMemory(file), new OpenContext(defects: defects));
        Defect d = Assert.Single(defects, x => x.Code == "biff.embedding-storage-missing");
        Assert.Equal(DefectKind.Reference, d.Kind);
        Assert.Equal(1, wb.ImportLoss!.CountOf("loss.ole-objects"));
        Assert.Null(wb.Package.FindPart("/xl/embeddings/oleObject1.bin"));
        Assert.NotNull(wb.Package.FindPart("/xl/drawings/drawing1.xml")); // the object stays a picture
    }

    [Fact]
    public void ObjectRecordsThatDoNotMatchTheShapesLeaveTheObjectsAsPictures()
    {
        // One more Obj record (a group, ot 0) before the sheet's last EOF: the drawing and the records no longer pair up.
        byte[] extra = [0x5D, 0x00, 0x1A, 0x00, 0x15, 0x00, 0x12, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x00, 0x00, 0x00];
        byte[] file = Rewritten((name, data) => name == "/Workbook" ? [.. data[..^4], .. extra, .. data[^4..]] : data);
        var defects = new DefectCollection();
        using Workbook wb = XlsConverter.ToWorkbook(ByteSource.FromMemory(file), new OpenContext(defects: defects));
        Assert.Contains(defects, x => x.Code == "biff.object-count-mismatch" && x.Severity == DefectSeverity.Repaired);
        Assert.Equal(1, wb.ImportLoss!.CountOf("loss.ole-objects"));
        Assert.Null(wb.Package.FindPart("/xl/embeddings/oleObject1.bin"));
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
