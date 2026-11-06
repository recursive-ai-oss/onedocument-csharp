using System.Text;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Protection;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class DocumentProtectionTests
{
    private const string W = TestDocs.WNs;

    private static string Settings(Document d)
    {
        d.CommitChanges();
        return Encoding.UTF8.GetString(d.Package.FindPart("/word/settings.xml")!.GetBytes().ToArray());
    }

    [Theory]
    [InlineData(ProtectionHash.Sha512, "SHA-512")]
    [InlineData(ProtectionHash.Sha384, "SHA-384")]
    [InlineData(ProtectionHash.Sha256, "SHA-256")]
    [InlineData(ProtectionHash.Sha1, "SHA-1")]
    [InlineData(ProtectionHash.Md5, "MD5")]
    public void ProtectSaveReopenVerify(ProtectionHash algorithm, string name)
    {
        using Document d = Document.Create();
        d.Body.AddParagraph("locked");
        Assert.False(d.Protection.IsEnforced);
        d.Protection.Protect(DocumentEditRestriction.TrackedChanges, "s3cret", restrictFormatting: true, new ProtectionOptions { Algorithm = algorithm, SpinCount = 500 });
        using var ms = new MemoryStream();
        d.SaveAs(ms, OfficeFormat.Docx);
        ms.Position = 0;
        using Document back = Document.Open(ms);
        DocumentProtection p = back.Protection;
        Assert.True(p.IsEnforced);
        Assert.Equal(DocumentEditRestriction.TrackedChanges, p.Restriction);
        Assert.True(p.FormattingRestricted);
        Assert.True(p.HasPassword);
        Assert.Equal(name, p.Verifier!.AlgorithmName);
        Assert.Equal(500, p.Verifier.SpinCount);
        Assert.Equal(PasswordCheck.Match, p.Verify("s3cret"));
        Assert.Equal(PasswordCheck.Mismatch, p.Verify("S3cret"));
        Assert.Equal(PasswordCheck.Mismatch, p.TryRemove("nope"));
        Assert.True(p.IsEnforced);
        Assert.Equal(PasswordCheck.Match, p.TryRemove("s3cret"));
        Assert.False(p.IsEnforced);
        Assert.False(p.HasPassword);
    }

    [Fact]
    public void ProtectionWithoutPasswordAndReplacement()
    {
        using Document d = Document.Create();
        d.Protection.Protect(DocumentEditRestriction.Forms);
        Assert.True(d.Protection.IsEnforced);
        Assert.False(d.Protection.HasPassword);
        Assert.Equal(PasswordCheck.Match, d.Protection.Verify("anything"));
        d.Protection.Protect(DocumentEditRestriction.ReadOnly, "x");
        string xml = Settings(d);
        Assert.Equal(1, Occurrences(xml, "<w:documentProtection"));
        Assert.Contains("w:edit=\"readOnly\"", xml);
        Assert.DoesNotContain("w:edit=\"forms\"", xml);
        Assert.Throws<ArgumentException>(() => d.Protection.Protect(DocumentEditRestriction.None));
    }

    [Fact]
    public void SaltIsSeededAndOutputIsDeterministic()
    {
        string One(byte[] seed)
        {
            using Document d = Document.Create();
            d.Protection.Protect(DocumentEditRestriction.ReadOnly, "pw", false, new ProtectionOptions { SpinCount = 10, Seed = seed });
            return Settings(d);
        }

        Assert.Equal(One([1, 2, 3]), One([1, 2, 3]));
        Assert.NotEqual(One([1, 2, 3]), One([1, 2, 4]));
    }

    [Fact]
    public void LegacyWordHashIsReadAndVerified()
    {
        // The hash Word 2007 wrote: the pre-2007 key of the password, byte-reversed, salted, hashed once more per spin.
        byte[] salt = [9, 8, 7, 6, 5, 4, 3, 2];
        byte[] key = BitConverter.GetBytes(PasswordVerifier.WordKey("Example"));
        byte[] hash = Legacy(key, salt, 25);
        string settings = $"<w:settings xmlns:w=\"{W}\"><w:documentProtection w:edit=\"comments\" w:enforcement=\"1\" w:cryptProviderType=\"rsaFull\" w:cryptAlgorithmClass=\"hash\" w:cryptAlgorithmType=\"typeAny\" w:cryptAlgorithmSid=\"12\" w:cryptSpinCount=\"25\" w:hash=\"{Convert.ToBase64String(hash)}\" w:salt=\"{Convert.ToBase64String(salt)}\"/></w:settings>";
        Document d = WithSettings(settings);
        Assert.True(d.Protection.IsEnforced);
        Assert.Equal(DocumentEditRestriction.Comments, d.Protection.Restriction);
        Assert.True(d.Protection.HasPassword);
        Assert.Null(d.Protection.Verifier);
        Assert.Equal(PasswordCheck.Match, d.Protection.Verify("Example"));
        Assert.Equal(PasswordCheck.Mismatch, d.Protection.Verify("example"));
        // setting a new protection replaces the old attributes
        d.Protection.Protect(DocumentEditRestriction.Comments, "Example", false, new ProtectionOptions { SpinCount = 5 });
        string xml = Settings(d);
        Assert.DoesNotContain("cryptAlgorithmSid", xml);
        Assert.DoesNotContain("w:hash=", xml);
        Assert.Equal(PasswordCheck.Match, d.Protection.Verify("Example"));
    }

    [Fact]
    public void UnsupportedAlgorithmAndHostileSpinCount()
    {
        string settings = $"<w:settings xmlns:w=\"{W}\"><w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\" w:algorithmName=\"WHIRLPOOL\" w:hashValue=\"AAAA\" w:saltValue=\"AAAA\" w:spinCount=\"10\"/></w:settings>";
        Assert.Equal(PasswordCheck.UnsupportedAlgorithm, WithSettings(settings).Protection.Verify("x"));
        string hostile = $"<w:settings xmlns:w=\"{W}\"><w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\" w:algorithmName=\"SHA-512\" w:hashValue=\"AAAA\" w:saltValue=\"AAAA\" w:spinCount=\"2000000000\"/></w:settings>";
        OfficeLimitException ex = Assert.Throws<OfficeLimitException>(() => WithSettings(hostile).Protection.Verify("x"));
        Assert.Equal("MaxEncryptionSpinCount", ex.LimitName);
    }

    [Fact]
    public void WriteProtectionRoundTrips()
    {
        using Document d = Document.Create();
        Assert.False(d.WriteProtection.IsPresent);
        d.WriteProtection.Set("reserve", recommended: true, new ProtectionOptions { SpinCount = 20 });
        using var ms = new MemoryStream();
        d.SaveAs(ms, OfficeFormat.Docx);
        ms.Position = 0;
        using Document back = Document.Open(ms);
        Assert.True(back.WriteProtection.IsPresent);
        Assert.True(back.WriteProtection.Recommended);
        Assert.True(back.WriteProtection.HasPassword);
        Assert.Equal(PasswordCheck.Match, back.WriteProtection.Verify("reserve"));
        Assert.Equal(PasswordCheck.Mismatch, back.WriteProtection.Verify("other"));
        Assert.True(back.WriteProtection.Remove());
        Assert.False(back.WriteProtection.IsPresent);
    }

    [Fact]
    public void EditingExceptionsAreWrittenListedAndRemoved()
    {
        using Document d = Document.Create();
        Paragraph a = d.Body.AddParagraph("one");
        Paragraph b = d.Body.AddParagraph("two");
        Paragraph c = d.Body.AddParagraph("three");
        EditingRange x = d.AddEditingException(a, b);
        EditingRange y = d.AddEditingException(c, c, user: "ann@example.com");
        Assert.NotEqual(x.Id, y.Id);
        Assert.Equal("everyone", x.Group);
        Assert.Equal("ann@example.com", y.User);
        string xml = d.MainMarkup.Root.OuterXml;
        Assert.Matches("<w:permStart [^>]*w:edGrp=\"everyone\"[^>]*/><w:p>.*one.*</w:p><w:p>.*two.*</w:p><w:permEnd [^>]*/>", xml);
        Assert.Contains("w:ed=\"ann@example.com\"", xml);
        Assert.Equal(2, d.EditingExceptions().Count);
        Assert.Throws<ArgumentException>(() => d.AddEditingException(c, a));
        Assert.Equal(4, d.RemoveEditingExceptions());
        Assert.Empty(d.EditingExceptions());
        Assert.DoesNotContain("perm", d.MainMarkup.Root.OuterXml);
    }

    private static byte[] Legacy(byte[] input, byte[] salt, int spin)
    {
        byte[] h = System.Security.Cryptography.SHA256.HashData([.. salt, .. input]);
        for (int i = 0; i < spin; i++)
        {
            h = System.Security.Cryptography.SHA256.HashData([.. h, .. BitConverter.GetBytes(i)]);
        }

        return h;
    }

    private static Document WithSettings(string settingsXml)
    {
        InMemoryPackage p = TestDocs.Package("<w:p/>");
        p.AddPart("/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml", Encoding.UTF8.GetBytes(settingsXml));
        p.SetRelationships("/word/document.xml", [new Relationship("rId5", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings", "settings.xml")]);
        return Document.FromPackage(p);
    }

    private static int Occurrences(string s, string needle)
    {
        int n = 0;
        for (int i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = s.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
