using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>
/// What the streaming reader needs from the style sheet: the number format of each cell format (<c>cellXfs</c> entry) and whether it
/// shows dates or times. Nothing else of the style sheet is kept.
/// </summary>
/// <remarks>ECMA-376 Part 1 CT_Stylesheet: <c>numFmts</c> holds the custom codes; <c>cellXfs</c> holds the formats that a cell's <c>s</c> indexes.</remarks>
internal sealed class StreamStyles
{
    private readonly int[] _numFmtIds;
    private readonly bool[] _isDate;

    private StreamStyles(int[] ids, bool[] isDate)
    {
        _numFmtIds = ids;
        _isDate = isDate;
    }

    public static StreamStyles Empty { get; } = new([], []);

    public int Count => _numFmtIds.Length;

    public int NumberFormatId(int style) => (uint)style < (uint)_numFmtIds.Length ? _numFmtIds[style] : 0;

    public bool IsDate(int style) => (uint)style < (uint)_isDate.Length && _isDate[style];

    public static StreamStyles Read(Stream part, OpenContext context, string partName)
    {
        using var reader = new MarkupReader(part, context, partName, leaveOpen: true);
        var codes = new Dictionary<int, string>();
        var ids = new List<int>();
        string? section = null;
        while (reader.Read())
        {
            if (reader.Depth == 1 && reader.Kind is XmlTokenKind.StartElement or XmlTokenKind.EmptyElement)
            {
                ReadOnlySpan<byte> n = reader.LocalName;
                if (n.SequenceEqual("numFmts"u8))
                {
                    section = "numFmts";
                }
                else if (n.SequenceEqual("cellXfs"u8))
                {
                    section = "cellXfs";
                }
                else
                {
                    section = null;
                    if (reader.Kind == XmlTokenKind.StartElement)
                    {
                        reader.Skip();
                    }
                }

                continue;
            }

            if (reader.Depth != 2 || section is null || reader.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement))
            {
                continue;
            }

            int id = 0;
            string? code = null;
            foreach (XmlAttributeToken a in reader.Attributes)
            {
                if (a.Prefix.Length != 0)
                {
                    continue;
                }

                if (a.LocalName.SequenceEqual("numFmtId"u8))
                {
                    _ = StreamXml.TryInt(a.RawValue, out id);
                }
                else if (a.LocalName.SequenceEqual("formatCode"u8))
                {
                    code = XmlText.GetString(a.RawValue, true);
                }
            }

            if (section == "numFmts" && reader.LocalName.SequenceEqual("numFmt"u8) && code is not null)
            {
                codes[id] = code;
            }
            else if (section == "cellXfs" && reader.LocalName.SequenceEqual("xf"u8))
            {
                ids.Add(id);
            }

            if (reader.Kind == XmlTokenKind.StartElement)
            {
                reader.Skip();
            }
        }

        bool[] isDate = new bool[ids.Count];
        var byId = new Dictionary<int, bool>();
        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            if (!byId.TryGetValue(id, out bool d))
            {
                NumberFormat? f = codes.TryGetValue(id, out string? c) ? (NumberFormat.TryParse(c, out NumberFormat? parsed, out _) ? parsed : null) : NumberFormat.GetBuiltIn(id);
                d = f?.IsDateTime ?? false;
                byId[id] = d;
            }

            isDate[i] = d;
        }

        return new StreamStyles([.. ids], isDate);
    }
}
