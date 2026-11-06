using System.Text;
using RecursiveAI.OneArchive.Cfb;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Redaction.Tests;

/// <summary>Runs every adversarial input through redaction and then tries to read the secret back from the output.</summary>
public sealed class AttackTests
{
    static AttackTests()
    {
        DocumentFormats.Register(FormatRegistry.Default);
        SpreadsheetFormats.Register(FormatRegistry.Default);
        PresentationFormats.Register(FormatRegistry.Default);
    }

    public sealed record Outcome(string Name, string Secret, List<string> Staged, List<string> Recoverable, bool Verified, List<string> Gaps, List<string> Survivors, byte[] Output);

    // The ways of hiding a secret that no part of the file can undo: the report must say so (Verified is false), but nothing is rewritten.
    private static readonly Dictionary<string, string> Reported = new(StringComparer.Ordinal)
    {
        ["namespace-uri-holds-secret"] = "a namespace URI is not rewritten",
        ["element-and-attribute-names"] = "element and attribute names are not rewritten",
        ["relationship-id-holds-secret"] = "relationship identifiers are not rewritten",
        ["latin1-declared-xml-part"] = "an XML part in a legacy encoding is searched as bytes only",
        ["internal-relationship-target-name"] = "part names are not rewritten",
        ["content-type-override-and-default"] = "part names are not rewritten",
        ["embedded-fonts-and-media-names"] = "part names are not rewritten",
        ["vba-project-split-by-compression"] = "compressed macro source is verified, not rewritten",
        ["ole-package-holds-zip"] = "a package stored inside an object stream is verified, not rewritten",
        ["gzip-part"] = "a compressed part is verified, not rewritten",
        ["alt-chunk-rtf-escapes"] = "an included RTF file is verified, not rewritten",
        ["alt-chunk-quoted-printable"] = "an included mail archive is verified, not rewritten",
    };

    // Matching is exact on the characters of the text; these hide the secret behind characters a reader does not see or treats as equal.
    private static readonly Dictionary<string, string> NotMatched = new(StringComparer.Ordinal)
    {
        ["soft-hyphen-and-zero-width"] = "invisible characters inside the secret",
        ["decomposed-unicode"] = "the secret in another Unicode normalisation form",
        ["non-breaking-space-in-name"] = "a no-break space where the plan has a space",
        ["curly-apostrophe-in-name"] = "a typographic apostrophe where the plan has a straight one",
    };

    // Cases whose staged file the independent reader cannot read the secret from (RTF escapes, quoted-printable, folding of a typographic mark).
    private static readonly HashSet<string> NoStagedReading = new(StringComparer.Ordinal)
    {
        "orphan-thumbnail", "zip-entry-comments-and-archive-comment", "zip-unicode-path-extra-field", "alt-chunk-rtf-escapes", "alt-chunk-quoted-printable", "curly-apostrophe-in-name", "png-compressed-text-chunk",
    };

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (AttackCase c in Attacks.All())
        {
            data.Add(c.Name);
        }

