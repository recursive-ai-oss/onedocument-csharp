using System.Diagnostics;
using System.Text.Json;
using OneRender.Image;
using OneRender.Painting;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Rendering.SmartArt;

namespace RecursiveAI.OneDocument.Tools.SmartArtProbe;

/// <summary>
/// Finds every SmartArt frame in the Word, PowerPoint and Excel files under one or more directories and paints each twice: as the file
/// stores it (the cached drawing, regenerating when it is missing or stale) and with the layout forced. Any exception that is not an
/// OfficeException counts as a failure. Writes <c>frames-N.jsonl</c> (one line per frame) and <c>summary-N.json</c>.
/// Usage: SmartArtProbe sweep &lt;outDir&gt; &lt;inputDir&gt;... [--shard i/n] [--limit N] [--png N].
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static readonly string[] Extensions = [".docx", ".docm", ".dotx", ".dotm", ".pptx", ".pptm", ".potx", ".ppsx", ".xlsx", ".xlsm", ".xltx", ".xltm"];

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "dump")
        {
            return Dump.Run(args);
        }

        if (args.Length >= 3 && args[0] == "compare")
        {
            return Compare.Run(args);
        }

        if (args.Length < 3 || args[0] != "sweep")
        {
            Console.Error.WriteLine("usage: SmartArtProbe sweep|compare <outDir> <inputDir>... [--shard i/n] [--limit N] [--png N]");
            return 2;
        }

        string output = args[1];
        var inputs = new List<string>();
        int shard = 0, shards = 1, limit = int.MaxValue, png = 0;
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shard" when i + 1 < args.Length:
                    string[] p = args[++i].Split('/');
                    shard = int.Parse(p[0]);
                    shards = int.Parse(p[1]);
                    break;
                case "--limit" when i + 1 < args.Length:
                    limit = int.Parse(args[++i]);
                    break;
                case "--png" when i + 1 < args.Length:
                    png = int.Parse(args[++i]);
                    break;
                default:
                    inputs.Add(args[i]);
                    break;
            }
        }

        Directory.CreateDirectory(output);
        string[] files = [.. inputs.SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];
        using var log = new StreamWriter(Path.Combine(output, "frames-" + shard + ".jsonl"));
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var layouts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var failures = new List<string>();
        int scanned = 0, pngDone = 0;
        var clock = Stopwatch.StartNew();
        for (int index = 0; index < files.Length && scanned < limit; index++)
        {
            if (index % shards != shard)
            {
                continue;
            }

            scanned++;
            string file = files[index];
            Bump(counts, "files");
            OpcPackage? package = null;
            try
            {
                if (!OpcPackage.TryOpen(file, out package, null) || package is null)
                {
                    Bump(counts, "unopenable");
                    continue;
                }

                IReadOnlyList<DiagramLocation> frames = DiagramHost.Find(package);
                if (frames.Count == 0)
                {
                    continue;
                }

                Bump(counts, "files-with-diagrams");
                foreach (DiagramLocation loc in frames)
                {
                    Bump(counts, "frames");
                    double w = Math.Clamp(loc.WidthPoints ?? 480, 10, 2000), h = Math.Clamp(loc.HeightPoints ?? 360, 10, 2000);
                    string layoutId = LayoutId(package, loc);
                    Bump(layouts, layoutId);
                    var line = new Dictionary<string, object?> { ["file"] = Path.GetFileName(file), ["host"] = loc.HostPart, ["layout"] = layoutId, ["w"] = w, ["h"] = h };
                    foreach ((string mode, DiagramRegeneration regen) in new[] { ("default", DiagramRegeneration.WhenMissingOrStale), ("forced", DiagramRegeneration.Always) })
                    {
                        var reports = new List<DiagramReport>();
                        var list = new DisplayList();
                        uint id = 0;
                        var ctx = new DrawingContext();
                        try
                        {
                            bool painted = DiagramHost.Paint(package, loc.HostPart, new DiagramPaintRequest(loc.References, list, () => ItemId.Of(++id), ctx, 0, 0, w * 4 / 3, h * 4 / 3, null, false), new DiagramOptions { Regeneration = regen, Report = reports.Add });
                            list.Validate();
                            DiagramReport? r = reports.LastOrDefault();
                            string key = mode + ":" + (r is null ? "none" : r.Source + (r.Reason is null ? string.Empty : "/" + r.Reason));
                            Bump(counts, key);
                            line[mode] = r is null ? "none" : r.Source + (r.Reason is null ? string.Empty : "/" + r.Reason);
                            line[mode + "Shapes"] = r?.ShapeCount ?? 0;
                            line[mode + "Notes"] = ctx.Notes.Select(n => n.Code).ToArray();
                            if (painted && png > pngDone && mode == "forced")
                            {
                                WritePng(output, file, loc, list, w, h, "forced");
                            }

                            if (painted && png > pngDone && mode == "default")
                            {
                                WritePng(output, file, loc, list, w, h, "default");
                                pngDone++;
                            }
                        }
                        catch (OfficeException ex)
                        {
                            Bump(counts, mode + ":typed:" + ex.GetType().Name);
                            line[mode] = "typed:" + ex.GetType().Name;
                        }
                        catch (Exception ex)
                        {
                            Bump(counts, mode + ":FAILURE");
                            line[mode] = "FAILURE:" + ex.GetType().Name;
                            failures.Add(Path.GetFileName(file) + " " + loc.HostPart + " " + mode + " " + ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? string.Empty).Split('\n').FirstOrDefault()?.Trim());
                        }
                    }

                    log.WriteLine(JsonSerializer.Serialize(line));
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Bump(counts, "FAILURE:file");
                failures.Add(Path.GetFileName(file) + " open/find " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                package?.Dispose();
            }
        }

        var summary = new Dictionary<string, object> { ["counts"] = counts, ["layouts"] = layouts, ["failures"] = failures, ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1) };
        File.WriteAllText(Path.Combine(output, "summary-" + shard + ".json"), JsonSerializer.Serialize(summary, Indented));
        foreach (KeyValuePair<string, long> kv in counts)
        {
            Console.WriteLine($"{kv.Key} {kv.Value}");
        }

        foreach (string f in failures.Take(20))
        {
            Console.WriteLine("FAIL " + f);
        }

        return failures.Count == 0 ? 0 : 1;
    }

    private static void Bump(SortedDictionary<string, long> d, string key) => d[key] = d.GetValueOrDefault(key) + 1;

    private static string LayoutId(OpcPackage package, DiagramLocation loc)
    {
        if (loc.References.Layout is not { } rid)
        {
            return "(none)";
        }

        foreach (Relationship r in package.GetRelationships(loc.HostPart))
        {
            if (r.Id == rid && OpcPackage.TryResolveTarget(loc.HostPart, r, out PartUri t) && package.FindPart(t.Value) is { } part && MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? doc, null, t.Value) && doc is not null)
            {
                return doc.Root.GetAttribute("uniqueId") ?? "(no id)";
            }
        }

        return "(unreadable)";
    }

    private static void WritePng(string output, string file, DiagramLocation loc, DisplayList list, double w, double h, string mode)
    {
        string dir = Path.Combine(output, "png");
        Directory.CreateDirectory(dir);
        double scale = Math.Min(1.5, 1200 / Math.Max(w * 4 / 3, 1));
        int pw = (int)Math.Ceiling(w * 4 / 3 * scale), ph = (int)Math.Ceiling(h * 4 / 3 * scale);
        RgbaImage img = DisplayListRasterizer.Render(list, pw, ph, new RasterOptions { DevicePixelRatio = (float)scale });
        string name = Path.GetFileNameWithoutExtension(file) + "-" + loc.References.Data + "-" + mode + ".png";
        File.WriteAllBytes(Path.Combine(dir, name), img.ToPng());
    }
}
