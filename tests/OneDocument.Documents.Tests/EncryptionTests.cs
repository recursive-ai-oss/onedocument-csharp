using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// Password-protected word-processing files written by LibreOffice (tools/Encryption/make-encrypted.py, password "secret"): a Word 97-2003
/// file and an Office Open XML package with agile encryption. Each opens with the password and says what is wrong without one.
/// </summary>
public sealed class EncryptionTests
{
    static EncryptionTests() => DocumentFormats.Register(FormatRegistry.Default);

    private static string Path(string name, [CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "../../testdata/encryption", name));

    [Fact]
    public void EncryptedWordBinaryFileOpensWithThePassword()
    {
        string plain = Office.ExtractText(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path("x"))!, "..", "doc", "basic.doc")));
        Assert.Equal(plain, Office.ExtractText(Path("secret-basic.doc"), new OpenContext(password: "secret")));
    }

    [Fact]
    public void EncryptedWordBinaryFileWithoutOrWithWrongPassword()
    {
        var none = Assert.Throws<OfficeEncryptedException>(() => Office.ExtractText(Path("secret-basic.doc")));
        Assert.Equal(EncryptionProblem.PasswordRequired, none.Problem);
        var wrong = Assert.Throws<OfficeEncryptedException>(() => Office.ExtractText(Path("secret-basic.doc"), new OpenContext(password: "other")));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, wrong.Problem);
    }

    [Fact]
    public void EncryptedPackageOpensAsADocumentAndMatchesThePlainOne()
    {
        var defects = new DefectCollection();
        using Document opened = Document.Open(Path("secret-basic.docx"), new OpenContext(defects: defects, password: "secret"));
        using Document plain = Document.Open(Path("plain-basic.docx"));
        Assert.Equal(plain.ExtractText(), opened.ExtractText());
        Assert.Null(opened.FilePath);
        Assert.DoesNotContain(defects, d => d.Code == "encryption.integrity-mismatch");
    }

    [Fact]
    public void EncryptedPackageThroughTheRegistryAndTheStatics()
    {
        using OfficeFile file = Office.Open(Path("secret-basic.docx"), new OpenContext(password: "secret"));
        Assert.IsType<Document>(file);
        Assert.Equal(Office.ExtractText(Path("plain-basic.docx")), Office.ExtractText(Path("secret-basic.docx"), new OpenContext(password: "secret")));
    }

    [Fact]
    public void EncryptedPackageWithoutOrWithWrongPassword()
    {
        var none = Assert.Throws<OfficeEncryptedException>(() => Document.Open(Path("secret-basic.docx")));
        Assert.Equal(EncryptionProblem.PasswordRequired, none.Problem);
        Assert.Equal("encryption.password-required", none.Defect.Code);
        var wrong = Assert.Throws<OfficeEncryptedException>(() => Document.Open(Path("secret-basic.docx"), new OpenContext(password: "nope")));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, wrong.Problem);
    }

    private static string DocFixture(string name) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path("x"))!, "..", "doc", name + ".doc"));

    // Files this test builds can be written out for other programs to read (tools/Encryption/README.md): set ONEDOCUMENT_ENCRYPTION_OUT to a directory.
    private static void Keep(string name, byte[] bytes)
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_ENCRYPTION_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(System.IO.Path.Combine(dir, name), bytes);
        }
    }

    public static TheoryData<string, string> Variants() =>
        new()
        {
            { "basic", "cryptoapi" },
            { "table", "cryptoapi" },
            { "picture", "cryptoapi" },
            { "lists", "cryptoapi" },
            { "basic", "xor" },
            { "table", "xor" },
            { "picture", "xor" },
        };

    [Theory]
    [MemberData(nameof(Variants))]
    public void SchemesTheConvertersDoNotWriteOpenWithThePassword(string fixture, string scheme)
    {
        byte[] original = File.ReadAllBytes(DocFixture(fixture));
        byte[] protectedBytes = scheme == "xor" ? DocEncryptor.Xor(original, "p@ss word") : DocEncryptor.CryptoApi(original, "p@ss word");
        Keep($"{scheme}-{fixture}.doc", protectedBytes);
        string expected = Office.ExtractText(DocFixture(fixture));
        using var source = RecursiveAI.OneDocument.IO.ByteSource.FromMemory(protectedBytes);
        var sw = new StringWriter();
        Office.ExtractText(source, sw, new OpenContext(password: "p@ss word"));
        Assert.Equal(expected, sw.ToString());

        var wrong = Assert.Throws<OfficeEncryptedException>(() => Office.ExtractText(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(protectedBytes), new StringWriter(), new OpenContext(password: "wrong")));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, wrong.Problem);
        var none = Assert.Throws<OfficeEncryptedException>(() => Office.ExtractText(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(protectedBytes), new StringWriter()));
        Assert.Equal(EncryptionProblem.PasswordRequired, none.Problem);
    }

    private static byte[] PatchWordStream(byte[] file, Action<byte[]> patch)
    {
        using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(file));
        byte[] word = cfb.FindPart("/WordDocument")!.GetBytes().ToArray();
        patch(word);
        cfb.FindPart("/WordDocument")!.SetBytes(word);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    private static string TryOpen(byte[] file, string password)
    {
        try
        {
            Office.ExtractText(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(file), new StringWriter(), new OpenContext(password: password));
            return "opened";
        }
        catch (OfficeException ex)
        {
            return ex.Defect.Code;
        }
    }

    [Fact]
    public void DamagedEncryptionFieldsAreTyped()
    {
        byte[] encrypted = DocEncryptor.CryptoApi(File.ReadAllBytes(DocFixture("basic")), "pw");
        Assert.Equal("opened", TryOpen(encrypted, "pw"));

        // lKey says the header is longer than the whole table stream.
        Assert.Equal("encryption.malformed-header", TryOpen(PatchWordStream(encrypted, w => BitConverter.TryWriteBytes(w.AsSpan(14), 0x7FFF_0000u)), "pw"));
        Assert.Equal("encryption.malformed-header", TryOpen(PatchWordStream(encrypted, w => BitConverter.TryWriteBytes(w.AsSpan(14), 2u)), "pw"));

        // Word 6/95 files and a table stream that names a header from a newer version.
        var legacy = Assert.Throws<OfficeUnsupportedException>(() => Office.ExtractText(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(PatchWordStream(encrypted, w => w[0] = 0xDC)), new StringWriter(), new OpenContext(password: "pw")));
        Assert.Equal("password-protected Word 6/95 documents", legacy.Feature);

        using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(encrypted));
        bool one = (BitConverter.ToUInt16(cfb.FindPart("/WordDocument")!.GetBytes().Span.Slice(10, 2)) & 0x0200) != 0;
        byte[] table = cfb.FindPart(one ? "/1Table" : "/0Table")!.GetBytes().ToArray();
        table[0] = 9;
        cfb.FindPart(one ? "/1Table" : "/0Table")!.SetBytes(table);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        var version = Assert.Throws<OfficeUnsupportedException>(() => Office.ExtractText(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(ms.ToArray()), new StringWriter(), new OpenContext(password: "pw")));
        Assert.Equal("binary encryption header version 9.2", version.Feature);
        Assert.Equal(one ? "/1Table" : "/0Table", version.Location.Container);
    }
}
