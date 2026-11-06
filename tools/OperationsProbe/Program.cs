using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

// Runs the document operations (merge, split, sheet copy, slide insert, field update) over a directory of files and writes the results
// to another directory, so that the outputs can be checked with the schema validator and a second consumer. One JSON line per run:
// the operation, the inputs, counts, the number of resolutions and problems, and whether anything other than a typed error came out.
//
//   OperationsProbe samples <output dir>    (the small documents of testdata/operations/ORACLE-REQUEST.md)
//   OperationsProbe docx-merge|docx-split|docx-fields|xlsx-copy|pptx-insert|pptx-split <input dir> <output dir> [max files]
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "samples")
        {
            Samples.Write(args[1]);
            return 0;
        }

        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: OperationsProbe <command> <input dir> <output dir> [max files]");
            return 2;
        }

        string input = args[1];
        string output = args[2];
        int max = args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue;
        Directory.CreateDirectory(output);
        string pattern = args[0].StartsWith("docx", StringComparison.Ordinal) ? "*.docx" : args[0].StartsWith("xlsx", StringComparison.Ordinal) ? "*.xlsx" : "*.pptx";
        string[] files = [.. Directory.EnumerateFiles(input, pattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal).Take(max)];
        int bad = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string a = files[i];
            string b = files[(i + 1) % files.Length];
            var line = new Dictionary<string, object?> { ["op"] = args[0], ["file"] = Path.GetRelativePath(input, a), ["with"] = Path.GetRelativePath(input, b) };
            try
            {
                switch (args[0])
                {
                    case "docx-merge": DocxMerge(a, b, output, i, line); break;
                    case "docx-split": DocxSplit(a, output, i, line); break;
                    case "docx-fields": DocxFields(a, output, i, line); break;
                    case "xlsx-copy": XlsxCopy(a, output, i, line); break;
                    case "pptx-insert": PptxInsert(a, b, output, i, line); break;
                    case "pptx-split": PptxSplit(a, output, i, line); break;
                    default: Console.Error.WriteLine("unknown command"); return 2;
                }

                line["status"] = "ok";
            }
            catch (OfficeException ex)
            {
                line["status"] = "typed";
                line["code"] = ex.Defect.Code;
            }
            catch (Exception ex)
            {
                line["status"] = "crash";
                line["error"] = ex.GetType().Name + ": " + ex.Message;
                bad++;
            }

            Console.WriteLine(JsonSerializer.Serialize(line));
        }

        return bad == 0 ? 0 : 1;
    }

    private static void Summarise(MergeReport r, Dictionary<string, object?> line)
    {
        line["counts"] = r.Counts;
        line["resolutions"] = r.Resolutions.GroupBy(x => x.Code).ToDictionary(g => g.Key, g => g.Count());
        line["problems"] = r.Problems.GroupBy(x => x.Code).ToDictionary(g => g.Key, g => g.Count());
        line["loss"] = r.Loss.Items.Select(x => x.Code).ToArray();
    }

    private static string Name(int i, string what, string ext) => i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + "-" + what + ext;

    private static void DocxMerge(string a, string b, string output, int i, Dictionary<string, object?> line)
    {
        var variants = new (string Name, MergeOptions Options)[]
        {
            ("default", new MergeOptions()),
            ("kept", new MergeOptions { Styles = StyleConflict.KeepBoth, Numbering = NumberingContinuation.Restart, Sections = SectionHandling.KeepSource }),
        };
        foreach ((string name, MergeOptions options) in variants)
        {
            using Document dst = Document.Open(a);
            using Document src = Document.Open(b);
            MergeReport r = dst.Append(src, options);
            dst.SaveAs(Path.Combine(output, Name(i, "merge-" + name, ".docx")));
            Summarise(r, line);
        }
    }

    private static void DocxSplit(string a, string output, int i, Dictionary<string, object?> line)
    {
        using Document d = Document.Open(a);
        int written = 0;
        using (SplitResult byHeading = d.SplitByHeading(1))
        {
            foreach (DocumentPiece p in byHeading.Pieces.Take(6))
            {
                p.Document.SaveAs(Path.Combine(output, Name(i, "heading" + written++, ".docx")));
            }

            line["headingPieces"] = byHeading.Pieces.Count;
        }

        using SplitResult bySection = d.SplitBySections();
        int s = 0;
        foreach (DocumentPiece p in bySection.Pieces.Take(6))
        {
            p.Document.SaveAs(Path.Combine(output, Name(i, "section" + s++, ".docx")));
        }

        line["sectionPieces"] = bySection.Pieces.Count;
    }

    private static void DocxFields(string a, string output, int i, Dictionary<string, object?> line)
    {
        using Document d = Document.Open(a);
        FieldUpdateReport r = d.UpdateFields();
        d.SaveAs(Path.Combine(output, Name(i, "fields", ".docx")));
        line["tablesBuilt"] = r.TablesBuilt;
        line["updated"] = r.FieldsUpdated;
        line["unchanged"] = r.FieldsUnchanged;
        line["skipped"] = r.Skipped.Count;
        line["passes"] = r.LayoutPasses;
    }

    private static void XlsxCopy(string a, string output, int i, Dictionary<string, object?> line)
    {
        using Workbook src = Workbook.Open(a);
        using Workbook dst = Workbook.Create();
        Worksheet placeholder = dst.Sheets[0];
        Worksheet? first = src.Sheets.FirstOrDefault(s => s.Kind == SheetKind.Worksheet);
        if (first is null)
        {
            line["note"] = "no worksheet";
            return;
        }

        SheetCopyResult r = first.CopyTo(dst);
        dst.Sheets.Remove(placeholder);
        dst.SaveAs(Path.Combine(output, Name(i, "copy", ".xlsx")));
        Summarise(r.Report, line);
        using WorkbookSplitResult split = src.SplitBySheet();
        line["sheetPieces"] = split.Pieces.Count;
        int n = 0;
        foreach (WorkbookPiece p in split.Pieces.Take(3))
        {
            p.Workbook.SaveAs(Path.Combine(output, Name(i, "sheet" + n++, ".xlsx")));
        }
    }

    private static void PptxInsert(string a, string b, string output, int i, Dictionary<string, object?> line)
    {
        using Presentation dst = Presentation.Open(a);
        using Presentation src = Presentation.Open(b);
        SlideInsertResult r = dst.InsertSlides(src.Slides.Take(8), 0);
        dst.SaveAs(Path.Combine(output, Name(i, "insert", ".pptx")));
        Summarise(r.Report, line);
    }

    private static void PptxSplit(string a, string output, int i, Dictionary<string, object?> line)
    {
        using Presentation d = Presentation.Open(a);
        IReadOnlyList<Presentation> parts = d.SplitEvery(5);
        try
        {
            line["parts"] = parts.Count;
            int n = 0;
            foreach (Presentation p in parts.Take(4))
            {
                p.SaveAs(Path.Combine(output, Name(i, "part" + n++, ".pptx")));
            }
        }
        finally
        {
            foreach (Presentation p in parts)
            {
                p.Dispose();
            }
        }
    }
}
