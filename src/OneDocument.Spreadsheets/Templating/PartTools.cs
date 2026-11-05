using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Templating;

// Package-level helpers for the parts that hang off a sheet (drawings, charts, comments, pictures). The workbook hub does not model these,
// so the template binder edits them as markup and keeps everything it does not understand.
internal static class PartTools
{
    public const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    public const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>The relative target of a part, as a relationship from <paramref name="source"/> writes it.</summary>
    public static string Relative(string source, string target)
    {
        string[] s = source.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] t = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        while (common < s.Length - 1 && common < t.Length - 1 && string.Equals(s[common], t[common], StringComparison.Ordinal))
        {
            common++;
        }

        var sb = new StringBuilder();
        for (int i = common; i < s.Length - 1; i++)
        {
            sb.Append("../");
        }

        sb.Append(string.Join('/', t.Skip(common)));
        return sb.ToString();
    }

    public static string? Resolve(Workbook wb, string source, Relationship r) =>
        r.TargetMode == RelationshipTargetMode.Internal && PartUri.TryResolve(new PartUri(source), r.Target, out PartUri uri) && wb.Package.FindPart(uri.Value) is { } p ? p.Name : null;

    public static List<(Relationship Rel, string Part)> Related(Workbook wb, string source, string typeEnd)
    {
        var list = new List<(Relationship, string)>();
        foreach (Relationship r in wb.Package.GetRelationships(source))
        {
            if (r.Type.EndsWith(typeEnd, StringComparison.Ordinal) && Resolve(wb, source, r) is { } p)
            {
                list.Add((r, p));
            }
        }

        return list;
    }

    public static string NextName(IOpcPackage package, string stem, string extension)
    {
        for (int n = 1; ; n++)
        {
            string name = stem + n.ToString(CultureInfo.InvariantCulture) + extension;
            if (package.FindPart(name) is null)
            {
                return name;
            }
        }
    }

    public static string NextRelationshipId(IReadOnlyList<Relationship> rels)
    {
        for (int n = 1; ; n++)
        {
            string id = "rId" + n.ToString(CultureInfo.InvariantCulture);
            if (!rels.Any(r => r.Id == id))
            {
                return id;
            }
        }
    }

    public static void Write(IPackagePart part, MarkupDocument doc)
    {
        byte[] bytes = doc.ToArray();
        using Stream s = part.OpenWrite();
        s.Write(bytes);
    }

    public static void Write(IPackagePart part, byte[] bytes)
    {
        using Stream s = part.OpenWrite();
        s.Write(bytes);
    }

    // Copies a part and everything it owns (a drawing and its charts). Pictures are shared, since a part may be the target of many relationships;
    // other parts get a copy so each sheet edits its own chart.
    public static string Duplicate(Workbook wb, string part, Dictionary<string, string> done)
    {
        if (done.TryGetValue(part, out string? existing))
        {
            return existing;
        }

        IPackagePart original = wb.Package.FindPart(part)!;
        int dot = part.LastIndexOf('.');
        int slash = part.LastIndexOf('/');
        int digits = dot;
        while (digits > slash + 1 && char.IsAsciiDigit(part[digits - 1]))
        {
            digits--;
        }

        string name = NextName(wb.Package, part[..digits], part[dot..]);
        wb.Package.AddPart(name, original.ContentType, original.GetBytes());
        done[part] = name;
        var rels = new List<Relationship>();
        foreach (Relationship r in wb.Package.GetRelationships(part))
        {
            if (Resolve(wb, part, r) is { } target && !r.Type.EndsWith("/image", StringComparison.Ordinal))
            {
                string copy = Duplicate(wb, target, done);
                rels.Add(r with { Target = Relative(name, copy) });
            }
            else if (Resolve(wb, part, r) is { } shared)
            {
                rels.Add(r with { Target = Relative(name, shared) });
            }
            else
            {
                rels.Add(r);
            }
        }

        if (rels.Count > 0)
        {
            wb.Package.SetRelationships(name, rels);
        }

        return name;
    }

    public static MarkupNode Child(MarkupNode parent, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c;
            }
        }

        return default;
    }

    public static IEnumerable<MarkupNode> Descendants(MarkupNode node)
    {
        foreach (MarkupNode c in node.ChildElements)
        {
            yield return c;
            foreach (MarkupNode d in Descendants(c))
            {
                yield return d;
            }
        }
    }

    public static string Escape(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}

/// <summary>The kind, size and media type of an image, read from its header; the pixels are never decoded.</summary>
internal readonly record struct ImageHeader(string Extension, string ContentType, int Width, int Height)
{
    public static byte[]? Bytes(object? raw)
    {
        switch (raw)
        {
            case byte[] b:
                return b;
            case string s:
                int comma = s.IndexOf(',', StringComparison.Ordinal);
                string b64 = s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0 ? s[(comma + 1)..] : s;
                Span<byte> buffer = new byte[(b64.Length * 3 / 4) + 3];
                return Convert.TryFromBase64String(b64.Trim(), buffer, out int written) ? buffer[..written].ToArray() : null;
            default:
                return null;
        }
    }

    public static bool TryRead(ReadOnlySpan<byte> b, out ImageHeader image)
    {
        image = default;
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            // PNG: the IHDR chunk follows the signature; width and height are big-endian.
            image = new ImageHeader(".png", "image/png", (int)ReadBe(b, 16), (int)ReadBe(b, 20));
            return true;
        }

        if (b.Length >= 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
        {
            image = new ImageHeader(".gif", "image/gif", b[6] | (b[7] << 8), b[8] | (b[9] << 8));
            return true;
        }

        if (b.Length >= 26 && b[0] == 'B' && b[1] == 'M')
        {
            image = new ImageHeader(".bmp", "image/bmp", BitConverter.ToInt32(b[18..22]), Math.Abs(BitConverter.ToInt32(b[22..26])));
            return true;
        }

        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            // JPEG: the first start-of-frame segment holds the size.
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                byte marker = b[i + 1];
                if (marker == 0xFF)
                {
                    i++;
                    continue;
                }

                if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                {
                    image = new ImageHeader(".jpeg", "image/jpeg", (b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                    return true;
                }

                i += 2 + ((b[i + 2] << 8) | b[i + 3]);
            }

            image = new ImageHeader(".jpeg", "image/jpeg", 0, 0);
            return true;
        }

        return false;
    }

    private static uint ReadBe(ReadOnlySpan<byte> b, int at) => ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];
}
