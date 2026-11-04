using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents.Revisions;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Tools.RevisionProbe;

/// <summary>
/// Corpus driver for the revision model.
///
///   RevisionProbe batch INPUT_DIR OUT_DIR [--shard I/N] [--limit N]
///       For every Word file under INPUT_DIR: count its tracked changes, and for files that have any, write the
///       accept-all and reject-all results as .docx files plus the text of each view (before and after) into OUT_DIR.
///       One JSON line per file on standard output.
///   RevisionProbe text FILE accepted|rejected
///       Prints the text of the main document in one view.
///   RevisionProbe resolve FILE OUT accept|reject
///       Writes the file with every tracked change accepted or rejected.
///
/// Only the parts that carry text and styles are read; every other entry is copied as it is.
/// </summary>
internal static class Program
{
    private static readonly string[] s_extensions = [".docx", ".docm", ".dotx", ".dotm"];

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: RevisionProbe batch IN OUT [--shard I/N] [--limit N] | text FILE accepted|rejected | resolve FILE OUT accept|reject");
            return 2;
        }

        switch (args[0])
        {
            case "batch":
                return Batch(args);
            case "text":
                {
                    using ZipArchive zip = ZipFile.OpenRead(args[1]);
                    var (parts, _) = Load(zip, null);
                    var view = args[2] == "rejected" ? RevisionView.Rejected : RevisionView.Accepted;
                    foreach (RevisionPart p in parts.Where(IsText))
                    {
                        Console.Out.Write(RevisionSet.GetText(p, view));
                    }

                    return 0;
                }

            case "resolve":
                {
                    using ZipArchive zip = ZipFile.OpenRead(args[1]);
                    var (parts, _) = Load(zip, null);
                    RevisionSet set = RevisionSet.Scan(parts);
                    int n = args[3] == "accept" ? set.AcceptAll() : set.RejectAll();
                    WriteZip(zip, parts, args[2]);
                    Console.WriteLine(n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return 0;
                }

            default:
                return 2;
        }
    }

    private static bool IsText(RevisionPart p) =>
        p.Uri.StartsWith("/word/document", StringComparison.Ordinal) || p.Uri.StartsWith("/word/header", StringComparison.Ordinal) ||
        p.Uri.StartsWith("/word/footer", StringComparison.Ordinal) || p.Uri.StartsWith("/word/footnotes", StringComparison.Ordinal) ||
        p.Uri.StartsWith("/word/endnotes", StringComparison.Ordinal) || p.Uri.StartsWith("/word/comments", StringComparison.Ordinal);

    private static bool Wanted(string name)
    {
        if (!name.StartsWith("word/", StringComparison.Ordinal) || !name.EndsWith(".xml", StringComparison.Ordinal))
        {
            return false;
        }

        string file = name[(name.LastIndexOf('/') + 1)..];
        return file.StartsWith("document", StringComparison.Ordinal) || file.StartsWith("header", StringComparison.Ordinal) ||
            file.StartsWith("footer", StringComparison.Ordinal) || file.StartsWith("footnotes", StringComparison.Ordinal) ||
            file.StartsWith("endnotes", StringComparison.Ordinal) || file.StartsWith("comments", StringComparison.Ordinal) ||
            file == "styles.xml" || file == "numbering.xml";
    }

    private static (List<RevisionPart> Parts, DefectCollection Defects) Load(ZipArchive zip, DefectCollection? sink)
    {
        var defects = sink ?? new DefectCollection();
        var ctx = new OpenContext(defects: defects);
        var parts = new List<RevisionPart>();
        foreach (ZipArchiveEntry e in zip.Entries.Where(e => Wanted(e.FullName)).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            if (e.Length > 256L * 1024 * 1024)
            {
                continue;
            }

            using Stream s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            parts.Add(new RevisionPart("/" + e.FullName, MarkupDocument.Parse(ms.ToArray(), ctx, "/" + e.FullName)));
        }

        return (parts, defects);
    }

    private static void WriteZip(ZipArchive source, List<RevisionPart> parts, string path)
    {
        var changed = parts.Where(p => p.Markup.IsModified).ToDictionary(p => p.Uri[1..], p => p, StringComparer.Ordinal);
        using FileStream fs = File.Create(path);
        using var outZip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (ZipArchiveEntry e in source.Entries)
        {
            ZipArchiveEntry o = outZip.CreateEntry(e.FullName, CompressionLevel.Fastest);
            using Stream w = o.Open();
            if (changed.TryGetValue(e.FullName, out RevisionPart? p) && p is not null)
            {
                w.Write(p.Markup.ToArray());
            }
            else
            {
                using Stream r = e.Open();
                r.CopyTo(w);
            }
        }
    }

    private static int Batch(string[] args)
    {
        string input = args[1];
        string output = args[2];
        int shard = 0;
        int shards = 1;
        int limit = int.MaxValue;
        for (int i = 3; i + 1 < args.Length; i += 2)
        {
            if (args[i] == "--shard")
            {
                string[] sp = args[i + 1].Split('/');
                shard = int.Parse(sp[0], System.Globalization.CultureInfo.InvariantCulture);
                shards = int.Parse(sp[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--limit")
            {
                limit = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        Directory.CreateDirectory(output);
        var files = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
            .Where(f => s_extensions.Contains(Path.GetExtension(f).ToLowerInvariant(), StringComparer.Ordinal))
            .Order(StringComparer.Ordinal).Where((_, i) => i % shards == shard).Take(limit).ToList();
        using Stream stdout = Console.OpenStandardOutput();
        foreach (string file in files)
        {
            string rel = Path.GetRelativePath(input, file);
            string id = rel.Replace('/', '_').Replace('\\', '_');
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("file", rel);
                try
                {
                    One(file, Path.Combine(output, id), w);
                }
                catch (Exception ex) when (ex is OfficeException or InvalidDataException or IOException or InvalidOperationException or ArgumentException or NotSupportedException)
                {
                    w.WriteString("status", "failed");
                    w.WriteString("failure", ex.GetType().Name + (ex is OfficeException oe ? ":" + oe.Defect.Code : string.Empty));
                    w.WriteString("message", ex.Message.Length > 200 ? ex.Message[..200] : ex.Message);
                }

                w.WriteEndObject();
            }

            stdout.Write(ms.ToArray());
            stdout.WriteByte((byte)'\n');
            stdout.Flush();
        }

        return 0;
    }

    private static void One(string file, string outBase, Utf8JsonWriter w)
    {
        using ZipArchive zip = ZipFile.OpenRead(file);
        var (parts, defects) = Load(zip, null);
        var scanSink = new DefectCollection();
        RevisionSet set = RevisionSet.Scan(parts, new OpenContext(defects: scanSink));
        RevisionCensus c = set.Census();
        w.WriteString("status", "ok");
        w.WriteNumber("revisions", c.Total);
        w.WriteStartObject("kinds");
        foreach (var kv in c.ByKind.OrderBy(k => k.Key))
        {
            w.WriteNumber(kv.Key.ToString(), kv.Value);
        }

        w.WriteEndObject();
        w.WriteNumber("authors", c.ByAuthor.Count);
        w.WriteStartObject("parts");
        foreach (var kv in c.ByPart.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            w.WriteNumber(kv.Key, kv.Value);
        }

        w.WriteEndObject();
        w.WriteStartArray("scanDefects");
        foreach (string code in scanSink.Select(d => d.Code).Distinct(StringComparer.Ordinal))
        {
            w.WriteStringValue(code);
        }

        w.WriteEndArray();
        w.WriteNumber("parseDefects", defects.Count);
        if (c.Total == 0)
        {
            return;
        }

        // Accept all.
        string acceptedBefore = ViewText(parts, RevisionView.Accepted);
        string rejectedBefore = ViewText(parts, RevisionView.Rejected);
        int accepted = set.AcceptAll();
        string acceptedAfter = ViewText(parts, RevisionView.Accepted);
        RevisionSet after = RevisionSet.Scan(parts);
        w.WriteNumber("accepted", accepted);
        w.WriteNumber("acceptedLeft", after.Revisions.Count);
        w.WriteBoolean("acceptViewEqualsResult", acceptedBefore == acceptedAfter);
        WriteZip(zip, parts, outBase + ".accepted.docx");
        File.WriteAllText(outBase + ".accepted.txt", MainText(parts, RevisionView.Accepted), new UTF8Encoding(false));

        // Reject all, from a fresh read.
        var (parts2, _) = Load(zip, null);
        var rejectSink = new DefectCollection();
        RevisionSet set2 = RevisionSet.Scan(parts2, new OpenContext(defects: rejectSink));
        int rejected = set2.RejectAll();
        string rejectedAfter = ViewText(parts2, RevisionView.Accepted);
        RevisionSet after2 = RevisionSet.Scan(parts2);
        w.WriteNumber("rejected", rejected);
        w.WriteNumber("rejectedLeft", after2.Revisions.Count);
        w.WriteBoolean("rejectViewEqualsResult", rejectedBefore == rejectedAfter);
        WriteZip(zip, parts2, outBase + ".rejected.docx");
        File.WriteAllText(outBase + ".rejected.txt", MainText(parts2, RevisionView.Accepted), new UTF8Encoding(false));
    }

    private static string MainText(List<RevisionPart> parts, RevisionView view) =>
        string.Concat(parts.Where(p => p.Uri == "/word/document.xml").Select(p => RevisionSet.GetText(p, view)));

    private static string ViewText(List<RevisionPart> parts, RevisionView view)
    {
        var sb = new StringBuilder();
        foreach (RevisionPart p in parts.Where(IsText))
        {
            sb.Append(RevisionSet.GetText(p, view));
        }

        return sb.ToString();
    }
}
