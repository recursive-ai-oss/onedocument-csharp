using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering;
using RecursiveAI.OneDocument.Rendering.Presentations;
using RecursiveAI.OneDocument.Rendering.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Cli;

/// <summary>
/// <c>onedocument render &lt;document&gt; -o &lt;out.pdf|out.png&gt; [--page n] [--font-dir dir]… [--system-fonts] [--no-tags] [--profile ua|a2a]</c>
/// lays a document, workbook or presentation out and writes a PDF, or one page as a PNG. Font substitutions are listed on standard error.
/// <c>onedocument convert &lt;in&gt; &lt;out&gt;</c> converts between formats by the output extension (including PDF and PNG).
/// </summary>
internal static class RenderCommand
{
    public const string Usage =
        "       onedocument render <document> -o <out.pdf|out.png> [--page <n>] [--font-dir <dir>]... [--system-fonts] [--no-tags] [--profile ua|a2a]\n" +
        "       onedocument convert <in> <out>\n";

    /// <summary>Runs <c>render</c>.</summary>
    /// <param name="args">The arguments after <c>render</c>.</param>
    /// <param name="error">Where notes, errors and usage go.</param>
    /// <returns>0 on success, 1 when the document could not be processed, 2 on a usage error.</returns>
    public static int Run(string[] args, TextWriter error)
    {
        string? input = null;
        string? output = null;
        int page = 1;
        bool tags = true;
        bool systemFonts = false;
        string? profile = null;
        var fontDirs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--page" when i + 1 < args.Length && int.TryParse(args[i + 1], out int p) && p > 0:
                    page = p;
                    i++;
                    break;
                case "--font-dir" when i + 1 < args.Length:
                    fontDirs.Add(args[++i]);
                    break;
                case "--profile" when i + 1 < args.Length:
                    profile = args[++i];
                    break;
                case "--system-fonts":
                    systemFonts = true;
                    break;
                case "--no-tags":
                    tags = false;
                    break;
                default:
                    if (args[i].StartsWith('-') || input is not null)
                    {
                        error.Write(Usage);
                        return 2;
                    }

                    input = args[i];
                    break;
            }
        }

        if (input is null || output is null)
        {
            error.Write(Usage);
            return 2;
        }

        var defects = new DefectCollection();
        try
        {
            var context = new OpenContext(defects: defects);
            using OfficeFile opened = Office.Open(input, context);
            if (opened is not Document doc)
            {
                return RenderOther(opened, output, page, tags, profile, Fonts(fontDirs, systemFonts), error);
            }

            FontLibrary fonts = Fonts(fontDirs, systemFonts);
            DocumentFonts.AddEmbeddedFonts(doc, fonts);
            var layoutOptions = new LayoutOptions { Fonts = fonts };
            string ext = Path.GetExtension(output).ToLowerInvariant();
            LayoutDocument layout;
            if (ext == ".pdf")
            {
                var options = new PdfRenderOptions
                {
                    Layout = layoutOptions,
                    Tagged = tags,
                    Profile = profile switch
                    {
                        "ua" => OneRender.Pdf.PdfProfile.PdfUA1,
                        "a2a" => OneRender.Pdf.PdfProfile.PdfA2a,
                        _ => OneRender.Pdf.PdfProfile.None,
                    },
                };
                layout = LayoutEngine.Layout(doc, layoutOptions);
                using var buffer = new MemoryStream();
                DocumentRenderer.WritePdf(layout, doc, buffer, options);
                File.WriteAllBytes(output, buffer.ToArray());
            }
            else if (ext == ".png")
            {
                layout = LayoutEngine.Layout(doc, layoutOptions);
                if (page > layout.Pages.Count)
                {
                    error.WriteLine($"error: the document has {layout.Pages.Count} page(s); there is no page {page}.");
                    return 1;
                }

                File.WriteAllBytes(output, DocumentRenderer.RenderPng(layout.Pages[page - 1]));
            }
            else
            {
                error.WriteLine("error: the output must end in .pdf or .png.");
                return 2;
            }

            foreach (FontSubstitutionNote note in layout.FontNotes)
            {
                error.WriteLine("warning font-substitution: " + note.Message);
            }

            foreach (LayoutNote note in layout.Notes)
            {
                error.WriteLine("warning " + note.Code + ": " + note.Message);
            }

            Console.WriteLine($"{layout.Pages.Count} page(s) laid out; wrote {output}");
            return 0;
        }
        catch (OfficeException ex)
        {
            error.WriteLine("error " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    // A presentation or a workbook: the same PDF and PNG output through each kind's renderer.
    private static int RenderOther(OfficeFile file, string output, int page, bool tags, string? profile, FontLibrary fonts, TextWriter error)
    {
        string ext = Path.GetExtension(output).ToLowerInvariant();
        if (ext is not (".pdf" or ".png"))
        {
            error.WriteLine("error: the output must end in .pdf or .png.");
            return 2;
        }

        OneRender.Pdf.PdfProfile pdfProfile = profile switch
        {
            "ua" => OneRender.Pdf.PdfProfile.PdfUA1,
            "a2a" => OneRender.Pdf.PdfProfile.PdfA2a,
            _ => OneRender.Pdf.PdfProfile.None,
        };
        switch (file)
        {
            case Presentation deck:
                {
                    var options = new PresentationRenderOptions { Fonts = fonts, Tagged = tags, Profile = pdfProfile };
                    var notes = new List<string>();
                    if (ext == ".pdf")
                    {
                        using var buffer = new MemoryStream();
                        PresentationRenderReport report = PresentationRenderer.WritePdf(deck, buffer, options);
                        File.WriteAllBytes(output, buffer.ToArray());
                        notes.AddRange(report.FontNotes.Select(n => "warning font-substitution: " + n.Message));
                        notes.AddRange(report.Notes.Select(n => "warning " + n.Code + ": " + n.Message));
                        Console.WriteLine($"{report.Pages.Count} page(s) rendered; wrote {output}");
                    }
                    else
                    {
                        File.WriteAllBytes(output, PresentationRenderer.RenderPng(deck, page - 1, options));
                        Console.WriteLine($"page {page} rendered; wrote {output}");
                    }

                    foreach (string n in notes)
                    {
                        error.WriteLine(n);
                    }

                    return 0;
                }

            case Workbook book:
                {
                    var layoutOptions = new LayoutOptions { Fonts = fonts };
                    var sheetOptions = new SheetRenderOptions { Fonts = fonts, Tagged = tags };
                    SheetLayoutDocument layout = WorkbookRenderer.Layout(book, sheetOptions);
                    if (ext == ".pdf")
                    {
                        using var buffer = new MemoryStream();
                        WorkbookRenderer.WritePdf(layout, buffer, new PdfRenderOptions { Layout = layoutOptions, Tagged = tags, Profile = pdfProfile });
                        File.WriteAllBytes(output, buffer.ToArray());
                    }
                    else
                    {
                        if (page > layout.Pages.Count)
                        {
                            error.WriteLine($"error: the workbook prints {layout.Pages.Count} page(s); there is no page {page}.");
                            return 1;
                        }

                        File.WriteAllBytes(output, WorkbookRenderer.RenderPng(layout, layout.Pages.Count > 0 ? layout.Pages[page - 1] : null));
                    }

                    foreach (FontSubstitutionNote n in layout.FontNotes)
                    {
                        error.WriteLine("warning font-substitution: " + n.Message);
                    }

                    foreach (SheetRenderNote n in layout.Notes)
                    {
                        error.WriteLine("warning " + n.Code + ": " + n.Message);
                    }

                    Console.WriteLine($"{layout.Pages.Count} page(s) laid out; wrote {output}");
                    return 0;
                }

            default:
                error.WriteLine("error: rendering is available for documents, workbooks and presentations; this file is a " + file.Kind + ".");
                return 1;
        }
    }

    /// <summary>Runs <c>convert</c>.</summary>
    /// <param name="args">The arguments after <c>convert</c>.</param>
    /// <param name="error">Where errors and usage go.</param>
    /// <returns>0 on success, 1 when the conversion failed, 2 on a usage error.</returns>
    public static int Convert(string[] args, TextWriter error)
    {
        if (args.Length != 2 || args[0].StartsWith('-'))
        {
            error.Write(Usage);
            return 2;
        }

        try
        {
            Office.Convert(args[0], args[1]);
            Console.WriteLine("wrote " + args[1]);
            return 0;
        }
        catch (OfficeException ex)
        {
            error.WriteLine("error " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static FontLibrary Fonts(List<string> dirs, bool system)
    {
        OneRender.Fonts.Noto.NotoArabicFonts.Register();
        OneRender.Fonts.Noto.NotoHebrewFonts.Register();
        OneRender.Fonts.Noto.NotoThaiFonts.Register();
        OneRender.Fonts.Cjk.CjkFonts.Register();
        FontLibrary library = FontLibrary.CreateDefault();
        library.AddOptionalPackages();
        foreach (string dir in dirs)
        {
            library.AddDirectory(dir);
        }

        if (system)
        {
            library.AddSystemFonts();
        }

        return library;
    }
}
