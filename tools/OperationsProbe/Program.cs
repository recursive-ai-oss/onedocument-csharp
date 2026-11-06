using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Revisions;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Tools.OperationsProbe;

/// <summary>
/// Corpus driver for find and replace.
///
///   OperationsProbe run INPUT_DIR [--pattern REGEX] [--limit N] [--shard I/N]
///
/// For every DOCX, XLSX and PPTX under INPUT_DIR the text is read through the operation's own Find, the same replacement is made with
/// a regular expression and compared with what a plain .NET Regex.Replace gives on that text (after the replacement, after saving and
/// reopening, and for a document the same replacement made as tracked changes, accepted and rejected). One JSON line per file with the
/// counts, the typed issues and OfficeException codes, and the checks that failed. Files are only read.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] != "run")
        {
            Console.Error.WriteLine("usage: OperationsProbe run INPUT_DIR [--pattern REGEX] [--limit N] [--shard I/N] [--list FILE]");
            return 2;
        }

        string pattern = @"\b(?:the|of|and)\b";
        int limit = int.MaxValue;
        int shard = 0;
        int shards = 1;
        string? list = null;
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pattern":
                    pattern = args[++i];
                    break;
                case "--verbose":
                    Verbose = true;
                    break;
                case "--list":
                    list = args[++i];
                    break;
                case "--limit":
                    limit = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--shard":
                    string[] p = args[++i].Split('/');
                    shard = int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                    shards = int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
        }

        string[] files = [.. Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)];
        if (list is not null)
        {
            var wanted = new HashSet<string>(File.ReadAllLines(list), StringComparer.Ordinal);
            files = [.. files.Where(f => wanted.Contains(Path.GetRelativePath(args[1], f)))];
        }

        int n = 0;
        int bad = 0;
        for (int i = shard; i < files.Length && n < limit; i += shards, n++)
        {
            string result = Check(files[i], args[1], pattern);
            Console.Out.WriteLine(result);
            if (result.Contains("\"failed\":[\"", StringComparison.Ordinal))
            {
                bad++;
            }
        }

        Console.Error.WriteLine("files " + n + ", with failed checks " + bad);
        return bad == 0 ? 0 : 1;
    }

    private static bool Verbose;

    // The markup of the first paragraph whose text differs, with letters and digits masked: the structure without the content.
    private static void ShowShape(Func<Document> open, int k)
    {

        using Document d = open();
        TextHit? hit = d.Find(@"[\s\S]+", new DocumentFindOptions { Search = new SearchOptions { UseRegex = true } }).ElementAtOrDefault(k);
        if (hit is not null && hit.Location.Story == "textbox" && hit.Location.Container == d.MainPartName)
        {
            var flat = new List<(Paragraph P, TextBox Box)>();
            void Walk(IReadOnlyList<TextBox> boxes)
            {
                foreach (TextBox box in boxes)
                {
                    foreach (Paragraph q in box.Paragraphs())
                    {
                        flat.Add((q, box));
                    }

                    Walk(box.TextBoxes());
                }
            }

            Walk(d.Body.TextBoxes());
            if (hit.Location.Index < flat.Count)
            {
                (Paragraph tp, TextBox tb) = flat[hit.Location.Index];
                Console.Error.WriteLine("SHAPE textbox kind " + tb.Kind + " alternate " + tb.Alternate.IsValid + " boxes in body " + d.Body.TextBoxes().Count + " flat " + flat.Count + " " + Mask(tp.Markup.Node.OuterXml));
            }

            return;
        }

        if (hit is null || hit.Location.Story != "body")
        {
            Console.Error.WriteLine("SHAPE paragraph " + k + " story " + hit?.Location.Story + " part " + hit?.Location.Container);
            return;
        }

        Paragraph p = d.Body.Paragraphs().ElementAt(hit.Location.Index);
        Console.Error.WriteLine("SHAPE paragraph " + k + " " + Mask(p.Markup.Node.OuterXml));
    }

    private static string Mask(string xml) => Regex.Replace(xml, @"(?<=>)[^<]+(?=<)", m => Regex.Replace(m.Value, @"\p{L}", "a"));

    private static string Check(string path, string root, string pattern)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var failed = new List<string>();
        int items = 0;
        int issues = 0;
        string status = "ok";
        string? code = null;
        string? issueNote = null;
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            var rx = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var options = new SearchOptions { UseRegex = true };
            const string All = @"[\s\S]+";
            if (ext == ".docx")
            {
                Func<Document> open = () => Document.Open(new MemoryStream(bytes));
                Func<Document, string[]> snapNow = d => [.. d.Find(All, new DocumentFindOptions { Search = options }).Select(h => h.Text)];
                using Document d0 = open();
                string[] before = snapNow(d0);
                string[] expected = [.. before.Select(t => rx.Replace(t, "X")).Where(t => t.Length > 0)];
                ReplaceReport r = d0.Replace(pattern, "X", new DocumentReplaceOptions { Search = new SearchOptions { UseRegex = true } });
                items = r.Count;
                issues = r.Issues.Count;
                issueNote ??= r.Issues.Count > 0 ? r.Issues[0].Code + ": " + r.Issues[0].Message : null;
                Compare("replace", snapNow(d0), expected, failed);
                if (Verbose && !snapNow(d0).SequenceEqual(expected, StringComparer.Ordinal))
                {
                    int at = 0;
                    string[] now = snapNow(d0);
                    while (at < now.Length && at < expected.Length && now[at] == expected[at])
                    {
                        at++;
                    }

                    ShowShape(open, at);
                }

                using var ms = new MemoryStream();
                d0.SaveAs(ms, OfficeFormat.Docx);
                ms.Position = 0;
                using (Document back = Document.Open(ms))
                {
                    Compare("reopen", snapNow(back), expected, failed);
                }

                // Accepting resolves the document's own tracked changes too (a deleted paragraph mark joins paragraphs), so the
                // expected text is the same document accepted without a replacement, then replaced.
                string[] acceptedExpected;
                using (Document b = open())
                {
                    b.Revisions.AcceptAll();
                    acceptedExpected = [.. snapNow(b).Select(t => rx.Replace(t, "X")).Where(t => t.Length > 0)];
                }

                using (Document t1 = open())
                {
                    ReplaceReport tr = t1.Replace(pattern, "X", new DocumentReplaceOptions { Search = new SearchOptions { UseRegex = true }, TrackedAuthor = "probe", TrackedIds = RevisionIds.ContinueAfterMax });
                    if (tr.Count != r.Count)
                    {
                        failed.Add("tracked-count");
                    }

                    t1.Revisions.AcceptAll();
                    string[] gotAccepted = snapNow(t1);
                    Compare("accept", gotAccepted, acceptedExpected, failed, joined: true);
                    if (Verbose && string.Concat(gotAccepted) != string.Concat(acceptedExpected))
                    {
                        string a = string.Concat(gotAccepted);
                        string e = string.Concat(acceptedExpected);
                        int pos = 0;
                        while (pos < a.Length && pos < e.Length && a[pos] == e[pos])
                        {
                            pos++;
                        }

                        int para = 0;
                        for (int sum = 0; para < acceptedExpected.Length && sum + acceptedExpected[para].Length <= pos; para++)
                        {
                            sum += acceptedExpected[para].Length;
                        }

                        Console.Error.WriteLine("ACCEPT textboxes in body now " + t1.Body.TextBoxes().Count + ", hits " + t1.Find("[\\s\\S]+", new DocumentFindOptions { Search = options, Stories = DocumentStories.TextBoxes }).Count);
                        Console.Error.WriteLine("ACCEPT diverges in paragraph " + para + " of " + acceptedExpected.Length + " (actual has " + gotAccepted.Length + ")");
                        ShowShape(open, para);
                    }
                }

                // A document that already had tracked changes loses them on reject-all too, so the baseline is the same
                // document with the changes rejected and nothing replaced.
                string[] rejectedBaseline;
                using (Document b = open())
                {
                    b.Revisions.RejectAll();
                    rejectedBaseline = snapNow(b);
                }

                using (Document t2 = open())
                {
                    _ = t2.Replace(pattern, "X", new DocumentReplaceOptions { Search = new SearchOptions { UseRegex = true }, TrackedAuthor = "probe" });
                    t2.Revisions.RejectAll();
                    Compare("reject", snapNow(t2), rejectedBaseline, failed, joined: true);
                }
            }
            else if (ext == ".xlsx")
            {
                using Workbook w0 = Workbook.Open(new MemoryStream(bytes));
                string[] before = [.. w0.Find(All, new WorkbookFindOptions { Search = options }).Select(h => h.Text)];
                string[] expected = [.. before.Select(t => rx.Replace(t, "X")).Where(t => t.Length > 0)];
                ReplaceReport r = w0.Replace(pattern, "X", new WorkbookReplaceOptions { Search = options });
                items = r.Count;
                issues = r.Issues.Count;
                issueNote ??= r.Issues.Count > 0 ? r.Issues[0].Code + ": " + r.Issues[0].Message : null;
                Compare("replace", [.. w0.Find(All, new WorkbookFindOptions { Search = options }).Select(h => h.Text)], expected, failed);
                using var ms = new MemoryStream();
                w0.SaveAs(ms, w0.Format);
                ms.Position = 0;
                using Workbook back = Workbook.Open(ms);
                Compare("reopen", [.. back.Find(All, new WorkbookFindOptions { Search = options }).Select(h => h.Text)], expected, failed);
            }
            else
            {
                using Presentation p0 = Presentation.Open(new MemoryStream(bytes));
                var all = new PresentationFindOptions { Search = options, Parts = PresentationParts.All };
                string[] before = [.. p0.Find(All, all).Select(h => h.Text)];
                string[] expected = [.. before.Select(t => rx.Replace(t, "X")).Where(t => t.Length > 0)];
                ReplaceReport r = p0.Replace(pattern, "X", new PresentationReplaceOptions { Search = options, Parts = PresentationParts.All });
                items = r.Count;
                issues = r.Issues.Count;
                issueNote ??= r.Issues.Count > 0 ? r.Issues[0].Code + ": " + r.Issues[0].Message : null;
                Compare("replace", [.. p0.Find(All, all).Select(h => h.Text)], expected, failed);
                using var ms = new MemoryStream();
                p0.SaveAs(ms, OfficeFormat.Pptx);
                ms.Position = 0;
                using Presentation back = Presentation.Open(ms);
                Compare("reopen", [.. back.Find(All, all).Select(h => h.Text)], expected, failed);
            }
        }
        catch (OfficeException ex)
        {
            status = "typed-error";
            code = ex.Defect.Code;
        }

        if (issues > 0)
        {
            // Matches the operation reported and left alone make the text differ from a plain replacement by design.
            failed.RemoveAll(f => f is "replace" or "reopen" or "accept");
        }

        var buf = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buf))
        {
            w.WriteStartObject();
            w.WriteString("file", Path.GetRelativePath(root, path));
            w.WriteString("status", status);
            if (code is not null)
            {
                w.WriteString("code", code);
            }

            w.WriteNumber("replacements", items);
            w.WriteNumber("issues", issues);
            if (issueNote is not null)
            {
                w.WriteString("issue", issueNote);
            }

            w.WriteStartArray("failed");
            foreach (string f in failed)
            {
                w.WriteStringValue(f);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    // Texts compared in order; paragraphs and cells whose text is empty after the replacement are not listed by a search.
    private static void Compare(string name, string[] actual, string[] expected, List<string> failed, bool joined = false)
    {
        bool same = joined ? string.Concat(actual) == string.Concat(expected) : actual.SequenceEqual(expected, StringComparer.Ordinal);
        if (!same && Verbose)
        {
            // Only positions, lengths and code points are printed: the corpus text itself stays where it is.
            string a = joined ? string.Concat(actual) : string.Join("\u0001", actual);
            string e = joined ? string.Concat(expected) : string.Join("\u0001", expected);
            int k = 0;
            while (k < a.Length && k < e.Length && a[k] == e[k])
            {
                k++;
            }

            string Cp(string x) => string.Join(" ", x.Skip(Math.Max(0, k - 3)).Take(10).Select(c => ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
            Console.Error.WriteLine("DIFF " + name + " at " + k + " lengths " + a.Length + "/" + e.Length + " counts " + actual.Length + "/" + expected.Length + " actual[" + Cp(a) + "] expected[" + Cp(e) + "]");
        }

        if (!same)
        {
            failed.Add(name);
        }
    }
}
