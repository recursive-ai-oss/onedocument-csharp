using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.SmartArt;

namespace RecursiveAI.OneDocument.Tools.SmartArtProbe;

/// <summary>
/// Compares what the layout engine makes with the drawing a file stores, for every diagram that has both. Shapes are matched by text;
/// for each matched pair the centre offset (as a fraction of the frame's larger side), the size ratios and the font size ratio are
/// recorded. The stored drawing is what the file's own application laid out, so this measures how close the engine comes without
/// running that application. Usage: SmartArtProbe compare &lt;outDir&gt; &lt;inputDir&gt;... [--shard i/n] [--limit N].
/// </summary>
internal static class Compare
{
    private static readonly string[] Extensions = [".docx", ".docm", ".pptx", ".pptm", ".xlsx", ".xlsm"];

    public static int Run(string[] args)
    {
        string output = args[1];
        var inputs = new List<string>();
        int shard = 0, shards = 1, limit = int.MaxValue;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--shard" && i + 1 < args.Length)
            {
                string[] p = args[++i].Split('/');
                shard = int.Parse(p[0]);
                shards = int.Parse(p[1]);
            }
            else if (args[i] == "--limit" && i + 1 < args.Length)
            {
                limit = int.Parse(args[++i]);
            }
            else
            {
                inputs.Add(args[i]);
            }
        }

        Directory.CreateDirectory(output);
        string[] files = [.. inputs.SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];
        using var log = new StreamWriter(Path.Combine(output, "compare-" + shard + ".jsonl"));
        int scanned = 0;
        for (int index = 0; index < files.Length && scanned < limit; index++)
        {
            if (index % shards != shard)
            {
                continue;
            }

            scanned++;
            if (!OpcPackage.TryOpen(files[index], out OpcPackage? package, null) || package is null)
            {
                continue;
            }

            using (package)
            {
                try
                {
                    foreach (DiagramLocation loc in DiagramHost.Find(package))
                    {
                        if (DiagramHost.ReadStored(package, loc.HostPart, loc.References) is not { } stored)
                        {
                            continue;
                        }

                        double w = Math.Clamp(loc.WidthPoints ?? 480, 10, 2000), h = Math.Clamp(loc.HeightPoints ?? 360, 10, 2000);
                        if (w < 20 && stored.Count > 0)
                        {
                            // A worksheet frame states its size by cells; the stored drawing's extent stands in.
                            w = Math.Max(20, stored.Max(s => s.X + s.Width));
                            h = Math.Max(20, stored.Max(s => s.Y + s.Height));
                        }

                        DiagramLayoutResult? made = DiagramHost.Generate(package, loc.HostPart, loc.References, w, h);
                        log.WriteLine(JsonSerializer.Serialize(Measure(Path.GetFileName(files[index]), LayoutId(package, loc), w, h, stored, made)));
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    log.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["file"] = Path.GetFileName(files[index]), ["failure"] = ex.GetType().Name + ": " + ex.Message }));
                }
            }
        }

        return 0;
    }

    private static string LayoutId(OpcPackage package, DiagramLocation loc)
    {
        if (loc.References.Layout is not { } rid)
        {
            return "(none)";
        }

        foreach (Relationship r in package.GetRelationships(loc.HostPart))
        {
            if (r.Id == rid && OpcPackage.TryResolveTarget(loc.HostPart, r, out PartUri t) && package.FindPart(t.Value) is { } part
                && RecursiveAI.OneDocument.Markup.MarkupDocument.TryParse(part.GetBytes(), out RecursiveAI.OneDocument.Markup.MarkupDocument? doc, null, t.Value) && doc is not null)
            {
                string id = doc.Root.GetAttribute("uniqueId") ?? "(no id)";
                return id[(id.LastIndexOf('/') + 1)..];
            }
        }

        return "(unreadable)";
    }

    private static string Norm(string s) => string.Join(' ', s.Split([' ', '\n', '\r', '\t', '\v'], StringSplitOptions.RemoveEmptyEntries));

    private static Dictionary<string, object?> Measure(string file, string layout, double w, double h, IReadOnlyList<DiagramShape> stored, DiagramLayoutResult? made)
    {
        var line = new Dictionary<string, object?> { ["file"] = file, ["layout"] = layout, ["w"] = Math.Round(w, 1), ["h"] = Math.Round(h, 1), ["stored"] = stored.Count, ["made"] = made?.Shapes.Count ?? -1 };
        if (made is null)
        {
            return line;
        }

        var used = new HashSet<int>();
        var centre = new List<double>();
        var wr = new List<double>();
        var hr = new List<double>();
        var fr = new List<double>();
        int withText = 0, matched = 0;
        double scale = Math.Max(w, h);
        foreach (DiagramShape s in stored)
        {
            string t = Norm(s.Text);
            if (t.Length == 0)
            {
                continue;
            }

            withText++;
            int idx = -1;
            for (int i = 0; i < made.Shapes.Count; i++)
            {
                if (!used.Contains(i) && Norm(made.Shapes[i].Text) == t)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                continue;
            }

            used.Add(idx);
            matched++;
            DiagramShape g = made.Shapes[idx];
            centre.Add(Math.Sqrt(Math.Pow((s.X + (s.Width / 2)) - (g.X + (g.Width / 2)), 2) + Math.Pow((s.Y + (s.Height / 2)) - (g.Y + (g.Height / 2)), 2)) / scale);
            if (s.Width > 0.5 && s.Height > 0.5)
            {
                wr.Add(g.Width / s.Width);
                hr.Add(g.Height / s.Height);
            }

            if (s.FontSize > 0 && g.FontSize > 0)
            {
                fr.Add(g.FontSize / s.FontSize);
            }
        }

        line["textShapes"] = withText;
        line["matched"] = matched;
        line["centreErr"] = centre.Count == 0 ? null : Math.Round(centre.Average(), 4);
        line["widthRatio"] = wr.Count == 0 ? null : Math.Round(wr.Average(), 3);
        line["heightRatio"] = hr.Count == 0 ? null : Math.Round(hr.Average(), 3);
        line["fontRatio"] = fr.Count == 0 ? null : Math.Round(fr.Average(), 3);
        return line;
    }
}
