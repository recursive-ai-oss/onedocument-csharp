using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using NPOI.XWPF.Extractor;
using NPOI.XWPF.UserModel;
using OpenXmlReader = DocumentFormat.OpenXml.OpenXmlReader;

namespace RecursiveAI.OneOffice.Bench;

/// <summary>
/// The work each benchmark row does, one method per (library, operation), shared by the BenchmarkDotNet classes and by the
/// fresh-process peak-memory runner so both measure exactly the same code. Every row takes the input document's bytes and
/// returns a number derived from the result (so nothing is optimised away) that the caller may ignore.
/// </summary>
/// <remarks>
/// To add a row: write the method here, add it to <see cref="All"/> under its row name (<c>library.operation</c>), and add a
/// <c>[Benchmark]</c> that calls it in the matching class. Comparators run only through their public APIs.
/// </remarks>
public static class Rows
{
    /// <summary>The fixed text the edit rows write into the first paragraph.</summary>
    private const string EditedText = "Edited by the benchmark";

    /// <summary>Every row, by name. The peak-memory runner and the report generator look rows up here.</summary>
    public static IReadOnlyDictionary<string, Func<byte[], long>> All { get; } = new Dictionary<string, Func<byte[], long>>(StringComparer.Ordinal)
    {
        ["rawzip.loadsave"] = RawZipLoadSave,
        ["openxml.loadsave"] = OpenXmlLoadSave,
        ["npoi.loadsave"] = NpoiLoadSave,
        ["rawzip.edit"] = RawZipEdit,
        ["openxml.edit"] = OpenXmlEdit,
        ["npoi.edit"] = NpoiEdit,
        ["rawzip.lazyopen"] = RawZipLazyOpen,
        ["openxml.lazyopen"] = OpenXmlLazyOpen,
        ["npoi.lazyopen"] = NpoiLazyOpen,
        ["openxml.extract.dom"] = OpenXmlExtractDom,
        ["openxml.extract.reader"] = OpenXmlExtractReader,
        ["npoi.extract"] = NpoiExtract,
    };

    /// <summary>Rows waiting for the layer they measure. Listed so the gap is visible, not forgotten.</summary>
    public static IReadOnlyList<string> Pending { get; } =
    [
        "oneoffice.loadsave (package open + save)",
        "oneoffice.edit (change one paragraph, save)",
        "oneoffice.lazyopen (open, read one part)",
        "oneoffice.extract (text extraction)",
    ];

    // Baseline: what any zip-level tool must pay. Entries are read and written back whole through the BCL zip classes.
    /// <summary>Reads every entry and writes it into a new package: the floor for a tool that recompresses.</summary>
    public static long RawZipLoadSave(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using var zin = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zout = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            CopyEntries(zin, zout, null, null);
        }

