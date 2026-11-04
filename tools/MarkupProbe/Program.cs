using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Tools.MarkupProbe;

/// <summary>
/// Reads every XML part of every Office Open XML file under a directory into the markup tree and checks the round-trip contract:
/// parse then write is byte-identical; a scripted edit (an attribute on the root, an attribute on the first empty element, a comment
/// prepended to the first element with children) changes only the edited element and keeps every compatibility and extension construct.
/// </summary>
internal static class Program
{
    private static readonly string[] s_extensions = [".docx", ".docm", ".dotx", ".dotm", ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlam", ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm"];
    private static readonly string[] s_constructs = ["AlternateContent", "Fallback", "Ignorable", "extLst", "ProcessContent", "MustUnderstand", "<![CDATA[", "<?"];

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: MarkupProbe <directory> [maxFiles]");
            return 2;
        }

        int maxFiles = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue;
        var stats = new Stats();
        var failures = new List<string>();
        foreach (string file in Directory.EnumerateFiles(args[0], "*", SearchOption.AllDirectories).Where(f => s_extensions.Contains(Path.GetExtension(f).ToLowerInvariant(), StringComparer.Ordinal)).Order(StringComparer.Ordinal).Take(maxFiles))
        {
            stats.Files++;
            try
            {
                using ZipArchive zip = ZipFile.OpenRead(file);
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string ext = Path.GetExtension(e.FullName).ToLowerInvariant();
                    if (ext is not (".xml" or ".rels" or ".vml"))
                    {
                        continue;
                    }

                    if (e.Length > 256L << 20)
                    {
                        stats.Skipped++;
                        continue;
                    }

                    byte[] data;
                    using (Stream s = e.Open())
                    using (var ms = new MemoryStream((int)e.Length))
                    {
                        s.CopyTo(ms);
                        data = ms.ToArray();
                    }

                    Check(data, Path.GetFileName(file) + "!" + e.FullName, stats, failures);
                }
            }
            catch (InvalidDataException ex)
            {
                stats.BadZips++;
                failures.Add($"{Path.GetFileName(file)}: not a readable zip ({ex.Message})");
            }
        }

        Console.WriteLine($"files: {stats.Files} (unreadable zips {stats.BadZips})");
        Console.WriteLine($"xml parts: {stats.Parts} ({stats.Bytes:N0} bytes); skipped as too large: {stats.Skipped}");
        Console.WriteLine($"parsed: {stats.Parsed}; refused with a typed error: {stats.Refused}");
        Console.WriteLine($"byte-identical after parse and write: {stats.Identical} of {stats.Parsed}");
        Console.WriteLine($"edit 1 (root attribute) changed only the root start tag: {stats.Edit1} of {stats.Parsed}");
        Console.WriteLine($"edit 2 (attribute on first empty element) changed only that element: {stats.Edit2} of {stats.Edit2Tried}");
        Console.WriteLine($"edit 3 (comment prepended to first element with children) changed only that element: {stats.Edit3} of {stats.Edit3Tried}");
        Console.WriteLine($"compatibility/extension constructs present before = after, in every edit: {stats.ConstructsKept} of {stats.Edit1 + stats.Edit2 + stats.Edit3}");
        foreach (KeyValuePair<string, int> kv in stats.Codes.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"refused {kv.Key}: {kv.Value}");
        }

        foreach (KeyValuePair<string, int> kv in stats.Seen.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"parts containing {kv.Key}: {kv.Value}");
        }

        foreach (string f in failures.Take(60))
        {
            Console.WriteLine("FAIL " + f);
        }

        Console.WriteLine($"failures: {failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Check(byte[] data, string name, Stats stats, List<string> failures)
    {
        stats.Parts++;
        stats.Bytes += data.Length;
        MarkupDocument doc;
        try
        {
            doc = MarkupDocument.Parse(data, partUri: "/" + name);
        }
        catch (OfficeException ex)
        {
            stats.Refused++;
            stats.Codes[ex.Defect.Code] = stats.Codes.GetValueOrDefault(ex.Defect.Code) + 1;
            failures.Add($"{name}: refused {ex.Defect.Code} at {ex.Location}: {ex.Message}");
            return;
        }

        stats.Parsed++;
        string text = Encoding.UTF8.GetString(data);
        foreach (string c in s_constructs)
        {
            if (text.Contains(c, StringComparison.Ordinal))
            {
                stats.Seen[c] = stats.Seen.GetValueOrDefault(c) + 1;
            }
        }

        if (doc.ToArray().AsSpan().SequenceEqual(data))
        {
            stats.Identical++;
        }
        else
        {
            failures.Add($"{name}: round trip differs");
            return;
        }

        // Edit 1: an attribute on the root.
        {
            MarkupDocument d = MarkupDocument.Parse(data);
            MarkupNode root = d.Root;
            int start = root.SourceStart;
            int end = root.SourceEnd;
            root.SetAttribute("xmlns:oneoffice-probe", "urn:probe");
            if (OnlyChangedWithin(data, d.ToArray(), start, end, "xmlns:oneoffice-probe".Length + 9 + 4, out string why))
            {
                stats.Edit1++;
                CheckConstructs(text, d, 0, stats, failures, name);
            }
            else
            {
                failures.Add($"{name}: edit 1 {why}");
            }
        }

        // Edit 2: an attribute on the first element with no children below the root.
        {
            MarkupDocument d = MarkupDocument.Parse(data);
            MarkupNode target = FirstElement(d.Root, withChildren: false);
            if (!target.IsNull)
            {
                stats.Edit2Tried++;
                int start = target.SourceStart;
                int end = target.SourceEnd;
                target.SetAttribute("oneoffice-probe", "1");
                if (OnlyChangedWithin(data, d.ToArray(), start, end, " oneoffice-probe=\"1\"".Length, out string why))
                {
                    stats.Edit2++;
                    CheckConstructs(text, d, 0, stats, failures, name);
                }
                else
                {
                    failures.Add($"{name}: edit 2 {why}");
                }
            }
        }

        // Edit 3: a comment prepended inside the first element that has children.
        {
            MarkupDocument d = MarkupDocument.Parse(data);
            MarkupNode target = FirstElement(d.Root, withChildren: true);
            if (!target.IsNull)
            {
                stats.Edit3Tried++;
                int start = target.SourceStart;
                int end = target.SourceEnd;
                target.PrependXml("<!--oneoffice-probe-->"u8);
                if (OnlyChangedWithin(data, d.ToArray(), start, end, "<!--oneoffice-probe-->".Length, out string why))
                {
                    stats.Edit3++;
                    CheckConstructs(text, d, 1, stats, failures, name);
                }
                else
                {
                    failures.Add($"{name}: edit 3 {why}");
                }
            }
        }
    }

    private static MarkupNode FirstElement(MarkupNode root, bool withChildren)
    {
        // Document order below the root, skipping the root's own start.
        var stack = new Stack<MarkupNode>();
        for (MarkupNode c = root.LastChild; !c.IsNull; c = c.PreviousSibling)
        {
            stack.Push(c);
        }

        while (stack.Count > 0)
        {
            MarkupNode n = stack.Pop();
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            if (withChildren ? !n.FirstChild.IsNull : n.FirstChild.IsNull)
            {
                return n;
            }

            for (MarkupNode c = n.LastChild; !c.IsNull; c = c.PreviousSibling)
            {
                stack.Push(c);
            }
        }

        return default;
    }

    /// <summary>The edited bytes equal the original outside [start, end) (shifted by the growth), and the growth is what the edit adds.</summary>
    private static bool OnlyChangedWithin(byte[] before, byte[] after, int start, int end, int addedAtLeast, out string why)
    {
        why = string.Empty;
        if (start < 0 || end < start)
        {
            why = "element has no source range";
            return false;
        }

        int growth = after.Length - before.Length;
        if (growth < addedAtLeast - 12)
        {
            why = $"output grew by {growth}, expected about {addedAtLeast}";
            return false;
        }

        int prefix = 0;
        int max = Math.Min(before.Length, after.Length);
        while (prefix < max && before[prefix] == after[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < max - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        int changedFrom = prefix;
        int changedToBefore = before.Length - suffix;
        if (changedFrom < start || changedToBefore > end)
        {
            why = $"changed region [{changedFrom},{changedToBefore}) is outside the element [{start},{end})";
            return false;
        }

        return true;
    }

    private static void CheckConstructs(string original, MarkupDocument edited, int extraComments, Stats stats, List<string> failures, string name)
    {
        string text = Encoding.UTF8.GetString(edited.ToArray());
        foreach (string c in s_constructs)
        {
            if (Count(original, c) != Count(text, c))
            {
                failures.Add($"{name}: construct {c} count changed");
                return;
            }
        }

        if (extraComments != 0 && Count(text, "<!--oneoffice-probe-->") != extraComments)
        {
            failures.Add($"{name}: probe comment missing");
            return;
        }

        stats.ConstructsKept++;
    }

    private static int Count(string s, string needle)
    {
        int n = 0;
        int i = 0;
        while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += needle.Length;
        }

        return n;
    }

    private sealed class Stats
    {
        public int Files;
        public int BadZips;
        public int Parts;
        public long Bytes;
        public int Skipped;
        public int Parsed;
        public int Refused;
        public int Identical;
        public int Edit1;
        public int Edit2;
        public int Edit2Tried;
        public int Edit3;
        public int Edit3Tried;
        public int ConstructsKept;
        public Dictionary<string, int> Codes { get; } = [];
        public Dictionary<string, int> Seen { get; } = [];
    }
}