        return data;
    }

    private static OfficeFile Open(AttackCase c)
    {
        var ms = new MemoryStream(c.Input);
        OpenContext? ctx = c.Password is null ? null : new OpenContext(password: c.Password);
        return c.Kind switch
        {
            OfficeKind.Document => Document.Open(ms, ctx),
            OfficeKind.Workbook => Workbook.Open(ms, ctx),
            _ => Presentation.Open(ms, ctx),
        };
    }

    internal static Outcome Attack(AttackCase c)
    {
        string secret = c.Secret;
        using (OfficeFile probe = Open(c))
        {
            if (secret.StartsWith('@'))
            {
                secret = probe.ExtractText().Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).First(static w => w.Length >= 5 && w.All(char.IsLetter));
            }
        }

        string[] secrets = c.IgnoreCase ? [secret, secret.ToLowerInvariant()] : [secret];

        // The same file through the staging step with nothing to remove: where the secret is readable before redaction.
        using OfficeFile first = Open(c);
        var none = new RedactionPlan();
        none.Matches.Add(RedactionMatch.Literal("\u0001no such text\u0001"));
        using var stagedOutput = new MemoryStream();
        first.Redact(none, stagedOutput);
        List<string> staged = Attacker.Recover(stagedOutput.ToArray(), secrets);

        using OfficeFile file = Open(c);
        var plan = new RedactionPlan();
        plan.Matches.Add(RedactionMatch.Literal(secret, c.IgnoreCase));
        using var output = new MemoryStream();
        RedactionReport report = file.Redact(plan, output);
        byte[] bytes = output.ToArray();
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_REDACTION_DUMP") is { Length: > 0 } dump)
        {
            Directory.CreateDirectory(dump);
            File.WriteAllBytes(Path.Combine(dump, c.Name + ".zip"), bytes);
            File.WriteAllBytes(Path.Combine(dump, c.Name + ".input.zip"), c.Input);
        }

        return new Outcome(c.Name, secret, staged, Attacker.Recover(bytes, secrets), report.Verified, [.. report.Gaps.Select(static g => g.Code)], [.. report.Survivors.Select(static s => s.ToString() ?? string.Empty)], bytes);
    }

    /// <summary>Every adversarial input: the secret must be gone from everything a reader can decode, or the report must say it is not.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheSecretCannotBeReadBack(string name)
    {
        AttackCase c = Attacks.All().First(x => x.Name == name);
        Outcome o = Attack(c);
        if (NotMatched.TryGetValue(name, out string? why))
        {
            // Open design question, written down in docs/redaction.md: the plan's literal does not match this text, so nothing is removed.
            Assert.Skip("Matching is exact: " + why + ". See docs/redaction.md, Matching.");
        }

        if (!NoStagedReading.Contains(name))
        {
            Assert.True(o.Staged.Count > 0, "the attack hides a secret the independent reader finds before redaction: " + name);
        }

        if (Reported.TryGetValue(name, out string? limit))
        {
            // Not removed (" + limit + "), so the report must not call the file verified.
            Assert.False(o.Verified, limit);
            Assert.NotEmpty(o.Survivors.Concat(o.Gaps));
            return;
        }

        Assert.Empty(o.Recoverable);
        Assert.True(o.Verified);
        Assert.Empty(o.Survivors);
    }

    [Fact]
    public void ThumbnailsThatNothingPointsToAreDropped()
    {
        Outcome o = Attack(Attacks.All().First(x => x.Name == "orphan-thumbnail"));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(o.Output));
        Assert.DoesNotContain(zip.Entries, static e => e.FullName.StartsWith("docProps/thumbnail", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("zip-entry-comments-and-archive-comment")]
    [InlineData("zip-unicode-path-extra-field")]
    public void ZipHeaderTextIsInTheInputAndGoneFromTheOutput(string name)
    {
        AttackCase c = Attacks.All().First(x => x.Name == name);
        Assert.NotEmpty(Attacker.Recover(c.Input, Attacks.T));
        Assert.Empty(Attacker.Recover(Attack(c).Output, Attacks.T));
    }

    [Fact]
    public void ZipCommentsAndExtraFieldsAreBlankedAndTheArchiveStillOpens()
    {
        Outcome o = Attack(Attacks.All().First(x => x.Name == "zip-entry-comments-and-archive-comment"));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(o.Output));
        Assert.Equal(3, zip.Entries.Count);
        Assert.All(zip.Entries, static e => Assert.DoesNotContain("QUOKKA", e.Comment, StringComparison.Ordinal));
        using var again = new MemoryStream(o.Output);
        using Document doc = Document.Open(again);
        Assert.Contains("x", doc.ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void FragmentedCompoundFileKeepsItsStructureAndLosesOnlyTheSecret()
    {
        AttackCase c = Attacks.All().First(x => x.Name == "ole-object-fragmented-compound-file");
        Outcome o = Attack(c);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(o.Output));
        using var ms = new MemoryStream();
        using (Stream s = zip.GetEntry("word/embeddings/oleObject1.bin")!.Open())
        {
            s.CopyTo(ms);
        }

        using var reader = new CompoundFileReader(ms.ToArray());
        using Stream data = reader.OpenStream("Data");
        var bytes = new byte[8 * 512];
        Assert.Equal(bytes.Length, data.Read(bytes, 0, bytes.Length));
        Assert.Equal(Attacks.T.Length, bytes.AsSpan().Count((byte)'X'));
        Assert.Equal(0, bytes.AsSpan(0, 504).ToArray().Count(static b => b != 0));
    }

    [Fact]
    public void PatternsOnTextInsideEncodingsMatchTextAndLeaveOtherBytesAlone()
    {
        // A base64 value that holds text is searched with the pattern; one that holds a hash is not touched, however many digits it contains.
        byte[] hash = [0x12, 0x34, 0xA0, 0x35, 0x36, 0xF1, 0x37, 0x38, 0x91, 0x39, 0x31, 0xC2, 0x32, 0x33, 0x8E, 0x34, 0x35, 0xB7, 0x36, 0x37];
        string hashText = Convert.ToBase64String(hash);
        byte[] input = Attacks.Docx("<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .Add("/customXml/item1.xml", "application/xml", "<root><blob>" + Attacks.Base64("account 123456789 end") + "</blob><hash>" + hashText + "</hash></root>")
            .Rel("/word/document.xml", "rId10", "customXml", "../customXml/item1.xml"));
        var plan = new RedactionPlan();
        plan.Matches.Add(RedactionMatch.Pattern(@"\d{6,}"));
        using var output = new MemoryStream();
        using (Document doc = Document.Open(new MemoryStream(input)))
        {
            RedactionReport report = doc.Redact(plan, output);
            Assert.True(report.Verified);
        }

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(output.ToArray()));
        using var reader = new StreamReader(zip.GetEntry("customXml/item1.xml")!.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("<hash>" + hashText + "</hash>", xml, StringComparison.Ordinal);
        string blob = xml[(xml.IndexOf("<blob>", StringComparison.Ordinal) + 6)..xml.IndexOf("</blob>", StringComparison.Ordinal)];
        Assert.Equal("account XXXXXXXXX end", Encoding.UTF8.GetString(Convert.FromBase64String(blob)));
    }

    [Fact]
    public void EscapedCharactersAreWrittenBackEscapedWhereExcelNeedsThem()
    {
        // The string holds a line feed and an escaped control character around the secret; only the secret changes.
        byte[] input = Attacks.Xlsx("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>", "<si><t xml:space=\"preserve\">a_x0001_b _x0051_UOKKA4419 _x005F_x0041_ end</t></si>");
        var plan = new RedactionPlan();
        plan.Matches.Add(RedactionMatch.Literal(Attacks.T));
        using var output = new MemoryStream();
        using (Workbook wb = Workbook.Open(new MemoryStream(input)))
        {
            Assert.True(wb.Redact(plan, output).Verified);
        }

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(output.ToArray()));
        using var reader = new StreamReader(zip.GetEntry("xl/sharedStrings.xml")!.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("a_x0001_b \u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2588 _x005F_x0041_ end", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ADoublyEncodedLinkTargetIsDecodedBeforeMatching()
    {
        byte[] input = Attacks.Docx("<w:p><w:hyperlink r:id=\"rId10\"><w:r><w:t>x</w:t></w:r></w:hyperlink></w:p>", b => b
            .Rel("/word/document.xml", "rId10", "hyperlink", "http://example.com/a%2520b/%2551UOKKA4419", external: true));
        var plan = new RedactionPlan();
        plan.Matches.Add(RedactionMatch.Literal(Attacks.T));
        using var output = new MemoryStream();
        using (Document doc = Document.Open(new MemoryStream(input)))
        {
            Assert.True(doc.Redact(plan, output).Verified);
        }

        Assert.Empty(Attacker.Recover(output.ToArray(), Attacks.T));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(output.ToArray()));
        using var reader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        Assert.Contains("a%2520b/REDACTEDA", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void LettersOfAnIdentifierMarkElsewhereAreStillSurvivors()
    {
        // "RED" is part of the mark that replaces the bookmark name; an element named RED is not covered by that mark.
        byte[] input = Attacks.Docx("<w:p><w:bookmarkStart w:id=\"1\" w:name=\"Secret99\"/><w:r><w:t>x</w:t></w:r><w:bookmarkEnd w:id=\"1\"/></w:p><RED/>");
        var plan = new RedactionPlan();
        plan.Matches.Add(RedactionMatch.Literal("Secret99"));
        plan.Matches.Add(RedactionMatch.Literal("RED"));
        using var output = new MemoryStream();
        using Document doc = Document.Open(new MemoryStream(input));
        RedactionReport report = doc.Redact(plan, output);
        Assert.False(report.Verified);
        Assert.Contains(report.Survivors, static s => s.Location.EndsWith("(name)", StringComparison.Ordinal));
    }
}
