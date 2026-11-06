using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Tools.Xml2003Probe;

/// <summary>
/// Reads Office 2003 XML files and prints JSON lines.
/// <c>Xml2003Probe read DIR [--max N]</c>: opens every file of the directory (any extension) that the sniffer calls Word 2003 XML or Excel 2003 XML,
/// counts defects and losses by code and reports any exception that is not an OfficeException.
/// <c>Xml2003Probe convert DIR OUT</c>: the same files saved as DOCX or XLSX under OUT, for the schema validator.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("read" or "convert"))
        {
            Console.Error.WriteLine("usage: Xml2003Probe read DIR [--max N] | convert DIR OUT");
            return 2;
        }

        DocumentFormats.Register(FormatRegistry.Default);
        SpreadsheetFormats.Register(FormatRegistry.Default);
        string dir = args[1];
        string? output = args[0] == "convert" && args.Length > 2 ? args[2] : null;
        int max = int.MaxValue;
        for (int i = 2; i + 1 < args.Length; i++)
        {
            if (args[i] == "--max" && int.TryParse(args[i + 1], out int m))
            {
                max = m;
            }
        }

        if (output is not null)
        {
            Directory.CreateDirectory(output);
        }

        var defects = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var losses = new SortedDictionary<string, long>(StringComparer.Ordinal);
        int words = 0, books = 0, skipped = 0, typed = 0, untyped = 0, seen = 0;
        foreach (string path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (seen >= max)
            {
                break;
            }

            OfficeFormat format;
            try
            {
                format = Office.Detect(path);
            }
            catch (Exception ex) when (ex is IOException or OfficeException)
            {
                continue;
            }

            if (format is not (OfficeFormat.Word2003Xml or OfficeFormat.Excel2003Xml))
            {
                skipped++;
                continue;
            }

            seen++;
            var context = new OpenContext();
            try
            {
                if (format == OfficeFormat.Word2003Xml)
                {
                    using Document doc = Document.Open(path, context);
                    _ = doc.Paragraphs.Count();
                    Merge(losses, doc.ImportLoss);
                    words++;
                    Save(doc, output, path, ".docx", OfficeFormat.Docx);
                }
                else
                {
                    using Workbook wb = Workbook.Open(path, context);
                    foreach (Worksheet sheet in wb.Sheets)
                    {
                        _ = sheet.UsedRange();
                    }

                    Merge(losses, wb.ImportLoss);
                    books++;
                    Save(wb, output, path, ".xlsx", OfficeFormat.Xlsx);
                }
            }
            catch (OfficeException ex)
            {
                typed++;
                Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileName(path), status = "typed", code = ex.Defect.Code }));
            }
            catch (Exception ex)
            {
                untyped++;
                Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileName(path), status = "UNTYPED", error = ex.GetType().Name + ": " + ex.Message }));
            }

            foreach (Defect d in (DefectCollection)context.Defects)
            {
                defects[d.Code] = defects.GetValueOrDefault(d.Code) + 1;
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new { summary = true, words, books, skipped, typed, untyped, defects, losses }));
        return untyped == 0 ? 0 : 1;
    }

    private static void Merge(SortedDictionary<string, long> into, LossReport? report)
    {
        if (report is null)
        {
            return;
        }

        foreach (LossItem item in report.Items)
        {
            into[item.Code] = into.GetValueOrDefault(item.Code) + item.Count;
        }
    }

    private static void Save(OfficeFile file, string? output, string path, string extension, OfficeFormat format)
    {
        if (output is null)
        {
            return;
        }

        using FileStream stream = File.Create(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + extension));
        file.SaveAs(stream, format);
    }
}
