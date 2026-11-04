using System.Globalization;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.TextExtraction;

/// <summary>
/// The strings of a workbook's shared string table (ECMA-376 Part 1 §18.4), held as one array of characters and an end offset per
/// string, so a worksheet's cells can be resolved in the order they come without a string object per entry.
/// </summary>
internal sealed class SharedStrings
{
    private readonly char[] _chars;
    private readonly int[] _ends;

    private SharedStrings(char[] chars, int[] ends)
    {
        _chars = chars;
        _ends = ends;
    }

    public static SharedStrings Empty { get; } = new([], []);

    public int Count => _ends.Length;

    public ReadOnlySpan<char> Get(int index)
    {
        int start = index == 0 ? 0 : _ends[index - 1];
        return _chars.AsSpan(start, _ends[index] - start);
    }

    /// <summary>Reads <c>sst</c>: for each <c>si</c> the text of <c>t</c> and <c>r/t</c>, leaving out phonetic runs (<c>rPh</c>, §18.4.6).</summary>
    public static SharedStrings Read(MarkupReader r, OpenContext context, CancellationToken cancellation)
    {
        var text = new CharBuffer(context.Limits.MaxAllocationBytes);
        var ends = new List<int>();
        bool collect = false;
        while (r.Read())
        {
            switch (r.Kind)
            {
                case XmlTokenKind.StartElement:
                case XmlTokenKind.EmptyElement:
                    if (Ns.Classify(r.NamespaceUri) != NsKind.Sml)
                    {
                        break;
                    }

                    if (r.LocalName.SequenceEqual("t"u8))
                    {
                        collect = r.Kind == XmlTokenKind.StartElement;
                    }
                    else if (r.LocalName.SequenceEqual("rPh"u8) || r.LocalName.SequenceEqual("extLst"u8))
                    {
                        r.Skip();
                    }
                    else if (r.LocalName.SequenceEqual("si"u8) && r.Kind == XmlTokenKind.EmptyElement)
                    {
                        ends.Add(text.Length);
                    }

                    break;
                case XmlTokenKind.EndElement:
                    if (Ns.Classify(r.NamespaceUri) != NsKind.Sml)
                    {
                        break;
                    }

                    if (r.LocalName.SequenceEqual("t"u8))
                    {
                        collect = false;
                    }
                    else if (r.LocalName.SequenceEqual("si"u8))
                    {
                        ends.Add(text.Length);
                        if ((ends.Count & 0xFFF) == 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                        }
                    }

                    break;
                case XmlTokenKind.Text:
                case XmlTokenKind.Whitespace:
                    if (collect)
                    {
                        text.AppendXml(r.Content);
                    }

                    break;
                case XmlTokenKind.CData:
                    if (collect)
                    {
                        text.AppendXml(r.Content, literal: true);
                    }

                    break;
            }
        }

        return new SharedStrings(text.AsSpan().ToArray(), [.. ends]);
    }
}

/// <summary>
/// Streams the string cells of one worksheet (ECMA-376 Part 1 §18.3) to a <see cref="TextSink"/> in row and cell order: shared strings
/// (<c>t="s"</c>), formula string results (<c>t="str"</c>) and inline strings (<c>t="inlineStr"</c>). Numbers, booleans and errors have
/// no text of their own and are left out; the full cell semantics belong to the workbook model.
/// </summary>
internal sealed class SheetTextExtractor
{
    private enum CellType : byte
    {
        Other,
        Shared,
        FormulaString,
        Inline,
    }

    private readonly OpenContext _context;
    private readonly SharedStrings _strings;
    private readonly TextSink _sink;
    private readonly string _partName;
    private readonly CancellationToken _cancellation;
    private readonly CharBuffer _cell;

    public SheetTextExtractor(OpenContext context, SharedStrings strings, TextSink sink, string partName, CancellationToken cancellation)
    {
        _context = context;
        _strings = strings;
        _sink = sink;
        _partName = partName;
        _cancellation = cancellation;
        _cell = new CharBuffer(context.Limits.MaxAllocationBytes);
    }

