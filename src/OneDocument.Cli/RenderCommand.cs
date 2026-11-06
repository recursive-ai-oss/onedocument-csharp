using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering;
using RecursiveAI.OneDocument.Rendering.Presentations;
using RecursiveAI.OneDocument.Rendering.Spreadsheets;
using RecursiveAI.OneDocument.Rendering.Svg;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Cli;

/// <summary>
/// <c>onedocument render &lt;document&gt; -o &lt;out.pdf|out.png|out.jpg|out.tif|out.webp|out.svg&gt; [--pages 1-3,5|all] [--dpi n] [--width px] [--height px]
/// [--quality n] [--svg-glyphs auto|text|outlines] [--transparent] [--font-dir dir]… [--system-fonts] [--no-tags] [--profile ua|a2a]</c>
/// lays a document, workbook or presentation out and writes a PDF, or its pages as images. A TIFF holds every page asked for (all by default); the
/// other image formats write page 1 unless <c>--pages</c> asks for more, and then one file per page, <c>out-1.png</c>, <c>out-2.png</c>…
/// Font substitutions are listed on standard error.
/// <c>onedocument convert &lt;in&gt; &lt;out&gt;</c> converts between formats by the output extension (including PDF and the image formats; images hold the first page).
/// </summary>
internal static class RenderCommand
{
    public const string Usage =
        "       onedocument render <document> -o <out.pdf|out.png|out.jpg|out.tif|out.webp|out.svg> [--pages <1-3,5|all>] [--dpi <n>] [--width <px>] [--height <px>]\n" +
        "                          [--quality <1-100>] [--svg-glyphs auto|text|outlines] [--transparent] [--font-dir <dir>]... [--system-fonts] [--no-tags] [--profile ua|a2a]\n" +
        "       onedocument convert <in> <out>\n";

    /// <summary>Runs <c>render</c>.</summary>
    /// <param name="args">The arguments after <c>render</c>.</param>
    /// <param name="error">Where notes, errors and usage go.</param>
    /// <returns>0 on success, 1 when the document could not be processed, 2 on a usage error.</returns>
    public static int Run(string[] args, TextWriter error)
    {
        string? input = null;
        string? output = null;
        string? pages = null;
        double? dpi = null;
        int? width = null, height = null;
        int quality = 90;
        bool transparent = false;
        SvgGlyphMode glyphs = SvgGlyphMode.Auto;
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
                case "--page" or "--pages" when i + 1 < args.Length:
                    pages = args[++i];
                    break;
                case "--dpi" when i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0:
                    dpi = d;
                    i++;
                    break;
                case "--width" when i + 1 < args.Length && int.TryParse(args[i + 1], out int wpx) && wpx > 0:
                    width = wpx;
                    i++;
                    break;
                case "--height" when i + 1 < args.Length && int.TryParse(args[i + 1], out int hpx) && hpx > 0:
                    height = hpx;
                    i++;
                    break;
                case "--quality" when i + 1 < args.Length && int.TryParse(args[i + 1], out int q) && q is >= 1 and <= 100:
                    quality = q;
                    i++;
                    break;
                case "--svg-glyphs" when i + 1 < args.Length && args[i + 1] is "auto" or "text" or "outlines":
                    glyphs = args[++i] switch { "text" => SvgGlyphMode.Text, "outlines" => SvgGlyphMode.Outlines, _ => SvgGlyphMode.Auto };
                    break;
                case "--transparent":
                    transparent = true;
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
            if (PageExporter.TryGetFormat(output, out PageImageFormat imageFormat))
            {
                FontLibrary imageFonts = Fonts(fontDirs, systemFonts);
                if (opened is Document imageDoc)
                {
                    DocumentFonts.AddEmbeddedFonts(imageDoc, imageFonts);
                }

                var exportOptions = new PageExportOptions
                {
                    Layout = new LayoutOptions { Fonts = imageFonts },
                    Dpi = dpi,
                    WidthPixels = width,
                    HeightPixels = height,
                    Quality = quality,
                    TransparentBackground = transparent,
                    SvgGlyphMode = glyphs,
                    SvgTags = tags,
                };
                return ExportImages(opened, output, imageFormat, pages, exportOptions, error);
            }

            if (opened is not Document doc)
            {
                return RenderOther(opened, output, tags, profile, Fonts(fontDirs, systemFonts), error);
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
            else
            {
                error.WriteLine(OutputHint);
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

    // A presentation or a workbook: the same PDF output through each kind's renderer.
    private static int RenderOther(OfficeFile file, string output, bool tags, string? profile, FontLibrary fonts, TextWriter error)
    {
        string ext = Path.GetExtension(output).ToLowerInvariant();
        if (ext != ".pdf")
        {
            error.WriteLine(OutputHint);
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
                    using var buffer = new MemoryStream();
                    PresentationRenderReport report = PresentationRenderer.WritePdf(deck, buffer, options);
                    File.WriteAllBytes(output, buffer.ToArray());
                    notes.AddRange(report.FontNotes.Select(n => "warning font-substitution: " + n.Message));
                    notes.AddRange(report.Notes.Select(n => "warning " + n.Code + ": " + n.Message));
                    Console.WriteLine($"{report.Pages.Count} page(s) rendered; wrote {output}");

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
                    using var buffer = new MemoryStream();
                    WorkbookRenderer.WritePdf(layout, buffer, new PdfRenderOptions { Layout = layoutOptions, Tagged = tags, Profile = pdfProfile });
                    File.WriteAllBytes(output, buffer.ToArray());

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

    private const string OutputHint = "error: the output must end in .pdf, .png, .jpg, .tif, .webp or .svg.";

    // Pages as images: one file for page 1 (or the single page asked for), one numbered file per page for several, one TIFF for all.
    private static int ExportImages(OfficeFile file, string output, PageImageFormat format, string? pagesText, PageExportOptions options, TextWriter error)
    {
        PageSelection? selection = null;
        if (pagesText is not null && !PageSelection.TryParse(pagesText, out selection!))
        {
            error.WriteLine("error: --pages takes page numbers and ranges such as 1-3,5, or all.");
            return 2;
        }

        try
        {
            if (format == PageImageFormat.Tiff || selection is null || selection.IsSinglePage)
            {
                options.Pages = selection ?? (format == PageImageFormat.Tiff ? PageSelection.All : PageSelection.Page(1));
                using var buffer = new MemoryStream();
                PageExportReport one = PageExporter.Export(file, buffer, format, options);
                File.WriteAllBytes(output, buffer.ToArray());
                Notes(one, error);
                Console.WriteLine($"{one.Exported.Count} of {one.PageCount} page(s) exported; wrote {output}");
                return 0;
            }

            options.Pages = selection;
            string stem = Path.Combine(Path.GetDirectoryName(output) ?? string.Empty, Path.GetFileNameWithoutExtension(output));
            string ext = Path.GetExtension(output);
            var written = new List<string>();
            PageExportReport report = PageExporter.ExportPages(file, format, page =>
            {
                string name = stem + "-" + page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ext;
                File.WriteAllBytes(name, page.Data);
                written.Add(name);
            }, options);
            Notes(report, error);
            Console.WriteLine($"{report.Exported.Count} of {report.PageCount} page(s) exported; wrote {string.Join(", ", written)}");
            return 0;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error.WriteLine("error: " + ex.Message);
            return 1;
        }
        catch (ArgumentException ex)
        {
            error.WriteLine("error: " + ex.Message);
            return 2;
        }
    }

    private static void Notes(PageExportReport report, TextWriter error)
    {
        foreach (string note in report.Notes)
        {
            error.WriteLine("warning " + note);
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
