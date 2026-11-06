using System.Buffers.Binary;
using System.IO.Compression;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Tests;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>The VBA project of a PowerPoint 97-2003 file: a persisted, zlib-compressed storage and the flag that announces it.</summary>
public sealed class PptVbaTests
{
    private static byte[] Record(int verInstance, int type, byte[] payload)
    {
        byte[] r = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(r, (ushort)verInstance);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(2), (ushort)type);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(r, 8);
        return r;
    }

    private static byte[] Compressed(byte[] storage)
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((uint)storage.Length));
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(storage);
        }

        return ms.ToArray();
    }

    // Document container { VBAInfoContainer { VBAInfoAtom } }, an ordinary OLE storage record, then the project record.
    private static byte[] Ppt(bool compressed, bool withInfo, bool encrypted = false)
    {
        byte[] project = VbaTestProject.Typical().Build();
        byte[] atom = Record(0x2, 0x0400, [.. BitConverter.GetBytes(1u), .. BitConverter.GetBytes(1u), .. BitConverter.GetBytes(0u)]);
        byte[] info = Record(0x1F, 0x03FF, atom);
        var doc = new List<byte>();
        if (withInfo)
        {
            doc.AddRange(Record(0xF, 0x03E8, info));
        }

        byte[] plainOle = new byte[40];
        plainOle[0] = 0xD0; // not a compound file: another persisted object
        doc.AddRange(Record(0x0, 0x1011, plainOle));
        int projectAt = doc.Count;
        doc.AddRange(compressed ? Record(0x10, 0x1011, Compressed(project)) : Record(0x00, 0x1011, project));
        var cfb = CfbPackage.Create();
        cfb.AddPart("/PowerPoint Document", string.Empty, doc.ToArray());
        byte[] user = new byte[28];
        BinaryPrimitives.WriteUInt16LittleEndian(user.AsSpan(2), 0x0FF6);
        BinaryPrimitives.WriteUInt32LittleEndian(user.AsSpan(4), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(user.AsSpan(8), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(user.AsSpan(12), encrypted ? 0xF3D1C4DF : 0xE391C05F);
        cfb.AddPart("/Current User", string.Empty, user);
        cfb.AddPart("/\u0005SummaryInformation", string.Empty, new byte[48]);
        _ = projectAt;
        using var output = new MemoryStream();
        cfb.WriteTo(output);
        return output.ToArray();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Open_FindsThePersistedProject(bool compressed)
    {
        byte[] file = Ppt(compressed, withInfo: true);
        PresentationFormats.Register(Formats.FormatRegistry.Default);
        VbaProject? p = VbaProject.Open(new MemoryStream(file));
        Assert.NotNull(p);
        Assert.StartsWith("PowerPoint Document@", p.Container, StringComparison.Ordinal);
        Assert.Equal(4, p.Modules.Count);
        Assert.Contains("Sub Run()", p.FindModule("Module1")!.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Remove_EmptiesTheRecordInPlaceAndClearsTheFlag(bool compressed)
    {
        byte[] file = Ppt(compressed, withInfo: true);
        PresentationFormats.Register(Formats.FormatRegistry.Default);
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(file), output);
        Assert.True(report.HadProject);
        Assert.Equal(OfficeFormat.Ppt, report.OutputFormat);
        Assert.Equal(4, report.ModulesRemoved);
        Assert.Contains(report.Removed, i => i.Kind == VbaRemovedKind.Project && i.Bytes > 1000);
        Assert.Contains(report.Removed, i => i.Kind == VbaRemovedKind.Marker);
        Assert.Contains(report.NotDone, n => n.Code == "vba.ppt-signature-kept");

        using CfbPackage before = CfbPackage.Open(ByteSource.FromMemory(file));
        using CfbPackage after = CfbPackage.Open(ByteSource.FromMemory(output.ToArray()));
        byte[] a = before.FindPart("/PowerPoint Document")!.GetBytes().ToArray();
        byte[] b = after.FindPart("/PowerPoint Document")!.GetBytes().ToArray();
        Assert.Equal(a.Length, b.Length); // nothing moves, so every persist offset stays right
        const int flag = 8 + 8 + 8 + 4; // three record headers and the persist reference
        Assert.Equal(a.AsSpan(0, flag).ToArray(), b.AsSpan(0, flag).ToArray());
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(a.AsSpan(flag)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(flag)));
        Assert.Equal(before.FindPart("/Current User")!.GetBytes().ToArray(), after.FindPart("/Current User")!.GetBytes().ToArray());
        Assert.Null(VbaProject.Open(new MemoryStream(output.ToArray())));
    }

    [Fact]
    public void Remove_WithoutTheInfoRecord_StillEmptiesTheProject()
    {
        byte[] file = Ppt(compressed: true, withInfo: false);
        PresentationFormats.Register(Formats.FormatRegistry.Default);
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(file), output);
        Assert.True(report.HadProject);
        Assert.DoesNotContain(report.Removed, i => i.Kind == VbaRemovedKind.Marker);
        Assert.Null(VbaProject.Open(new MemoryStream(output.ToArray())));
    }

    [Fact]
    public void NoProject_IsACopy()
    {
        byte[] file = Ppt(compressed: true, withInfo: false);
        using var cfb = CfbPackage.Open(ByteSource.FromMemory(file));
        cfb.FindPart("/PowerPoint Document")!.SetBytes(new byte[8]);
        using var plain = new MemoryStream();
        cfb.WriteTo(plain);
        PresentationFormats.Register(Formats.FormatRegistry.Default);
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(plain.ToArray()), output);
        Assert.False(report.HadProject);
        Assert.Null(VbaProject.Open(new MemoryStream(plain.ToArray())));
    }

    [Fact]
    public void EncryptedFile_IsATypedError_ForRemoval()
    {
        byte[] file = Ppt(compressed: true, withInfo: true, encrypted: true);
        PresentationFormats.Register(Formats.FormatRegistry.Default);
        Assert.Throws<OfficeUnsupportedException>(() => VbaRemoval.Remove(new MemoryStream(file), new MemoryStream()));
    }
}
