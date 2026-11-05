using System.Text;
using System.Text.Json;
using OnePdf;
using OnePdf.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Rendering;
using RecursiveAI.OneDocument.Rendering.Tests;

// LayoutProbe: the generated layout test set and the layout differential.
//
//   LayoutProbe gen <dir>                 write the test documents (DOCX) to <dir>
//   LayoutProbe render <dir> <out> [fonts...]  lay out and render every DOCX in <dir> to <out>/NAME.pdf and NAME.png (page 1),
//                                         and write <out>/layout.jsonl; extra arguments are font directories
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

            foreach ((string name, byte[] docx) in SmartArtSamples.All())
            {
                File.WriteAllBytes(Path.Combine(args[1], name + ".docx"), docx);
            }

            foreach ((string name, byte[] pptx) in SmartArtSamples.Presentations())
            {
                File.WriteAllBytes(Path.Combine(args[1], name + ".pptx"), pptx);
            }

            Console.WriteLine($"{LayoutCases.All().Count + DrawingSamples.All().Count} documents and {DrawingSamples.Presentations().Count} presentations in {args[1]}");
            return 0;
        }

        if (args.Length >= 3 && args[0] == "render")
        {
            return Render(args[1], args[2], args[3..]);
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

        Console.Error.WriteLine("usage: LayoutProbe gen <dir> | render <dir> <out> [fontdir...] | lines <pdf>... | compare <oursDir> <otherDir>");
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

        Console.WriteLine($"rendered to {output}; {failed} failed");
        return failed == 0 ? 0 : 1;
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
