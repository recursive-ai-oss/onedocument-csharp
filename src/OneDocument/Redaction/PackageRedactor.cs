using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Redaction;

// Removes matches from every part of an OPC package (and, recursively, from packages embedded in it).
internal static class PackageRedactor
{
    private const string ThumbnailType = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";

    public static void Run(OpcPackage package, RedactionContext ctx, string prefix, int depth)
    {
        if (depth > ctx.Limits.MaxRecursionDepth)
        {
            ctx.Report.Gap("redaction.depth", prefix, "Embedded packages nest deeper than the recursion limit; the innermost one was not searched.");
            return;
        }

        if (ctx.Plan.Options.DropThumbnails)
        {
            DropThumbnails(package, ctx, prefix);
        }

        foreach (IPackagePart part in package.Parts.ToArray())
        {
            Part(package, part, ctx, prefix, depth);
        }

        Relationships(package, ctx, prefix);
        foreach (IPackagePart part in package.Parts)
        {
            if (ctx.FindSpans(part.Name, prefix + part.Name, [], literalOnly: true))
            {
                ctx.Report.Gap("redaction.part-name", prefix + part.Name, "A part name contains a match; names are not rewritten.");
            }
        }
    }

    private static void DropThumbnails(OpcPackage package, RedactionContext ctx, string prefix)
    {
        IReadOnlyList<Relationship> rels = package.GetRelationships();
        if (!rels.Any(static r => r.Type == ThumbnailType))
        {
            return;
        }

        var keep = new List<Relationship>();
        foreach (Relationship r in rels)
        {
            if (r.Type == ThumbnailType)
            {
                if (r.TargetMode == RelationshipTargetMode.Internal && OpcPackage.TryResolveTarget(null, r, out PartUri target))
                {
                    package.RemovePart(target.ToString());
                }

                ctx.Report.Add("thumbnail", prefix + r.Target, 1);
            }
            else
            {
                keep.Add(r);
            }
        }

        package.SetRelationships(null, keep);
    }

    private static void Part(OpcPackage package, IPackagePart part, RedactionContext ctx, string prefix, int depth)
    {
        string location = prefix + part.Name;
        byte[] bytes = part.GetBytes().ToArray();
        if (bytes.Length == 0)
        {
            return;
        }

        if (BinaryText.IsZip(bytes))
        {
            try
            {
                using var nested = OpcPackage.Open(new MemoryStream(bytes, writable: false), new OpenContext(ctx.Limits), leaveOpen: false);
                Run(nested, ctx, location + "!", depth + 1);
                if (nested.IsModified)
                {
                    using var output = new MemoryStream();
                    nested.WriteTo(output);
                    part.SetBytes(output.ToArray());
                }

                return;
            }
            catch (OfficeException)
            {
                ctx.Report.Gap("redaction.embedded-package", location, "An embedded zip is not an OPC package; it was searched as raw bytes only.");
            }
        }

        if (LooksLikeXml(part, bytes) && MarkupDocument.TryParse(bytes, out MarkupDocument? doc, new OpenContext(ctx.Limits), part.Name) && doc is not null)
        {
            new XmlRedactor(ctx, location, Category(part.Name)).Run(doc);
            if (doc.IsModified)
            {
                part.SetBytes(doc.ToArray());
            }

            return;
        }

        if (IsXmlType(part.ContentType))
        {
            ctx.Report.Gap("redaction.part-not-xml", location, "The part is declared as XML but is not well-formed; it was searched as raw bytes only.");
        }

        Opaque(part, bytes, ctx, location);
    }

    private static void Opaque(IPackagePart part, byte[] bytes, RedactionContext ctx, string location)
    {
        bool image = IsImagePart(part.Name, part.ContentType);
        int n = BinaryText.Redact(bytes, ctx, image, location);
        if (n > 0)
        {
            part.SetBytes(bytes);
            ctx.Report.Add("binary-overwrite", location, n);
        }

        if (!image && !IsXmlType(part.ContentType))
        {
            ctx.Report.Gap("redaction.opaque-part", location, "Binary part (" + part.ContentType + "): searched as raw text only; compressed or encrypted content inside it cannot be searched.");
        }
    }

