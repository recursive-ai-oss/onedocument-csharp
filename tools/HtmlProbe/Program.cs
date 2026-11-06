using System.Text;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using OneHtmlDocument = RecursiveAI.OneHtml.Document;

// HTML in bulk, information only.
//
//   HtmlProbe export <docx-directory> <out-directory> [--max N]   every DOCX: open, write HTML (kept as NNNN.html), parse it with OneHtml (parse errors),
//                                                                  compare its words with the extracted text, read it back, count export loss codes
//   HtmlProbe import <html-directory> [--max N]                   every HTML file: import, extract text, write DOCX, open it again, count import loss codes
//
// File names are printed only for failures, so the output carries no corpus content.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("export" or "import"))
        {
            Console.Error.WriteLine("usage: HtmlProbe export <docx-dir> <out-dir> [--max N] | HtmlProbe import <html-dir> [--max N]");
            return 2;
        }

        int max = int.MaxValue;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--max" && int.TryParse(args[i + 1], out int m))
            {
                max = m;
            }
        }

        return args[0] == "export" ? Export(args[1], args.Length > 2 && args[2] != "--max" ? args[2] : null, max) : Import(args[1], max);
    }

    private static IEnumerable<string> Files(string dir, params string[] patterns) =>
        patterns.SelectMany(p => Directory.EnumerateFiles(dir, p, SearchOption.AllDirectories)).OrderBy(static f => f, StringComparer.Ordinal);

    private static Dictionary<string, int> Bag(string text)
    {
        var bag = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string w in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            bag[w] = bag.GetValueOrDefault(w) + 1;
        }

        return bag;
    }

    private static double Similarity(string a, string b)
    {
        Dictionary<string, int> x = Bag(a);
        Dictionary<string, int> y = Bag(b);
        long common = 0;
        foreach ((string w, int n) in x)
        {
            common += Math.Min(n, y.GetValueOrDefault(w));
        }

        long total = Math.Max(x.Values.Sum(), y.Values.Sum());
        return total == 0 ? 1 : (double)common / total;
    }

    private static string BodyText(OneHtmlDocument doc)
    {
        var sb = new StringBuilder();
        void Walk(RecursiveAI.OneHtml.Node n)
        {
            for (RecursiveAI.OneHtml.Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == RecursiveAI.OneHtml.NodeKind.Text)
                {
                    sb.Append(c.Data);
                }
                else if (c.IsElement)
                {
                    sb.Append(' ');
                    Walk(c);
                    sb.Append(' ');
                }
            }
        }

        Walk(doc.Root.QuerySelector("body"));
        return sb.ToString();
    }

    private static int Export(string dir, string? outDir, int max)
    {
        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
        }

        int files = 0, opened = 0, exported = 0, parseClean = 0, close = 0, reread = 0, failed = 0;
        var loss = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var sims = new List<double>();
        foreach (string file in Files(dir, "*.docx", "*.docm", "*.dotx").Take(max))
        {
            files++;
            try
            {
                using Document d = Document.Open(file);
                opened++;
                string html = d.ToHtml();
                exported++;
                if (outDir is not null)
                {
                    File.WriteAllText(Path.Combine(outDir, files.ToString("D4") + ".html"), html);
                }

                foreach (LossItem item in d.ExportLoss!.Items)
                {
                    loss[item.Code] = loss.GetValueOrDefault(item.Code) + 1;
                }

                var parsed = OneHtmlDocument.Parse(html, new RecursiveAI.OneHtml.ParseOptions { RecordErrors = true });
                if (parsed.ParseErrors.Count == 0)
                {
                    parseClean++;
                }

                double sim = Similarity(d.ExtractText(), BodyText(parsed));
                sims.Add(sim);
                if (sim >= 0.95)
                {
                    close++;
                }

                using Document back = Document.FromHtml(html);
                if (Similarity(BodyText(parsed), back.ExtractText()) >= 0.99)
                {
                    reread++;
                }
            }
            catch (Exception ex) when (ex is OfficeException)
            {
                failed++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("{\"unexpected\":\"" + Path.GetFileName(file) + "\",\"type\":\"" + ex.GetType().Name + "\"}");
            }
        }

        sims.Sort();
        Console.WriteLine("{\"files\":" + files + ",\"opened\":" + opened + ",\"exported\":" + exported + ",\"parseErrorFree\":" + parseClean + ",\"textSimilar95\":" + close +
            ",\"rereadSimilar99\":" + reread + ",\"typedFailures\":" + failed + ",\"medianSimilarity\":" + (sims.Count == 0 ? 0 : sims[sims.Count / 2]).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "}");
        foreach ((string code, int n) in loss)
        {
            Console.WriteLine("{\"loss\":\"" + code + "\",\"documents\":" + n + "}");
        }

        return 0;
    }

    private static int Import(string dir, int max)
    {
        int files = 0, imported = 0, saved = 0, failed = 0;
        var loss = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (string file in Files(dir, "*.html", "*.htm").Take(max))
        {
            files++;
            try
            {
                using Document d = Document.FromHtml(File.ReadAllBytes(file));
                imported++;
                _ = d.ExtractText();
                using var ms = new MemoryStream();
                d.SaveAs(ms, OfficeFormat.Docx);
                ms.Position = 0;
                using Document again = Document.Open(ms);
                _ = again.ExtractText();
                saved++;
                foreach (LossItem item in d.ImportLoss!.Items)
                {
                    loss[item.Code] = loss.GetValueOrDefault(item.Code) + item.Count;
                }
            }
            catch (OfficeException)
            {
                failed++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("{\"unexpected\":\"" + Path.GetFileName(file) + "\",\"type\":\"" + ex.GetType().Name + "\"}");
            }
        }

        Console.WriteLine("{\"files\":" + files + ",\"imported\":" + imported + ",\"savedAndReopened\":" + saved + ",\"typedFailures\":" + failed + "}");
        foreach ((string code, long n) in loss)
        {
            Console.WriteLine("{\"loss\":\"" + code + "\",\"items\":" + n + "}");
        }

        return 0;
    }
}
