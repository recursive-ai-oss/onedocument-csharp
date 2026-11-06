using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>Document properties and the embedded object of a PowerPoint binary file written by a presentation program (testdata/ole).</summary>
public sealed class PptOleTests
{
    private static string Fixture([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../../testdata/ole/ole-props.ppt"));

    [Fact]
    public void PropertiesAreCarried()
    {
        using Presentation p = Presentation.Open(Fixture());
        var props = new DocumentProperties(p.Package);
        Assert.Equal("Fixture odp", props.Core.Title);
        Assert.Equal("Ada Lovelace", props.Core.Creator);
        Assert.Equal("Grace Hopper", props.Core.LastModifiedBy);
        Assert.Equal("ole, fixture", props.Core.Keywords);
        Assert.Equal("Atlas", props.Custom.Find("Project")!.Value.AsString);
        Assert.Equal(42.0, props.Custom.Find("Count")!.Value.AsDouble);

        // What stays out is named: the thumbnail, a picture the package keeps as a part of its own.
        Assert.Equal(1, p.ConversionLoss!.CountOf("ppt.document-properties-not-carried"));
        Assert.Contains("thumbnail", p.ConversionLoss.Items.Single(i => i.Code == "ppt.document-properties-not-carried").Message, StringComparison.Ordinal);
    }

    // The fixture with the object's storage record (ExOleObjStg, compressed) changed by a callback that gets the document stream and the offset of the record.
    private static byte[] WithStorageRecord(Action<byte[], int> change)
    {
        using CfbPackage source = CfbPackage.Open(ByteSource.FromFile(Fixture()));
        using CfbPackage copy = CfbPackage.Create();
        copy.RootClassId = source.RootClassId;
        foreach (CfbStreamPart part in source.Streams)
        {
            byte[] bytes = part.GetBytes().ToArray();
            if (part.Name == "/PowerPoint Document")
            {
                // A record header with version 0, instance 1 (compressed) and type 0x1011.
                int at = bytes.AsSpan().IndexOf(new byte[] { 0x10, 0x00, 0x11, 0x10 });
                Assert.True(at >= 0, "the fixture has a compressed object storage");
                change(bytes, at);
            }

            copy.AddPart(part.Name, string.Empty, bytes);
        }

        using var ms = new MemoryStream();
        copy.WriteTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void AStorageThatDoesNotInflateKeepsThePictureAndIsReported()
    {
        byte[] file = WithStorageRecord((bytes, at) =>
        {
            for (int i = 14; i < 30; i++)
            {
                bytes[at + i] ^= 0xFF; // the zlib stream after the 4-byte declared size
            }
        });
        using Presentation p = Presentation.Open(new MemoryStream(file));
        Assert.Contains(p.Defects, d => d.Code == "ppt.ole-storage-unreadable" && d.Severity == Diagnostics.DefectSeverity.Dropped);
        Assert.Equal(1, p.ConversionLoss!.CountOf("ppt.ole-object-flattened"));
        Assert.Null(p.Package.FindPart("/ppt/embeddings/oleObject1.bin"));
        Assert.NotNull(p.Package.FindPart("/ppt/media/image1.png"));
    }

    [Fact]
    public void AStorageDeclaringMoreThanTheLimitsAllowIsNotInflated()
    {
        byte[] file = WithStorageRecord((bytes, at) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 8), 0x7FFFFFF0));
        using Presentation p = Presentation.Open(new MemoryStream(file));
        Assert.Contains(p.Defects, d => d.Code == "ppt.ole-storage-too-large" && d.Kind == Diagnostics.DefectKind.Limit);
        Assert.Equal(1, p.ConversionLoss!.CountOf("ppt.ole-object-flattened"));
    }

    [Fact]
    public void TheEmbeddedObjectIsCarriedAsAnObjectShape()
    {
        using Presentation p = Presentation.Open(Fixture());
        Assert.Equal(0, p.ConversionLoss!.CountOf("ppt.ole-object-flattened"));
        string slide = System.Text.Encoding.UTF8.GetString(p.Package.FindPart("/ppt/slides/slide1.xml")!.GetBytes().Span);
        Assert.Contains("<a:graphicData uri=\"http://schemas.openxmlformats.org/presentationml/2006/ole\"><p:oleObj", slide, StringComparison.Ordinal);
        Assert.Contains("<p:embed/>", slide, StringComparison.Ordinal);
        Assert.Contains("progId=\"Excel.Sheet.", slide, StringComparison.Ordinal);
        Assert.Contains("<p:pic>", slide, StringComparison.Ordinal);

        Relationship rel = Assert.Single(p.Package.GetRelationships("/ppt/slides/slide1.xml"), r => r.Type.EndsWith("/oleObject", StringComparison.Ordinal));
        Assert.Equal("../embeddings/oleObject1.bin", rel.Target);
        IPackagePart bin = p.Package.FindPart("/ppt/embeddings/oleObject1.bin")!;
        Assert.Equal("application/vnd.openxmlformats-officedocument.oleObject", bin.ContentType);
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bin.GetBytes().ToArray()));
        Assert.Equal(new Guid("00020810-0000-0000-C000-000000000046"), cfb.RootClassId);
        Assert.NotNull(cfb.FindPart("/Workbook"));
        Assert.NotNull(cfb.FindPart("/\u0001CompObj"));
    }
}
