using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Ole;

namespace RecursiveAI.OneDocument.Tests;

public class OleStorageTests
{
    private static byte[] CompObj(string userType, string progId)
    {
        var b = new List<byte>();
        b.AddRange(new byte[28]);
        void Ansi(string s)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(s + "\0");
            b.AddRange(BitConverter.GetBytes((uint)bytes.Length));
            b.AddRange(bytes);
        }

        Ansi(userType);
        b.AddRange(BitConverter.GetBytes(0xFFFFFFFFu)); // clipboard format marker
        b.AddRange(BitConverter.GetBytes(0u));
        Ansi(progId);
        return [.. b];
    }

    private static byte[] LinkedOleStream(string path)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(0x02000001u)); // version
        b.AddRange(BitConverter.GetBytes(1u)); // flags: linked
        b.AddRange(BitConverter.GetBytes(0u)); // link update option
        b.AddRange(BitConverter.GetBytes(0u)); // reserved
        b.AddRange(BitConverter.GetBytes(0u)); // no reserved moniker stream
        b.AddRange(BitConverter.GetBytes(0u)); // no relative source moniker

        // An absolute source moniker: a size that counts itself, the file moniker CLSID, cAnti, the ANSI path.
        byte[] ansi = Encoding.Latin1.GetBytes(path + "\0");
        var moniker = new List<byte>();
        moniker.AddRange(new Guid("00000303-0000-0000-C000-000000000046").ToByteArray());
        moniker.AddRange(BitConverter.GetBytes((ushort)0));
        moniker.AddRange(BitConverter.GetBytes((uint)ansi.Length));
        moniker.AddRange(ansi);
        moniker.AddRange(new byte[] { 0xFF, 0xFF, 0xAD, 0xDE });
        b.AddRange(BitConverter.GetBytes((uint)moniker.Count + 4));
        b.AddRange(moniker);
        return [.. b];
    }

    private static OleStorageImage Image(Guid cls, params (string Path, byte[] Data)[] streams) =>
        new("_1", cls, [.. streams.Select(s => new OleStreamImage(s.Path, s.Data))], []);

    [Fact]
    public void AnEmbeddedObjectIsDescribedByItsCompObjStream()
    {
        OleStorageImage image = Image(new Guid("00020906-0000-0000-C000-000000000046"), ("\u0001CompObj", CompObj("Microsoft Word Document", "Word.Document.8")), ("WordDocument", [1, 2, 3]));
        OleObjectInfo info = image.Describe();
        Assert.Equal(OleObjectKind.Embedded, info.Kind);
        Assert.Equal("Word.Document.8", info.ProgId);
        Assert.Equal("Microsoft Word Document", info.UserType);
        Assert.Null(info.LinkTarget);
    }

    [Fact]
    public void AClassWithoutACompObjStreamGivesItsKnownProgId()
    {
        Assert.Equal("Equation.3", Image(new Guid("0002CE02-0000-0000-C000-000000000046")).Describe().ProgId);
        Assert.Null(Image(Guid.NewGuid()).Describe().ProgId);
    }

    [Fact]
    public void ALinkedObjectNamesItsFile()
    {
        OleStorageImage image = Image(OleObjectInfo.StandardLinkClassId, ("\u0001Ole", LinkedOleStream("C:\\data\\sheet one.xls")), ("\u0001CompObj", CompObj("Microsoft Excel Worksheet", "Excel.Sheet.8")));
        OleObjectInfo info = image.Describe();
        Assert.Equal(OleObjectKind.Linked, info.Kind);
        Assert.Equal("C:\\data\\sheet one.xls", info.LinkTarget);
        Assert.Equal("file:///C:/data/sheet%20one.xls", OleEmbedding.FileUri(info.LinkTarget!));
    }

    [Fact]
    public void APackageIsRecognized()
    {
        OleStorageImage image = Image(OleObjectInfo.PackageClassId, ("\u0001Ole10Native", [4, 0, 0, 0, 1, 2, 3, 4]));
        Assert.Equal(OleObjectKind.Package, image.Describe().Kind);
        Assert.True(image.Describe().IsPackage);
        Assert.Equal("Package", image.Describe().ProgId);
    }

    [Fact]
    public void DamagedDescriptionStreamsGiveLessDetailNeverAnException()
    {
        foreach (int n in new[] { 0, 3, 27, 28, 31, 40 })
        {
            byte[] good = CompObj("Type", "Prog.Id");
            OleStorageImage image = Image(Guid.Empty, ("\u0001CompObj", good.AsSpan(0, Math.Min(n, good.Length)).ToArray()), ("\u0001Ole", good.AsSpan(0, Math.Min(n, good.Length)).ToArray()));
            _ = image.Describe();
        }

        byte[] huge = new byte[60];
        BitConverter.GetBytes(0xFFFFFFF0u).CopyTo(huge, 28);
        _ = Image(Guid.Empty, ("\u0001CompObj", huge)).Describe();
    }

    [Fact]
    public void AStorageRoundTripsThroughACompoundFileWithItsClassAndNestedStorages()
    {
        var cls = new Guid("00020810-0000-0000-C000-000000000046");
        var sub = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var original = new OleStorageImage("_77", cls, [new OleStreamImage("Workbook", [1, 2, 3]), new OleStreamImage("Nested/Inner", [9]), new OleStreamImage("\u0001Ole", new byte[20])], [new OleSubStorageImage("Nested", sub)]);
        byte[] file = original.ToCompoundFile();
        OleStorageImage? back = OleStorageImage.FromCompoundFile(file, "_77");
        Assert.NotNull(back);
        Assert.Equal(cls, back!.ClassId);
        Assert.Equal(new byte[] { 1, 2, 3 }, back.Find("Workbook"));
        Assert.Equal(new byte[] { 9 }, back.Find("Nested/Inner"));
        Assert.Equal(sub, Assert.Single(back.Storages).ClassId);
    }

    [Fact]
    public void ANameThatACompoundFileCannotStoreIsATypedError()
    {
        OleStorageImage image = Image(Guid.Empty, (new string('x', 40), []));
        var ex = Assert.Throws<OfficeFormatException>(() => image.ToCompoundFile());
        Assert.Equal("ole.storage-name-invalid", ex.Defect.Code);
    }

    [Fact]
    public void BytesThatAreNotACompoundFileGiveNullAndADefect()
    {
        var defects = new DefectCollection();
        Assert.Null(OleStorageImage.FromCompoundFile(new byte[] { 1, 2, 3 }, "x", new OpenContext(defects: defects)));
        Assert.NotEmpty(defects);
    }

    [Fact]
    public void CapturingAStorageTooLargeForTheLimitsIsReportedAndLeftOut()
    {
        using CfbPackage cfb = CfbPackage.Create();
        cfb.AddPart("/ObjectPool/_1/Data", string.Empty, new byte[5000]);
        cfb.AddPart("/ObjectPool/_2/Data", string.Empty, new byte[10]);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        using CfbPackage opened = CfbPackage.Open(ByteSource.FromMemory(ms.ToArray()));
        var defects = new DefectCollection();
        OleStorageSet set = OleStorageSet.Capture(opened, "/ObjectPool", new OpenContext(new OfficeLimits { MaxPartSize = 1000 }, defects));
        Assert.Equal("_2", Assert.Single(set).Name);
        Defect d = Assert.Single(defects, x => x.Code == "ole.storage-too-large");
        Assert.Equal(DefectKind.Limit, d.Kind);
    }
}
