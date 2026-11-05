using System.Text;
using System.Text.Json;
using OnePdf;
using OnePdf.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering;
using RecursiveAI.OneDocument.Rendering.Presentations;
using RecursiveAI.OneDocument.Rendering.Tests;

// LayoutProbe: the generated layout test set and the layout differential.
//
//   LayoutProbe gen <dir>                 write the test documents (DOCX) and slide decks (PPTX, named slide-*.pptx) to <dir>
//   LayoutProbe render <dir> <out> [fonts...]  lay out and render every DOCX and PPTX in <dir> to <out>/NAME.pdf and NAME.png (page 1),
//                                         and write <out>/layout.jsonl; extra arguments are font directories
//   LayoutProbe sweep-pptx <dir> <out.jsonl> [keepDir]  render every PPTX under <dir> (recursively), one JSON line each with its page
//                                         counts and notes, and a summary line; PDFs of the first LAYOUTPROBE_KEEP files go to keepDir.
//                                         LAYOUTPROBE_NOTES=1 also renders notes pages
//   LayoutProbe lines <pdf>...            print the text lines of each page of PDFs as JSON lines (OnePdf text extraction)
//   LayoutProbe compare <ours> <other>    compare page counts and per-page text lines of NAME.pdf in two directories
//
// compare prints one JSON line per document and a summary line; exit status 1 when any page count differs.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "gen")
        {
            Directory.CreateDirectory(args[1]);
            foreach ((string name, byte[] docx) in LayoutCases.All())
            {
                File.WriteAllBytes(Path.Combine(args[1], name + ".docx"), docx);
            }

            foreach ((string name, byte[] docx) in DrawingSamples.All())
            {
                File.WriteAllBytes(Path.Combine(args[1], "drawing-" + name + ".docx"), docx);
            }

            foreach ((string name, byte[] pptx) in DrawingSamples.Presentations())
            {
                File.WriteAllBytes(Path.Combine(args[1], "drawing-" + name + ".pptx"), pptx);
            }

            foreach ((string name, byte[] pptx) in SlideCases.All())
            {
                File.WriteAllBytes(Path.Combine(args[1], "slide-" + name + ".pptx"), pptx);
            }

            Console.WriteLine($"{LayoutCases.All().Count + DrawingSamples.All().Count} documents and {DrawingSamples.Presentations().Count + SlideCases.All().Count} presentations in {args[1]}");
            return 0;
        }

        if (args.Length >= 3 && args[0] == "render")
        {
            return Render(args[1], args[2], args[3..]);
        }

        if (args.Length >= 3 && args[0] == "sweep-pptx")
        {
            return Sweep(args[1], args[2], args.Length > 3 ? args[3] : null);
        }

        if (args.Length >= 2 && args[0] == "lines")
        {
            foreach (string pdf in args[1..])
            {
                Console.WriteLine(JsonSerializer.Serialize(new { file = pdf, pages = Lines(pdf) }));
            }

            return 0;
        }

        if (args.Length == 3 && args[0] == "compare")
        {
            return Compare(args[1], args[2]);
        }

        Console.Error.WriteLine("usage: LayoutProbe gen <dir> | render <dir> <out> [fontdir...] | sweep-pptx <dir> <out.jsonl> [keepDir] | lines <pdf>... | compare <oursDir> <otherDir>");
        return 2;
    }

    private static int Render(string input, string output, string[] fontDirs)
    {
        Directory.CreateDirectory(output);
        OneRender.Fonts.Noto.NotoArabicFonts.Register();
        OneRender.Fonts.Noto.NotoHebrewFonts.Register();
        OneRender.Fonts.Noto.NotoThaiFonts.Register();
        OneRender.Fonts.Cjk.CjkFonts.Register();
        FontLibrary fonts = FontLibrary.CreateDefault();
        fonts.AddOptionalPackages();
        foreach (string dir in fontDirs)
        {
            fonts.AddDirectory(dir);
        }

        int failed = 0;
        using var log = new StreamWriter(Path.Combine(output, "layout.jsonl"), append: false, new UTF8Encoding(false));
        foreach (string file in Directory.EnumerateFiles(input, "*.docx").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                using Document doc = Document.Open(file);
                var options = new LayoutOptions { Fonts = fonts };
                LayoutDocument layout = LayoutEngine.Layout(doc, options);
                using (FileStream pdf = File.Create(Path.Combine(output, name + ".pdf")))
                {
                    DocumentRenderer.WritePdf(layout, doc, pdf, new PdfRenderOptions { Layout = options, Profile = ProfileFromEnvironment() });
                }

                File.WriteAllBytes(Path.Combine(output, name + ".png"), DocumentRenderer.RenderPng(layout.Pages[0]));
                log.WriteLine(JsonSerializer.Serialize(new
                {
                    name,
                    pages = layout.Pages.Count,
                    lines = layout.Pages.Select(p => p.Paragraphs.Sum(x => x.Lines.Count())).ToArray(),
                    fontNotes = layout.FontNotes.Select(n => n.Message).ToArray(),
                    notes = layout.Notes.Select(n => n.Code).ToArray(),
                }));
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        foreach (string file in Directory.EnumerateFiles(input, "*.pptx").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                using Presentation deck = Presentation.Open(file);
                var options = new PresentationRenderOptions { Fonts = fonts, Profile = ProfileFromEnvironment(), Output = PresentationOutput.Slides };
                PresentationRenderReport report;
                using (FileStream pdf = File.Create(Path.Combine(output, name + ".pdf")))
                {
                    report = PresentationRenderer.WritePdf(deck, pdf, options);
                }

                File.WriteAllBytes(Path.Combine(output, name + ".png"), PresentationRenderer.RenderPng(deck, 0, options));
                log.WriteLine(JsonSerializer.Serialize(new
                {
                    name,
                    pages = report.Pages.Count,
                    hiddenSkipped = report.HiddenSlidesSkipped,
                    fontNotes = report.FontNotes.Select(n => n.Message).ToArray(),
                    notes = report.Notes.Select(n => n.Code).ToArray(),
                }));
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"rendered to {output}; {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    // Renders every PPTX under a directory and counts what came out. A file the library rejects with its own typed exception is counted
    // as rejected; any other exception is an untyped failure, which is a defect.
    private static int Sweep(string input, string outputJsonl, string? keepDir)
    {
        OneRender.Fonts.Noto.NotoArabicFonts.Register();
        OneRender.Fonts.Noto.NotoHebrewFonts.Register();
        OneRender.Fonts.Noto.NotoThaiFonts.Register();
        OneRender.Fonts.Cjk.CjkFonts.Register();
        FontLibrary fonts = FontLibrary.CreateDefault();
        fonts.AddOptionalPackages();
        bool withNotes = Environment.GetEnvironmentVariable("LAYOUTPROBE_NOTES") == "1";
        int keep = int.TryParse(Environment.GetEnvironmentVariable("LAYOUTPROBE_KEEP"), out int k) ? k : 0;
        if (keepDir is not null)
        {
            Directory.CreateDirectory(keepDir);
        }

        int files = 0, rendered = 0, rejected = 0, untyped = 0, slidePages = 0, notesPages = 0;
        var noteCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        using var log = new StreamWriter(outputJsonl, append: false, new UTF8Encoding(false));
        foreach (string file in Directory.EnumerateFiles(input, "*.pptx", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            files++;
            string name = Path.GetFileName(file);
            try
            {
                using Presentation deck = Presentation.Open(file);
                var options = new PresentationRenderOptions
                {
                    Fonts = fonts,
                    Output = withNotes ? PresentationOutput.Slides | PresentationOutput.NotesPages : PresentationOutput.Slides,
                    Compress = true,
                };
                using var ms = new MemoryStream();
                PresentationRenderReport report = PresentationRenderer.WritePdf(deck, ms, options);
                rendered++;
                slidePages += report.SlidePages;
                notesPages += report.NotesPages;
                foreach (RecursiveAI.OneDocument.Rendering.Drawing.DrawingNote n in report.Notes)
                {
                    noteCounts[n.Code] = noteCounts.GetValueOrDefault(n.Code) + 1;
                }

                if (keepDir is not null && rendered <= keep)
                {
                    File.WriteAllBytes(Path.Combine(keepDir, Path.ChangeExtension(name, ".pdf")), ms.ToArray());
                }

                log.WriteLine(JsonSerializer.Serialize(new { file = name, slides = report.SlidePages, notesPages = report.NotesPages, hidden = report.HiddenSlidesSkipped, notes = report.Notes.Select(n => n.Code).ToArray() }));
            }
            catch (RecursiveAI.OneDocument.OfficeException ex)
            {
                rejected++;
                log.WriteLine(JsonSerializer.Serialize(new { file = name, rejected = ex.GetType().Name, ex.Message }));
            }
            catch (Exception ex)
            {
                untyped++;
                log.WriteLine(JsonSerializer.Serialize(new { file = name, untyped = ex.GetType().Name, ex.Message, ex.StackTrace }));
            }
        }

        string summary = JsonSerializer.Serialize(new { summary = true, files, rendered, rejected, untyped, slidePages, notesPages, notes = noteCounts });
        log.WriteLine(summary);
        Console.WriteLine(summary);
        return untyped == 0 ? 0 : 1;
    }

    // LAYOUTPROBE_PROFILE=ua1 | a2a | ua1a2a writes PDF/UA-1 and/or PDF/A-2a files so a validator can be run over them.
    private static OneRender.Pdf.PdfProfile ProfileFromEnvironment() => Environment.GetEnvironmentVariable("LAYOUTPROBE_PROFILE") switch
    {
        "ua1" => OneRender.Pdf.PdfProfile.PdfUA1,
        "a2a" => OneRender.Pdf.PdfProfile.PdfA2a,
        "ua1a2a" => OneRender.Pdf.PdfProfile.PdfA2aAndUA1,
        _ => OneRender.Pdf.PdfProfile.None,
    };

    private static List<List<string>> Lines(string pdfPath)
    {
        using PdfDocument pdf = PdfDocument.Open(pdfPath);
        var pages = new List<List<string>>();
        foreach (PdfPageText page in pdf.ExtractText())
        {
            pages.Add([.. page.Blocks.SelectMany(b => b.Lines).Select(l => Normalize(l.Text)).Where(t => t.Length > 0)]);
        }

        return pages;
    }

    private static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int Compare(string ours, string other)
    {
        int differentPages = 0;
        int docs = 0;
        double sum = 0;
        foreach (string file in Directory.EnumerateFiles(ours, "*.pdf").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            string theirs = Path.Combine(other, name);
            if (!File.Exists(theirs))
            {
                continue;
            }

            docs++;
            List<List<string>> a = Lines(file);
            List<List<string>> b = Lines(theirs);
            int total = b.Sum(p => p.Count);
            int agree = 0;
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                var pool = a[i].GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
                foreach (string line in b[i])
                {
                    if (pool.TryGetValue(line, out int n) && n > 0)
                    {
                        agree++;
                        pool[line] = n - 1;
                    }
                }
            }

            double ratio = total == 0 ? 1 : (double)agree / total;
            sum += ratio;
            bool same = a.Count == b.Count;
            if (!same)
            {
                differentPages++;
            }

            Console.WriteLine(JsonSerializer.Serialize(new { name, pagesOurs = a.Count, pagesOther = b.Count, linesOther = total, linesAgreeing = agree, ratio = Math.Round(ratio, 3) }));
        }

        Console.WriteLine(JsonSerializer.Serialize(new { summary = true, documents = docs, pageCountDiffers = differentPages, meanLineAgreement = docs == 0 ? 0 : Math.Round(sum / docs, 3) }));
        return differentPages == 0 ? 0 : 1;
    }
}