    /// <summary>Reads the whole worksheet.</summary>
    public void Run(MarkupReader r)
    {
        CellType type = CellType.Other;
        long cellOffset = 0;
        bool collect = false;
        bool inInline = false;
        int sharedIndex = -1;
        while (r.Read())
        {
            switch (r.Kind)
            {
                case XmlTokenKind.StartElement:
                case XmlTokenKind.EmptyElement:
                    if (Ns.Classify(r.NamespaceUri) != NsKind.Sml)
                    {
                        break;
                    }

                    ReadOnlySpan<byte> name = r.LocalName;
                    if (name.SequenceEqual("c"u8))
                    {
                        type = r.Kind == XmlTokenKind.EmptyElement ? CellType.Other : CellTypeOf(r);
                        cellOffset = r.TokenOffset;
                        sharedIndex = -1;
                        inInline = false;
                        _cell.Clear();
                    }
                    else if (name.SequenceEqual("v"u8))
                    {
                        collect = r.Kind == XmlTokenKind.StartElement && type is CellType.Shared or CellType.FormulaString;
                    }
                    else if (name.SequenceEqual("is"u8))
                    {
                        inInline = r.Kind == XmlTokenKind.StartElement;
                    }
                    else if (name.SequenceEqual("t"u8))
                    {
                        collect = r.Kind == XmlTokenKind.StartElement && inInline;
                    }
                    else if (name.SequenceEqual("rPh"u8) || name.SequenceEqual("extLst"u8))
                    {
                        r.Skip();
                    }

                    break;
                case XmlTokenKind.EndElement:
                    if (Ns.Classify(r.NamespaceUri) != NsKind.Sml)
                    {
                        break;
                    }

                    name = r.LocalName;
                    if (name.SequenceEqual("c"u8))
                    {
                        EmitCell(type, sharedIndex, cellOffset);
                        type = CellType.Other;
                    }
                    else if (name.SequenceEqual("v"u8))
                    {
                        collect = false;
                        if (type == CellType.Shared)
                        {
                            sharedIndex = int.TryParse(_cell.AsSpan().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? index : int.MinValue;
                            _cell.Clear();
                        }
                    }
                    else if (name.SequenceEqual("t"u8))
                    {
                        collect = false;
                    }
                    else if (name.SequenceEqual("is"u8))
                    {
                        inInline = false;
                    }
                    else if (name.SequenceEqual("row"u8))
                    {
                        _cancellation.ThrowIfCancellationRequested();
                        _sink.RowEnd(1);
                    }

                    break;
                case XmlTokenKind.Text:
                case XmlTokenKind.Whitespace:
                    if (collect)
                    {
                        _cell.AppendXml(r.Content);
                    }

                    break;
                case XmlTokenKind.CData:
                    if (collect)
                    {
                        _cell.AppendXml(r.Content, literal: true);
                    }

                    break;
            }
        }
    }

    private static CellType CellTypeOf(MarkupReader r)
    {
        if (!Ns.TryGetAttribute(r, "t"u8, out ReadOnlySpan<byte> t))
        {
            return CellType.Other;
        }

        // ECMA-376 Part 1 §18.18.11 ST_CellType.
        if (t.SequenceEqual("s"u8))
        {
            return CellType.Shared;
        }

        if (t.SequenceEqual("str"u8))
        {
            return CellType.FormulaString;
        }

        return t.SequenceEqual("inlineStr"u8) ? CellType.Inline : CellType.Other;
    }

    private bool EmitCell(CellType type, int sharedIndex, long offset)
    {
        ReadOnlySpan<char> text;
        switch (type)
        {
            case CellType.Shared:
                if (sharedIndex == -1)
                {
                    return false;
                }

                if (sharedIndex < 0 || sharedIndex >= _strings.Count)
                {
                    _context.Defects.Report(new Defect(
                        DefectKind.Reference,
                        "text.shared-string-out-of-range",
                        DefectSeverity.Dropped,
                        "A cell names a shared string that the table does not have; the cell is left out.",
                        DefectLocation.InPart(_partName, offset)));
                    return false;
                }

                text = _strings.Get(sharedIndex);
                break;
            case CellType.FormulaString:
            case CellType.Inline:
                text = _cell.AsSpan();
                break;
            default:
                return false;
        }

        if (text.IsEmpty)
        {
            return false;
        }

        _sink.CellStart(1);
        _sink.Paragraph(text, new ParagraphInfo(offset, 1, false));
        return true;
    }
}