        return output.Length;
    }

    /// <summary>Opens the package and clones it to a new stream through the SDK.</summary>
    public static long OpenXmlLoadSave(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using WordprocessingDocument doc = WordprocessingDocument.Open(input, false);
        using var output = new MemoryStream();
        using (doc.Clone(output, true))
        {
        }

        return output.Length;
    }

    /// <summary>Loads the document model and writes it out.</summary>
    public static long NpoiLoadSave(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using var doc = new XWPFDocument(input);
        using var output = new MemoryStream();
        doc.Write(output);
        return output.Length;
    }

    /// <summary>Rewrites only <c>word/document.xml</c>, with the first text replaced, and copies the rest through the BCL zip classes.</summary>
    public static long RawZipEdit(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using var zin = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zout = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            CopyEntries(zin, zout, "word/document.xml", ReplaceFirstText);
        }

        return output.Length;
    }

    /// <summary>Opens the package for editing, changes the first text element and saves back.</summary>
    public static long OpenXmlEdit(byte[] src)
    {
        using var ms = new MemoryStream();
        ms.Write(src);
        ms.Position = 0;
        using (WordprocessingDocument doc = WordprocessingDocument.Open(ms, true))
        {
            Text first = doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().First();
            first.Text = EditedText;
        }

        return ms.Length;
    }

    /// <summary>Loads the model, changes the first run's text and writes it out.</summary>
    public static long NpoiEdit(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using var doc = new XWPFDocument(input);
        doc.Paragraphs[0].Runs[0].SetText(EditedText, 0);
        using var output = new MemoryStream();
        doc.Write(output);
        return output.Length;
    }

    /// <summary>Opens the zip and reads one small part's length.</summary>
    public static long RawZipLazyOpen(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        using Stream s = zip.GetEntry("word/styles.xml")!.Open();
        return Drain(s);
    }

    /// <summary>Opens the package and parses the styles part only.</summary>
    public static long OpenXmlLazyOpen(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using WordprocessingDocument doc = WordprocessingDocument.Open(input, false);
        return doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.ChildElements.Count;
    }

    /// <summary>Loads the document model (it has no narrower entry) and reads the core properties.</summary>
    public static long NpoiLazyOpen(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using var doc = new XWPFDocument(input);
        return doc.GetProperties().CoreProperties.Title?.Length ?? 0;
    }

    /// <summary>Counts characters of every <c>w:t</c> through the SDK's DOM.</summary>
    public static long OpenXmlExtractDom(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using WordprocessingDocument doc = WordprocessingDocument.Open(input, false);
        long n = 0;
        foreach (Text t in doc.MainDocumentPart!.Document!.Body!.Descendants<Text>())
        {
            n += t.Text.Length;
        }

        return n;
    }

    /// <summary>Counts characters of every <c>w:t</c> through the SDK's forward reader, its fastest documented way.</summary>
    public static long OpenXmlExtractReader(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using WordprocessingDocument doc = WordprocessingDocument.Open(input, false);
        long n = 0;
        using OpenXmlReader reader = OpenXmlReader.Create(doc.MainDocumentPart!);
        while (reader.Read())
        {
            if (reader.ElementType == typeof(Text) && reader.IsStartElement)
            {
                n += reader.LoadCurrentElement() is Text t ? t.Text.Length : 0;
            }
        }

        return n;
    }

    /// <summary>Extracts the text with the library's extractor class.</summary>
    public static long NpoiExtract(byte[] src)
    {
        using var input = new MemoryStream(src, writable: false);
        using var doc = new XWPFDocument(input);
        var extractor = new XWPFWordExtractor(doc);
        return extractor.Text.Length;
    }

    private static void CopyEntries(ZipArchive from, ZipArchive to, string? rewriteName, Func<byte[], byte[]>? rewrite)
    {
        foreach (ZipArchiveEntry e in from.Entries)
        {
            ZipArchiveEntry created = to.CreateEntry(e.FullName, CompressionLevel.Optimal);
            using Stream dst = created.Open();
            using Stream srcStream = e.Open();
            if (rewrite is not null && e.FullName == rewriteName)
            {
                using var buffer = new MemoryStream();
                srcStream.CopyTo(buffer);
                dst.Write(rewrite(buffer.ToArray()));
            }
            else
            {
                srcStream.CopyTo(dst);
            }
        }
    }

    private static byte[] ReplaceFirstText(byte[] xml)
    {
        // Replaces the content of the first <w:t ...>...</w:t> in place; the markup around it is untouched.
        ReadOnlySpan<byte> open = "<w:t"u8;
        int at = xml.AsSpan().IndexOf(open);
        while (at >= 0 && at + 4 < xml.Length && xml[at + 4] != (byte)'>' && xml[at + 4] != (byte)' ')
        {
            int next = xml.AsSpan(at + 4).IndexOf(open);
            at = next < 0 ? -1 : at + 4 + next;
        }

        if (at < 0)
        {
            return xml;
        }

        int start = at + 4 + xml.AsSpan(at + 4).IndexOf((byte)'>') + 1;
        int end = start + xml.AsSpan(start).IndexOf((byte)'<');
        byte[] text = System.Text.Encoding.UTF8.GetBytes(EditedText);
        byte[] result = new byte[xml.Length - (end - start) + text.Length];
        xml.AsSpan(0, start).CopyTo(result);
        text.CopyTo(result, start);
        xml.AsSpan(end).CopyTo(result.AsSpan(start + text.Length));
        return result;
    }

    private static long Drain(Stream s)
    {
        byte[] buf = new byte[16 * 1024];
        long n = 0;
        int r;
        while ((r = s.Read(buf, 0, buf.Length)) > 0)
        {
            n += r;
        }

        return n;
    }
}
