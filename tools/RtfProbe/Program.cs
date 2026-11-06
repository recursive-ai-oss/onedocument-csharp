using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;

// RtfProbe: reads and writes RTF in bulk and reports what happened.
//
//   RtfProbe read <dir> <out.jsonl> [--docx DIR] [--rtf DIR] [--txt DIR]
//       every .rtf under <dir>: opens it, counts paragraphs and tables, lists defects and loss codes, and writes
//       NAME.docx (--docx), the RTF written back from the model (--rtf; its text is compared with the original's) and NAME.ours.txt (--txt)
//   RtfProbe write <dir> <outdir>
//       every .docx under <dir> becomes NAME.rtf through the exporter, with one JSON line each on stdout (loss codes, reread text equal)
//   RtfProbe text <dir> <ext> <outdir>
//       the text of every file with that extension (docx, rtf, ...) through Office.ExtractText, as NAME.txt
//   RtfProbe similar <dirA> <dirB>
//       for every NAME.txt in both directories, the similarity of the two texts (Dice coefficient of character 5-grams of the
//       lower-cased text without white space), one JSON line each and a summary line
//
// One JSON object per file; the last line is a summary. Exit status 1 when a file raised anything but a typed error.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static int Main(string[] args)
    {
        DocumentFormats.Register(FormatRegistry.Default);
        if (args.Length >= 3 && args[0] == "read")
        {
            return Read(args);
        }

        if (args.Length >= 3 && args[0] == "write")
        {
            return Write(args[1], args[2]);
        }

        if (args.Length >= 4 && args[0] == "text")
        {
            return TextOf(args[1], args[2], args[3]);
        }

        if (args.Length >= 3 && args[0] == "similar")
        {
            return Similar(args[1], args[2]);
        }

        Console.Error.WriteLine("usage: RtfProbe read <dir> <out.jsonl> [--docx DIR] [--rtf DIR] [--txt DIR] | RtfProbe write <dir> <outdir> | RtfProbe text <dir> <ext> <outdir> | RtfProbe similar <dirA> <dirB>");
        return 2;
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string Norm(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (!char.IsWhiteSpace(c) && c != '\u00a0')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static int Read(string[] args)
    {
        string dir = args[1];
        string? docxDir = Option(args, "--docx"), rtfDir = Option(args, "--rtf"), txtDir = Option(args, "--txt");
        foreach (string? d in new[] { docxDir, rtfDir, txtDir })
        {
            if (d is not null)
            {
                Directory.CreateDirectory(d);
            }
        }

        using var output = new StreamWriter(args[2], false, new UTF8Encoding(false));
        int files = 0, typed = 0, untyped = 0, roundTripEqual = 0, roundTripChecked = 0;
        var defectCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lossCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(dir, "*.rtf", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            files++;
            string name = Path.GetFileNameWithoutExtension(path);
            var sw = Stopwatch.StartNew();
            var line = new Dictionary<string, object?> { ["file"] = Path.GetRelativePath(dir, path), ["bytes"] = new FileInfo(path).Length };
            try
            {
                var defects = new DefectCollection();
                using Document doc = Document.Open(path, new OpenContext(defects: defects));
                string text = doc.ExtractText();
                line["paragraphs"] = doc.Paragraphs.Count();
                line["tables"] = doc.Tables.Count();
                line["textLength"] = text.Length;
                var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (Defect x in defects)
                {
                    codes[x.Code] = codes.GetValueOrDefault(x.Code) + 1;
                    defectCounts[x.Code] = defectCounts.GetValueOrDefault(x.Code) + 1;
                }

                line["defects"] = codes;
                var loss = new SortedDictionary<string, long>(StringComparer.Ordinal);
                foreach (LossItem l in doc.ImportLoss!.Items)
                {
                    loss[l.Code] = l.Count;
                    lossCounts[l.Code] = lossCounts.GetValueOrDefault(l.Code) + 1;
                }

                line["loss"] = loss;
                if (txtDir is not null)
                {
                    File.WriteAllText(Path.Combine(txtDir, name + ".ours.txt"), text, new UTF8Encoding(false));
                }

                if (docxDir is not null)
                {
                    doc.SaveAs(Path.Combine(docxDir, name + ".docx"), OfficeFormat.Docx);
                }

                if (rtfDir is not null)
                {
                    string back = Path.Combine(rtfDir, name + ".rtf");
                    using (var ms = new MemoryStream())
                    {
                        // Written from the model, not copied: the bytes of an untouched file are only the original's.
                        // Written from the model, not copied: through DOCX, so the bytes of the original are not what comes back.
                        using var asDocx = new MemoryStream();
                        doc.SaveAs(asDocx, OfficeFormat.Docx);
                        asDocx.Position = 0;
                        using Document copy = Document.Open(asDocx);
                        copy.SaveAs(ms, OfficeFormat.Rtf);
                        File.WriteAllBytes(back, ms.ToArray());
                        line["exportLoss"] = copy.ExportLoss!.Items.Select(i => i.Code).ToArray();
                    }

                    using Document again = Document.Open(back);
                    roundTripChecked++;
                    bool same = Norm(again.ExtractText()) == Norm(text);
                    line["rereadTextEqual"] = same;
                    if (same)
                    {
                        roundTripEqual++;
                    }
                }

            }
            catch (OfficeException ex)
            {
                typed++;
                line["error"] = ex.GetType().Name + ": " + ex.Message;
            }
            catch (Exception ex)
            {
                untyped++;
                line["untyped"] = ex.GetType().Name + ": " + ex.Message;
            }

            line["ms"] = sw.ElapsedMilliseconds;
            output.WriteLine(JsonSerializer.Serialize(line, Json));
        }

        output.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["kind"] = "summary",
            ["files"] = files,
            ["typedErrors"] = typed,
            ["untypedExceptions"] = untyped,
            ["rewriteTextEqual"] = roundTripEqual,
            ["rewriteChecked"] = roundTripChecked,
            ["defects"] = defectCounts,
            ["loss"] = lossCounts,
        }, Json));
        return untyped > 0 ? 1 : 0;
    }

    private static int Write(string dir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        int untyped = 0;
        foreach (string path in Directory.EnumerateFiles(dir, "*.docx", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var line = new Dictionary<string, object?> { ["file"] = Path.GetRelativePath(dir, path) };
            try
            {
                using Document doc = Document.Open(path);
                string text = doc.ExtractText();
                string target = Path.Combine(outDir, name + ".rtf");
                doc.SaveAs(target, OfficeFormat.Rtf);
                line["loss"] = doc.ExportLoss!.Items.Select(i => i.Code + "=" + i.Count).ToArray();
                line["bytes"] = new FileInfo(target).Length;
                using Document back = Document.Open(target);
                line["rereadTextEqual"] = Norm(back.ExtractText()) == Norm(text);
            }
            catch (OfficeException ex)
            {
                line["error"] = ex.Message;
            }
            catch (Exception ex)
            {
                untyped++;
                line["untyped"] = ex.GetType().Name + ": " + ex.Message;
            }

            Console.WriteLine(JsonSerializer.Serialize(line, Json));
        }

        return untyped > 0 ? 1 : 0;
    }

    private static int TextOf(string dir, string ext, string outDir)
    {
        Directory.CreateDirectory(outDir);
        int failed = 0;
        foreach (string path in Directory.EnumerateFiles(dir, "*." + ext, SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".txt"), Office.ExtractText(path), new UTF8Encoding(false));
            }
            catch (OfficeException ex)
            {
                failed++;
                Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileName(path), error = ex.Message }, Json));
            }
        }

        return 0;
    }

    private static Dictionary<string, int> Grams(string text)
    {
        string s = Norm(text).ToLowerInvariant();
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i + 5 <= s.Length; i++)
        {
            string g = s.Substring(i, 5);
            d[g] = d.GetValueOrDefault(g) + 1;
        }

        return d;
    }

    private static double Dice(string a, string b)
    {
        Dictionary<string, int> ga = Grams(a), gb = Grams(b);
        long ta = ga.Values.Sum(v => (long)v), tb = gb.Values.Sum(v => (long)v);
        if (ta == 0 && tb == 0)
        {
            return 1;
        }

        if (ta == 0 || tb == 0)
        {
            return 0;
        }

        long common = 0;
        foreach (KeyValuePair<string, int> kv in ga)
        {
            if (gb.TryGetValue(kv.Key, out int n))
            {
                common += Math.Min(kv.Value, n);
            }
        }

        return 2.0 * common / (ta + tb);
    }

    private static int Similar(string dirA, string dirB)
    {
        var scores = new List<double>();
        foreach (string a in Directory.EnumerateFiles(dirA, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            string b = Path.Combine(dirB, Path.GetFileName(a));
            if (!File.Exists(b))
            {
                continue;
            }

            double s = Dice(File.ReadAllText(a), File.ReadAllText(b));
            scores.Add(s);
            Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileNameWithoutExtension(a), dice = Math.Round(s, 4) }, Json));
        }

        scores.Sort();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "summary",
            files = scores.Count,
            mean = scores.Count == 0 ? 0 : Math.Round(scores.Average(), 4),
            min = scores.Count == 0 ? 0 : scores[0],
            atLeast95 = scores.Count(x => x >= 0.95),
        }, Json));
        return 0;
    }
}
