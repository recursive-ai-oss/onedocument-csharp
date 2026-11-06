using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Doc.Binary;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Doc;

/// <summary>Which story a piece of text belongs to; decides how reference marks and separators are written.</summary>
internal enum StoryKind
{
    Main,
    Footnote,
    Endnote,
    Header,
    Comment,
    TextBox,
}

/// <summary>
/// Converts a Word binary file into WordprocessingML parts. The text, its character and paragraph runs, tables, sections, headers
/// and footers, notes, comments, lists, fields, bookmarks, tracked changes and pictures are carried over; what the model cannot
/// hold is reported to the loss sink and the defect sink.
/// </summary>
internal sealed partial class DocConverter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private readonly DocFile _f;
    private readonly DocReader _r;
    private readonly ILossSink _loss;
    private readonly IDefectSink _sink;
    private readonly DocStyleSheet _styles;
    private readonly DocFontTable _fonts;
    private readonly DocLists _lists;
    private readonly FormatContext _fmt;
    private readonly Dictionary<int, string> _styleIds = [];
    private readonly Dictionary<int, int> _fndRefs = [];
    private readonly Dictionary<int, int> _ednRefs = [];
    private readonly Dictionary<int, int> _atnRefs = [];
    private readonly Dictionary<int, List<string>> _events = [];
    private readonly Dictionary<int, SpaInfo> _spa = [];
    private readonly List<string> _revAuthors;
    private readonly List<string> _commentAuthors;
    private readonly List<(string Name, string ContentType, byte[] Bytes)> _media = [];
    private readonly HashSet<ushort> _reportedSprms = [];
    private DocArt.Drawing? _art;
    private int _bookmarkId = 1;
    private int _revisionId = 1;
    private int _drawingId = 1;
    private int _textBoxes;
    private int _oleObjects;
    private int _formFields;
    private int _shapes;
    private int _unmappedSprms;
    private int _fields;

    internal DocConverter(DocFile file, ILossSink loss)
    {
        _f = file;
        _r = new DocReader(file);
        _loss = loss;
        _sink = file.Context.Defects;
        _styles = DocStyleSheet.Read(file);
        _fonts = DocFontTable.Read(file);
        _lists = DocLists.Read(file);
        BuildStyleIds();
        _fmt = new FormatContext(_fonts, _styles, istd => _styleIds.TryGetValue(istd, out string? id) ? id : null);
        _revAuthors = ReadSttb(DocFib.SttbfRMark);
        _commentAuthors = ReadXstArray(DocFib.GrpXstAtnOwners);
    }

    private readonly record struct SpaInfo(int Spid, int Left, int Top, int Right, int Bottom, int Flags);

    /// <summary>The parts of the converted document.</summary>
    internal sealed class Part(string name, string contentType, string xml)
    {
        internal string Name { get; } = name;

        internal string ContentType { get; } = contentType;

        internal string Xml { get; set; } = xml;

        internal List<Relationship> Rels { get; } = [];

        /// <summary>Adds a relationship from this part and returns its identifier.</summary>
        internal string AddRel(string type, string target)
        {
            string id = "rId" + (Rels.Count + 1).ToString(CultureInfo.InvariantCulture);
            Rels.Add(new Relationship(id, type, target));
            return id;
        }
    }

    private void BuildStyleIds()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _styles.Styles.Count; i++)
        {
            DocStyle? s = _styles.Styles[i];
            if (s is null)
            {
                continue;
            }

            string primary = StyleName(s);
            var sb = new StringBuilder();
            bool up = true;
            foreach (char c in primary)
            {
                if (char.IsLetterOrDigit(c) && c < 0x80)
                {
                    sb.Append(up ? char.ToUpperInvariant(c) : c);
                    up = false;
                }
                else
                {
                    up = true;
                }
            }

            string id = sb.Length == 0 ? "Style" + i.ToString(CultureInfo.InvariantCulture) : sb.ToString();
            if (char.IsDigit(id[0]))
            {
                id = "S" + id;
            }

            string unique = id;
            for (int n = 2; !used.Add(unique); n++)
            {
                unique = id + n.ToString(CultureInfo.InvariantCulture);
            }

            _styleIds[i] = unique;
        }
    }

    // [MS-DOC] StdfBase.sti: the invariant identifier of an application-defined style, used when the file stores no name for it.
    private static string StyleName(DocStyle s)
    {
        string name = s.Name;
        int comma = name.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0)
        {
            name = name[..comma];
        }

        if (name.Length > 0)
        {
            return name;
        }

        return s.Sti switch
        {
            0 => "Normal",
            < 10 => "heading " + (s.Sti).ToString(CultureInfo.InvariantCulture),
            65 => "Default Paragraph Font",
            105 => "Normal Table",
            107 => "No List",
            _ => "Style" + s.Istd.ToString(CultureInfo.InvariantCulture),
        };
    }

    private List<string> ReadSttb(int pair)
    {
        var list = new List<string>();
        (uint fc, uint lcb) = _f.Fib.Pair(pair);
        byte[] t = _f.Table;
        if (lcb < 6 || (long)fc + 6 > t.Length)
        {
            return list;
        }

        int end = (int)Math.Min((long)fc + lcb, t.Length);
        int pos = (int)fc;
        bool unicode = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(pos)) == 0xFFFF;
        if (unicode)
        {
            pos += 2;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(pos));
        int cbExtra = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(pos + 2));
        pos += 4;
        for (int i = 0; i < count && pos < end; i++)
        {
            if (unicode)
            {
                int cch = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(pos));
                pos += 2;
                if (pos + (cch * 2) > end)
                {
                    break;
                }

                list.Add(Encoding.Unicode.GetString(t, pos, cch * 2));
                pos += (cch * 2) + cbExtra;
            }
            else
            {
                int cch = t[pos++];
                if (pos + cch > end)
                {
                    break;
                }

                var sb = new StringBuilder(cch);
                for (int k = 0; k < cch; k++)
                {
                    sb.Append((char)t[pos + k]);
                }

                list.Add(sb.ToString());
                pos += cch + cbExtra;
            }
        }

        return list;
    }

    private List<string> ReadXstArray(int pair)
    {
        var list = new List<string>();
        (uint fc, uint lcb) = _f.Fib.Pair(pair);
        byte[] t = _f.Table;
        if (lcb < 2 || (long)fc + 2 > t.Length)
        {
            return list;
        }

        int end = (int)Math.Min((long)fc + lcb, t.Length);
        int pos = (int)fc;
        while (pos + 2 <= end)
        {
            int cch = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(pos));
            pos += 2;
            if (pos + (cch * 2) > end)
            {
                break;
            }

            list.Add(Encoding.Unicode.GetString(t, pos, cch * 2));
            pos += cch * 2;
        }

        return list;
    }

    private static string AuthorOf(List<string> list, int index) => (uint)index < (uint)list.Count && list[index].Length > 0 ? list[index] : "Unknown";

    // [MS-DOC] DTTM: minutes (6 bits), hours (5), day (5), month (4), year since 1900 (9), weekday (3).
    private static string? DttmToIso(uint dttm)
    {
        if (dttm == 0)
        {
            return null;
        }

        int min = (int)(dttm & 0x3F);
        int hr = (int)((dttm >> 6) & 0x1F);
        int day = (int)((dttm >> 11) & 0x1F);
        int mon = (int)((dttm >> 16) & 0x0F);
        int yr = 1900 + (int)((dttm >> 20) & 0x1FF);
        if (mon is < 1 or > 12 || day is < 1 or > 31 || hr > 23 || min > 59)
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{yr:D4}-{mon:D2}-{day:D2}T{hr:D2}:{min:D2}:00Z");
    }

    private string? NumId(int ilfo) =>
        ilfo >= 1 && ilfo <= _lists.Overrides.Count && _lists.Lists.Exists(l => l.Lsid == _lists.Overrides[ilfo - 1].Lsid)
            ? ilfo.ToString(CultureInfo.InvariantCulture)
            : null;
}
