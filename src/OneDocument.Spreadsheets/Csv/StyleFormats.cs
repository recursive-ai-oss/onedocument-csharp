using System.Globalization;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Csv;

/// <summary>
/// Looks up the number format of a cell's style index, read once from the workbook's style sheet (ECMA-376 Part 1 §18.8.30 numFmt,
/// §18.8.10 cellXfs). It is the least that writing displayed values needs; fonts, fills and the rest of the style sheet are not read.
/// </summary>
internal sealed class StyleFormats
{
    private readonly int[] _xfFormat;
    private readonly Dictionary<int, string> _custom = [];
    private readonly NumberFormat?[] _cache;
    private readonly bool[] _resolved;

    internal StyleFormats(Workbook workbook)
    {
        _xfFormat = [];
        string? partName = null;
        foreach (Relationship r in workbook.Package.GetRelationships(workbook.MainPartName))
        {
            if (r.TargetMode == RelationshipTargetMode.Internal && Workbook.IsRelationship(r.Type, "styles")
                && PartUri.TryResolve(new PartUri(workbook.MainPartName), r.Target, out PartUri uri))
            {
                partName = uri.Value;
                break;
            }
        }

        if (partName is not null && workbook.Package.FindPart(partName) is { } part
            && MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? doc, workbook.OpenContext, partName) && doc is not null)
        {
            var xfs = new List<int>();
            foreach (MarkupNode section in doc.Root.ChildElements)
            {
                if (section.LocalNameString == "numFmts")
                {
                    foreach (MarkupNode f in section.ChildElements)
                    {
                        if (int.TryParse(f.GetAttribute("numFmtId"), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && f.GetAttribute("formatCode") is { } code)
                        {
                            _custom[id] = code;
                        }
                    }
                }
                else if (section.LocalNameString == "cellXfs")
                {
                    foreach (MarkupNode xf in section.ChildElements)
                    {
                        xfs.Add(int.TryParse(xf.GetAttribute("numFmtId"), NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : 0);
                    }
                }
            }

            _xfFormat = [.. xfs];
        }

        _cache = new NumberFormat?[_xfFormat.Length];
        _resolved = new bool[_xfFormat.Length];
    }

    /// <summary>Gets whether the workbook numbers dates from 1904 (ECMA-376 Part 1 §18.2.28 workbookPr date1904).</summary>
    internal static bool IsDate1904(Workbook workbook)
    {
        foreach (MarkupNode n in workbook.Markup.Root.ChildElements)
        {
            if (n.LocalNameString == "workbookPr")
            {
                return n.GetAttribute("date1904") is "1" or "true";
            }
        }

        return false;
    }

    /// <summary>Gets the number format of a style, or null for General (and for an unknown style).</summary>
    internal NumberFormat? Get(int style)
    {
        if ((uint)style >= (uint)_xfFormat.Length)
        {
            return null;
        }

        if (!_resolved[style])
        {
            int id = _xfFormat[style];
            string? code = _custom.TryGetValue(id, out string? c) ? c : NumberFormat.GetBuiltInCode(id);
            _cache[style] = code is null || id == 0 ? null : NumberFormat.TryParse(code, out NumberFormat? nf, out _) ? nf : null;
            _resolved[style] = true;
        }

        return _cache[style];
    }
}
