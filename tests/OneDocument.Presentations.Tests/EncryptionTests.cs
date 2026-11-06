using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>
/// Password-protected presentations written by LibreOffice (tools/Encryption/make-encrypted.py, password "secret"): a PowerPoint
/// 97-2003 file and an Office Open XML package with agile encryption.
/// </summary>
public class EncryptionTests
{
    private static string Dir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "encryption"));

    [Fact]
    public void EncryptedPackageOpensAsAPresentationAndMatchesThePlainOne()
    {
        using Presentation plain = Presentation.Open(Path.Combine(Dir(), "plain-runs.pptx"));
        var defects = new DefectCollection();
        var ctx = new OpenContext(defects: defects, password: "secret");
        using Presentation opened = Presentation.Open(Path.Combine(Dir(), "secret-runs.pptx"), ctx);
        Assert.Equal(plain.Slides.Count, opened.Slides.Count);
        Assert.Equal(plain.ExtractText(), opened.ExtractText());
        Assert.DoesNotContain(defects, d => d.Code == "encryption.integrity-mismatch");
    }

    [Fact]
    public void EncryptedPackageWithoutOrWithWrongPassword()
    {
        string path = Path.Combine(Dir(), "secret-runs.pptx");
        Assert.Equal(EncryptionProblem.PasswordRequired, Assert.Throws<OfficeEncryptedException>(() => Presentation.Open(path)).Problem);
        Assert.Equal(EncryptionProblem.PasswordIncorrect, Assert.Throws<OfficeEncryptedException>(() => Presentation.Open(path, new OpenContext(password: "x"))).Problem);
    }

    // Files this test builds can be written out for other programs to read (tools/Encryption/README.md): set ONEDOCUMENT_ENCRYPTION_OUT to a directory.
    private static void Keep(string name, byte[] bytes)
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_ENCRYPTION_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
        }
    }

    [Theory]
    [InlineData("lo-slide-runs.ppt")]
    [InlineData("lo-slide-pictures-charts-diagrams.ppt")]
    [InlineData("lo-slide-table.ppt")]
    public void EncryptedBinaryFileOpensWithThePassword(string fixture)
    {
        string path = Path.Combine(Dir(), "..", "ppt", fixture);
        byte[] encrypted = PptEncryptor.Encrypt(File.ReadAllBytes(path), "p@ss word");
        Keep("cryptoapi-" + fixture, encrypted);
        using Presentation plain = Presentation.Open(path);
        using Presentation opened = Presentation.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(encrypted), new OpenContext(password: "p@ss word"));
        Assert.Equal(plain.Slides.Count, opened.Slides.Count);
        Assert.Equal(plain.ExtractText(), opened.ExtractText());
        Assert.Equal(plain.Package.FindPart("/ppt/slides/slide1.xml")!.GetBytes().ToArray(), opened.Package.FindPart("/ppt/slides/slide1.xml")!.GetBytes().ToArray());
    }

    [Fact]
    public void FileProtectedAgainstEditingOnlyOpensWithoutAPassword()
    {
        // [MS-OFFCRYPTO] §2.4.2.3: PowerPoint encrypts such a file with a fixed password.
        string path = Path.Combine(Dir(), "..", "ppt", "lo-slide-runs.ppt");
        byte[] encrypted = PptEncryptor.Encrypt(File.ReadAllBytes(path), "/01Hannes Ruescher/01");
        Keep("default-lo-slide-runs.ppt", encrypted);
        using Presentation plain = Presentation.Open(path);
        using Presentation opened = Presentation.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(encrypted));
        Assert.Equal(plain.ExtractText(), opened.ExtractText());
    }

    [Fact]
    public void EncryptedBinaryFileWithoutOrWithWrongPassword()
    {
        byte[] encrypted = PptEncryptor.Encrypt(File.ReadAllBytes(Path.Combine(Dir(), "..", "ppt", "lo-slide-runs.ppt")), "p@ss word");
        var none = Assert.Throws<OfficeEncryptedException>(() => Presentation.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(encrypted)));
        Assert.Equal(EncryptionProblem.PasswordRequired, none.Problem);
        var wrong = Assert.Throws<OfficeEncryptedException>(() => Presentation.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(encrypted), new OpenContext(password: "x")));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, wrong.Problem);
    }

    private static byte[] PatchDocumentStream(byte[] file, Action<byte[]> patch)
    {
        using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(file));
        byte[] doc = cfb.FindPart("/PowerPoint Document")!.GetBytes().ToArray();
        patch(doc);
        cfb.FindPart("/PowerPoint Document")!.SetBytes(doc);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void DamagedEncryptedFilesAreTypedWithTheirLocation()
    {
        string path = Path.Combine(Dir(), "..", "ppt", "lo-slide-runs.ppt");
        byte[] plain = File.ReadAllBytes(path);
        byte[] encrypted = PptEncryptor.Encrypt(plain, "pw");
        long cryptOffset = RecursiveAI.OneDocument.Ppt.Binary.PptFile.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(plain), new OpenContext()).DocumentStream.Length;

        // The container claims more bytes than the stream has.
        byte[] longContainer = PatchDocumentStream(encrypted, d => BitConverter.TryWriteBytes(d.AsSpan((int)cryptOffset + 4), 0x7FFF_FFF0u));
        var tooLong = Assert.Throws<OfficeFormatException>(() => Presentation.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(longContainer), new OpenContext(password: "pw")));
        Assert.Equal("encryption.malformed-header", tooLong.Defect.Code);
        Assert.Equal(cryptOffset, tooLong.Location.Offset);

        // The header names a version this reader does not know.
        byte[] newer = PatchDocumentStream(encrypted, d => d[(int)cryptOffset + 8] = 9);
        var version = Assert.Throws<OfficeUnsupportedException>(() => Presentation.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(newer), new OpenContext(password: "pw")));
        Assert.Equal("binary encryption header version 9.2", version.Feature);

        // A persist object whose decrypted header claims more than the stream holds is dropped with a defect; the rest of the file still opens.
        var file = RecursiveAI.OneDocument.Ppt.Binary.PptFile.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(plain), new OpenContext());
        uint victim = file.PersistOffsets.Keys.Where(k => k != file.UserEdits[0].DocumentPersistId).OrderBy(k => k).Last();
        long victimOffset = file.PersistOffsets[victim];
        byte[] damaged = PatchDocumentStream(encrypted, d => d[(int)victimOffset + 7] ^= 0x7F);
        var defects = new DefectCollection();
        try
        {
            using Presentation p = Presentation.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(damaged), new OpenContext(defects: defects, password: "pw"));
        }
        catch (OfficeException)
        {
            // Losing that object may also make the file unreadable; the defect is what is asserted.
        }

        Assert.Contains(defects, x => x.Code == "ppt.persist-object-range" && x.Location.Offset == victimOffset);
    }
}