    // "openxmlformats" contains "xml", so the media type is checked by its suffix.
    internal static bool IsXmlType(string contentType) =>
        contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) || contentType.EndsWith("/xml", StringComparison.OrdinalIgnoreCase);

    internal static bool IsImagePart(string name, string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || BinaryText.IsImageName(name);

    private static bool LooksLikeXml(IPackagePart part, byte[] bytes)
    {
        if (IsXmlType(part.ContentType) || part.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || part.Name.EndsWith(".vml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int i = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return i < bytes.Length && bytes[i] == (byte)'<' && !IsImagePart(part.Name, part.ContentType);
    }

    private static void Relationships(OpcPackage package, RedactionContext ctx, string prefix)
    {
        var owners = new List<string?> { null };
        foreach (IPackagePart p in package.Parts)
        {
            owners.Add(p.Name);
        }

        foreach (string? owner in owners)
        {
            IReadOnlyList<Relationship> rels = package.GetRelationships(owner);
            List<Relationship>? updated = null;
            int count = 0;
            for (int i = 0; i < rels.Count; i++)
            {
                Relationship r = rels[i];
                string location = prefix + (owner ?? "/") + " rel " + r.Id;
                if (r.TargetMode == RelationshipTargetMode.External)
                {
                    string target = r.Target;
                    int n = ctx.Apply(target, true, location, out string replaced);
                    if (n == 0)
                    {
                        string unescaped = SafeUnescape(target);
                        if (unescaped != target && ctx.Apply(unescaped, true, location, out string r2) > 0)
                        {
                            replaced = Escape(r2);
                            n = 1;
                        }
                    }

                    if (n > 0)
                    {
                        updated ??= [.. rels];
                        updated[i] = r with { Target = replaced };
                        count += n;
                    }
                }
                else if (ctx.FindSpans(r.Target, location, [], literalOnly: true))
                {
                    ctx.Report.Gap("redaction.part-name", location, "A relationship target (a part name) contains a match; names are not rewritten.");
                }
            }

            if (updated is not null)
            {
                package.SetRelationships(owner, updated);
                ctx.Report.Add("hyperlink-target", prefix + (owner ?? "/"), count);
            }
        }
    }

    private static string SafeUnescape(string s)
    {
        try
        {
            return Uri.UnescapeDataString(s);
        }
        catch (UriFormatException)
        {
            return s;
        }
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            char c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~:/?#[]@!$&'()*+,;=%".Contains(c, StringComparison.Ordinal)))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    // The kind of place a part holds, for the report (docs/redaction.md lists them).
    public static string Category(string partName)
    {
        string n = partName.ToLowerInvariant();
        return n switch
        {
            _ when n.StartsWith("/docprops/", StringComparison.Ordinal) => "properties",
            _ when n.Contains("/glossary/", StringComparison.Ordinal) => "glossary",
            _ when n.Contains("/comments", StringComparison.Ordinal) || n.Contains("threadedcomments", StringComparison.Ordinal) => "comments",
            _ when n.Contains("/footnotes", StringComparison.Ordinal) || n.Contains("/endnotes", StringComparison.Ordinal) => "notes",
            _ when n.Contains("/header", StringComparison.Ordinal) || n.Contains("/footer", StringComparison.Ordinal) => "headers-footers",
            _ when n.Contains("/charts/", StringComparison.Ordinal) => "chart",
            _ when n.Contains("/diagrams/", StringComparison.Ordinal) => "smartart",
            _ when n.Contains("/notesslides/", StringComparison.Ordinal) || n.Contains("/notesmasters/", StringComparison.Ordinal) => "speaker-notes",
            _ when n.Contains("/slidelayouts/", StringComparison.Ordinal) || n.Contains("/slidemasters/", StringComparison.Ordinal) => "layouts-masters",
            _ when n.Contains("/slides/", StringComparison.Ordinal) => "slide",
            _ when n.EndsWith("/sharedstrings.xml", StringComparison.Ordinal) => "shared-strings",
            _ when n.Contains("/worksheets/", StringComparison.Ordinal) || n.Contains("/chartsheets/", StringComparison.Ordinal) => "sheet",
            _ when n.Contains("pivot", StringComparison.Ordinal) => "pivot",
            _ when n.Contains("externallink", StringComparison.Ordinal) => "external-link-cache",
            _ when n.Contains("/customxml/", StringComparison.Ordinal) => "custom-xml",
            _ when n.EndsWith("/workbook.xml", StringComparison.Ordinal) => "workbook",
            _ when n.EndsWith("/document.xml", StringComparison.Ordinal) => "body",
            _ when n.Contains("/drawings/", StringComparison.Ordinal) => "drawing",
            _ => "other",
        };
    }
}
