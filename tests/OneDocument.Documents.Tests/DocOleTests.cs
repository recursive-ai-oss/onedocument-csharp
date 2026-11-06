using System.Runtime.CompilerServices;
using System.Text;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>Document properties and the embedded object of a Word binary file written by a word processor (testdata/ole).</summary>
public sealed class DocOleTests
{
    static DocOleTests() => DocumentFormats.Register(FormatRegistry.Default);

    private static string Fixture([CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "../../testdata/ole/ole-props.doc"));

    [Fact]
    public void PropertiesAreCarried()
    {
        var defects = new DefectCollection();
        using Document d = Document.Open(Fixture(), new OpenContext(defects: defects));
        Assert.Equal("Fixture odt", d.Properties.Core.Title);
        Assert.Equal("Embedded objects", d.Properties.Core.Subject);
        Assert.Equal("Ada Lovelace", d.Properties.Core.Creator);
        Assert.Equal("Grace Hopper", d.Properties.Core.LastModifiedBy);
        Assert.Equal("ole, fixture", d.Properties.Core.Keywords);
        Assert.Equal("Fixture with properties", d.Properties.Core.Description);
        Assert.NotNull(d.Properties.Core.Created);
        Assert.Equal("Atlas", d.Properties.Custom.Find("Project")!.Value.AsString);
        Assert.Equal(42.0, d.Properties.Custom.Find("Count")!.Value.AsDouble);
        Assert.True(d.Properties.Custom.Find("Reviewed")!.Value.AsBoolean);
        Assert.NotNull(d.Properties.Custom.Find("Due")!.Value.AsDateTime);
        Assert.Equal(0, d.ImportLoss!.CountOf("loss.doc-properties"));
        Assert.DoesNotContain(defects.ToArray(), x => x.Code.StartsWith("ole.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheEmbeddedObjectIsCarriedAsAnObjectPart()
    {
        using Document d = Document.Open(Fixture());
        Assert.Equal(0, d.ImportLoss!.CountOf("loss.doc-ole-objects"));
        string xml = d.MainMarkup.Root.OuterXml;
        Assert.Contains("<o:OLEObject Type=\"Embed\"", xml, StringComparison.Ordinal);
        Assert.Contains("<v:imagedata r:id=", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("EMBED", xml, StringComparison.Ordinal);

        IPackagePart bin = d.Package.FindPart("/word/embeddings/oleObject1.bin")!;
        Assert.Equal("application/vnd.openxmlformats-officedocument.oleObject", bin.ContentType);
        Assert.Contains(d.Package.GetRelationships("/word/document.xml"), r => r.Type.EndsWith("/oleObject", StringComparison.Ordinal) && r.Target == "embeddings/oleObject1.bin");

        // The part is the object's storage as a compound file of its own: the class, the workbook stream and the object streams.
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bin.GetBytes().ToArray()));
        Assert.Equal(new Guid("00020810-0000-0000-C000-000000000046"), cfb.RootClassId);
        Assert.NotNull(cfb.FindPart("/Workbook"));
        Assert.NotNull(cfb.FindPart("/\u0001CompObj"));
        Assert.NotNull(cfb.FindPart("/\u0001Ole"));
        Assert.Contains("Before the object", d.ExtractText(), StringComparison.Ordinal);
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
        byte[] file = Rewritten((name, data) => name.StartsWith("/ObjectPool", StringComparison.Ordinal) ? null : data);
        var defects = new DefectCollection();
        using Document d = Document.Open(new MemoryStream(file), new OpenContext(defects: defects));
        Defect defect = Assert.Single(defects, x => x.Code == "doc.ole-storage-missing");
        Assert.Equal(DefectSeverity.Dropped, defect.Severity);
        Assert.Equal("/ObjectPool/_2147483647", defect.Location.Container);
        Assert.Equal(1, d.ImportLoss!.CountOf("loss.doc-ole-objects"));
        Assert.Null(d.Package.FindPart("/word/embeddings/oleObject1.bin"));
        Assert.Contains("<w:drawing>", d.MainMarkup.Root.OuterXml, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkWithoutAReadableMonikerKeepsItsPictureAndIsReported()
    {
        // A link whose \u0001Ole stream has the link flag and no monikers.
        byte[] link = [0x01, 0x00, 0x00, 0x02, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] file = Rewritten((name, data) => name == "/ObjectPool/_2147483647/\u0001Ole" ? link : data);
        var defects = new DefectCollection();
        using Document d = Document.Open(new MemoryStream(file), new OpenContext(defects: defects));
        Assert.Contains(defects, x => x.Code == "doc.ole-link-unreadable" && x.Kind == DefectKind.Reference);
        Assert.Equal(1, d.ImportLoss!.CountOf("loss.doc-ole-objects"));
    }

    [Fact]
    public void TheObjectStorageIsTheOneInTheFile()
    {
        using CfbPackage source = CfbPackage.Open(ByteSource.FromFile(Fixture()));
        byte[] original = source.FindPart("/ObjectPool/_2147483647/Workbook")!.GetBytes().ToArray();
        using Document d = Document.Open(Fixture());
        using CfbPackage carried = CfbPackage.Open(ByteSource.FromMemory(d.Package.FindPart("/word/embeddings/oleObject1.bin")!.GetBytes().ToArray()));
        Assert.Equal(original, carried.FindPart("/Workbook")!.GetBytes().ToArray());
        Assert.Equal(Encoding.UTF8.GetString(source.FindPart("/ObjectPool/_2147483647/\u0001CompObj")!.GetBytes().Span), Encoding.UTF8.GetString(carried.FindPart("/\u0001CompObj")!.GetBytes().Span));
    }
}
