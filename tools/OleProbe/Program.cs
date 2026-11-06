using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Tools.OleProbe;

/// <summary>
/// Looks at the property streams and the embedded-object storages of legacy files.
///
///   OleProbe streams &lt;file&gt;           storages (with CLSIDs) and streams of the compound file
///   OleProbe props &lt;file&gt;             the property sets, decoded, and what maps into the package properties
///   OleProbe objects &lt;file&gt;           the object storages and what they describe
///   OleProbe scan &lt;dir&gt; &lt;out.tsv&gt; [--shard i/n]   one line per .doc/.xls/.ppt: property streams, properties, unmapped, objects, defects
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: OleProbe streams|props|objects <file> | scan <dir> <out.tsv> [--shard i/n]");
            return 2;
        }

        switch (args[0])
        {
            case "streams": return Streams(args[1]);
            case "props": return Props(args[1]);
            case "objects": return Objects(args[1]);
            case "scan" when args.Length >= 3: return Scan(args[1], args[2], args.Skip(3).ToArray());
            default:
                Console.Error.WriteLine("unknown command");
                return 2;
        }
    }

    private static CfbPackage Open(string path, DefectCollection defects) =>
        CfbPackage.Open(ByteSource.FromFile(path), new OpenContext(defects: defects));

    private static int Streams(string path)
    {
        var defects = new DefectCollection();
        using CfbPackage cfb = Open(path, defects);
        Console.WriteLine("root " + cfb.RootClassId);
        foreach (CfbStorage s in cfb.Storages)
        {
            Console.WriteLine($"storage {Show(s.Path)} {s.ClassId}");
        }

        foreach (CfbStreamPart p in cfb.Streams)
        {
            Console.WriteLine($"stream {Show(p.Name)} {p.Length}");
        }

        foreach (Defect d in defects)
        {
            Console.WriteLine(d);
        }

        return 0;
    }

    private static string Show(string name) => name.Replace("\u0001", "\\1", StringComparison.Ordinal).Replace("\u0003", "\\3", StringComparison.Ordinal).Replace("\u0005", "\\5", StringComparison.Ordinal);

    private static int Props(string path)
    {
        var defects = new DefectCollection();
        using CfbPackage cfb = Open(path, defects);
        byte[] summary = cfb.FindPart("/" + OlePropertySets.SummaryInformationStreamName)?.GetBytes().ToArray() ?? [];
        byte[] doc = cfb.FindPart("/" + OlePropertySets.DocumentSummaryInformationStreamName)?.GetBytes().ToArray() ?? [];
        foreach ((string name, byte[] bytes) in new[] { ("SummaryInformation", summary), ("DocumentSummaryInformation", doc) })
        {
            Console.WriteLine($"== {name}: {bytes.Length} bytes");
            if (bytes.Length == 0)
            {
                continue;
            }

            var d2 = new DefectCollection();
            if (OlePropertySets.TryRead(bytes, out OlePropertySets? sets, new OpenContext(defects: d2), name))
            {
                Console.WriteLine($"version {sets!.Version} system 0x{sets.SystemIdentifier:X8} sets {sets.Sets.Count}");
                foreach (OlePropertySet s in sets.Sets)
                {
                    Console.WriteLine($"set {s.FormatId} codepage {s.CodePage} locale {s.Locale} names {s.Names.Count}");
                    foreach (OleProperty p in s.Properties)
                    {
                        Console.WriteLine($"  {p.Id,3} {(s.Names.TryGetValue(p.Id, out string? n) ? n : string.Empty),-20} {Describe(p.Value)}");
                    }
                }
            }

            foreach (Defect d in d2)
            {
                Console.WriteLine(d);
            }
        }

        using var pkg = new InMemoryPackage();
        var props = new DocumentProperties(pkg);
        IReadOnlyList<string> lost = OleDocumentProperties.Read(summary, doc, props);
        Console.WriteLine($"== mapped: title='{props.Core.Title}' author='{props.Core.Creator}' created={props.Core.Created:o} custom={props.Custom.List().Count} unmapped={lost.Count}");
        foreach (string l in lost)
        {
            Console.WriteLine("  unmapped: " + l);
        }

        return 0;
    }

    private static string Describe(OlePropertyValue v)
    {
        string text = v.Value switch
        {
            byte[] b => b.Length + " bytes",
            IReadOnlyList<object?> l => "[" + l.Count + " items] " + string.Join(", ", l.Take(5).Select(x => x is OlePropertyValue pv ? Describe(pv) : Convert.ToString(x, CultureInfo.InvariantCulture))),
            _ => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "null",
        };
        return "0x" + v.Type.ToString("X4", CultureInfo.InvariantCulture) + " " + (text.Length > 100 ? text[..100] + "..." : text);
    }

    private static int Objects(string path)
    {
        var defects = new DefectCollection();
        using CfbPackage cfb = Open(path, defects);
        foreach (OleStorageImage s in All(cfb, defects))
        {
            OleObjectInfo info = s.Describe();
            Console.WriteLine($"{s.Name}: {info.Kind} class {s.ClassId} progid {info.ProgId} type '{info.UserType}' link '{info.LinkTarget}' streams {string.Join(",", s.Streams.Select(x => Show(x.Path) + ":" + x.Data.Length))}");
        }

        return 0;
    }

    private static IEnumerable<OleStorageImage> All(CfbPackage cfb, DefectCollection defects)
    {
        var ctx = new OpenContext(defects: defects);
        foreach (OleStorageImage s in OleStorageSet.Capture(cfb, "/ObjectPool", ctx))
        {
            yield return s;
        }

        foreach (OleStorageImage s in OleStorageSet.CaptureByPrefix(cfb, "MBD", ctx))
        {
            yield return s;
        }

        foreach (OleStorageImage s in OleStorageSet.CaptureByPrefix(cfb, "Ole", ctx))
        {
            yield return s;
        }
    }

    private static int Scan(string dir, string outPath, string[] options)
    {
        int shard = 0;
        int shards = 1;
        for (int i = 0; i + 1 < options.Length; i++)
        {
            if (options[i] == "--shard")
            {
                string[] parts = options[i + 1].Split('/');
                shard = int.Parse(parts[0], CultureInfo.InvariantCulture);
                shards = int.Parse(parts[1], CultureInfo.InvariantCulture);
            }
        }

        string[] files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ppt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        using var writer = new StreamWriter(outPath, append: false, new UTF8Encoding(false));
        writer.WriteLine("name\tstatus\tsummary\tdocsummary\tprops\tunmapped\tcustom\tobjects\tembedded\tlinked\tpackage\tdefect_codes");
        for (int i = shard; i < files.Length; i += shards)
        {
            string f = files[i];
            var defects = new DefectCollection();
            try
            {
                using CfbPackage cfb = Open(f, defects);
                byte[] summary = cfb.FindPart("/" + OlePropertySets.SummaryInformationStreamName)?.GetBytes().ToArray() ?? [];
                byte[] doc = cfb.FindPart("/" + OlePropertySets.DocumentSummaryInformationStreamName)?.GetBytes().ToArray() ?? [];
                int props = 0;
                int unmapped = 0;
                int custom = 0;
                if (summary.Length + doc.Length > 0)
                {
                    using var pkg = new InMemoryPackage();
                    var dp = new DocumentProperties(pkg);
                    var ctx = new OpenContext(defects: defects);
                    unmapped = OleDocumentProperties.Read(summary, doc, dp, ctx).Count;
                    custom = dp.Custom.List().Count;
                    foreach (byte[] b in new[] { summary, doc })
                    {
                        if (b.Length > 0 && OlePropertySets.TryRead(b, out OlePropertySets? sets, new OpenContext(defects: new DefectCollection())))
                        {
                            props += sets!.Sets.Sum(s => s.Properties.Count);
                        }
                    }
                }

                OleObjectInfo[] objects = All(cfb, defects).Select(s => s.Describe()).ToArray();
                writer.WriteLine(string.Join('\t', Path.GetFileName(f), "ok", summary.Length > 0 ? 1 : 0, doc.Length > 0 ? 1 : 0, props, unmapped, custom, objects.Length,
                    objects.Count(o => o.Kind == OleObjectKind.Embedded), objects.Count(o => o.Kind == OleObjectKind.Linked), objects.Count(o => o.Kind == OleObjectKind.Package),
                    string.Join(',', defects.Select(d => d.Code).Distinct())));
            }
            catch (OfficeException ex)
            {
                writer.WriteLine(string.Join('\t', Path.GetFileName(f), "typed:" + ex.Defect.Code, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty));
            }
        }

        return 0;
    }
}
