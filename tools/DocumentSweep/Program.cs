using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneOffice.Documents;

namespace RecursiveAI.OneOffice.Tools.DocumentSweep;

/// <summary>
/// Opens every word-processing file under a directory with <c>Document.Open</c> and saves it to a new file, untouched or after a
/// one-paragraph edit, writing one JSON line per input: <c>{file, status, saved, edited, exception, code, defects, ms}</c>.
/// Usage: DocumentSweep &lt;inputDir&gt; &lt;outDir&gt; [--edit] [--text dir] [--shard i/n] [--limit N]. Exit 0 always unless usage is wrong (2): findings are in the lines.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--make-samples")
        {
            return MakeSamples(args[1]);
        }

        string? input = null;
        string? output = null;
        bool edit = false;
        bool editStories = false;
        string? textDir = null;
        int shard = 0;
        int shards = 1;
        int limit = int.MaxValue;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--edit") { edit = true; }
            else if (args[i] == "--edit-stories") { editStories = true; }
            else if (args[i] == "--text" && i + 1 < args.Length) { textDir = args[++i]; }
            else if (args[i] == "--shard" && i + 1 < args.Length)
            {
                string[] p = args[++i].Split('/');
                shard = int.Parse(p[0]);
                shards = int.Parse(p[1]);
            }
            else if (args[i] == "--limit" && i + 1 < args.Length) { limit = int.Parse(args[++i]); }
            else if (input is null) { input = args[i]; }
            else if (output is null) { output = args[i]; }
            else { input = null; break; }
        }

        if (input is null || output is null)
        {
            Console.Error.WriteLine("usage: DocumentSweep <inputDir> <outDir> [--edit] [--shard i/n] [--limit N]");
            return 2;
        }

        Directory.CreateDirectory(output);
        RecursiveAI.OneOffice.Formats.FormatRegistry registry = RecursiveAI.OneOffice.Formats.FormatRegistry.Default;
        DocumentFormats.Register(registry);
        if (textDir is not null)
        {
            Directory.CreateDirectory(textDir);
        }
        string[] files = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".docm", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".dotx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dotm", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).Where((_, i) => i % shards == shard).Take(limit).ToArray();
        foreach (string file in files)
        {
            string rel = Path.GetRelativePath(input, file);
            string target = Path.Combine(output, rel.Replace(Path.DirectorySeparatorChar, '_'));
            var line = new Dictionary<string, object?> { ["file"] = rel, ["edited"] = false };
            var sw = Stopwatch.StartNew();
            try
            {
                using Document doc = Document.Open(file);
                if (edit)
                {
                    Paragraph? p = doc.Paragraphs.FirstOrDefault(x => x.Text.Length > 0);
                    if (p is not null)
                    {
                        p.SetText(p.Text + " (edited)");
                        line["edited"] = true;
                    }
                }

                if (editStories)
                {
                    // A tracked insertion in the first text box (when there is one) and a title and a custom property.
                    IReadOnlyList<TextBox> boxes = doc.TextBoxes();
                    if (boxes.Count > 0 && boxes[0].Paragraphs().FirstOrDefault() is { } boxParagraph)
                    {
                        doc.Revisions.CreateAuthor("Sweep", new DateTimeOffset(2024, 3, 1, 10, 0, 0, TimeSpan.Zero)).InsertText(boxParagraph.Markup.Node, 0, "X");
                        line["boxEdited"] = true;
                    }

                    doc.Properties.Core.Title = "Edited title";
                    doc.Properties.Custom.SetString("SweepKey", "SweepValue");
                    line["edited"] = true;
                }

                if (textDir is not null)
                {
                    // Office.ExtractText on the file, for comparison with the command-line tool's output.
                    string fromFile = Office.ExtractText(file);
                    File.WriteAllText(Path.Combine(textDir, Path.GetFileName(target) + ".txt"), fromFile, new System.Text.UTF8Encoding(false));
                    line["modelTextSame"] = string.Equals(doc.ExtractText(), fromFile, StringComparison.Ordinal);
                }

                doc.SaveAs(target, doc.Format);
                line["status"] = "saved";
                line["format"] = doc.Format.ToString();
                line["defects"] = doc.Defects.Count;
            }
            catch (OfficeException ex)
            {
                line["status"] = "failed";
                line["exception"] = ex.GetType().Name;
                line["code"] = ex.Defect.Code;
            }
            catch (IOException ex)
            {
                line["status"] = "failed";
                line["exception"] = ex.GetType().Name;
                line["code"] = "io";
            }
            catch (Exception ex)
            {
                // Anything else is a bug in the library: an untyped exception leaving the public API.
                line["status"] = "untyped";
                line["exception"] = ex.GetType().Name;
                line["code"] = ex.Message.Length > 120 ? ex.Message[..120] : ex.Message;
            }

            line["ms"] = sw.ElapsedMilliseconds;
            Console.WriteLine(JsonSerializer.Serialize(line));
        }

        return 0;
    }

    // Four files we generate ourselves, for opening in the desktop applications to see whether they ask to repair them.
    private static int MakeSamples(string dir)
    {
        Directory.CreateDirectory(dir);
        using Document created = Document.Create();
        created.SaveAs(Path.Combine(dir, "created.docx"));
        created.Body.AddParagraph("A paragraph added by AddParagraph, with a tab\there & some <markup>.", "Normal");
        created.Paragraphs.First().SetText("The first paragraph, edited.");
        created.SaveAs(Path.Combine(dir, "edited.docx"));
        created.SaveAs(Path.Combine(dir, "edited.dotx"));
        created.SaveAs(Path.Combine(dir, "edited.xml"));
        return 0;
    }
}
