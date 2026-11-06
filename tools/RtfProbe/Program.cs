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

        Console.Error.WriteLine("usage: RtfProbe read <dir> <out.jsonl> [--docx DIR] [--rtf DIR] [--txt DIR] | RtfProbe write <dir> <outdir>");
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
}
