using System.Buffers;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>
/// A growing UTF-8 buffer the translators write XML into. Text and attribute values are escaped here, so the callers can pass
/// cell text, names and formulas straight through.
/// </summary>
internal sealed class XmlSink
{
    private byte[] _buffer;
    private int _length;

    public XmlSink(int capacity = 4096) => _buffer = new byte[capacity];

    public int Length => _length;

    public ReadOnlyMemory<byte> Memory => _buffer.AsMemory(0, _length);

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

    private Span<byte> Reserve(int n)
    {
        if (_buffer.Length - _length < n)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + n));
        }

        return _buffer.AsSpan(_length);
    }

    /// <summary>Appends markup that needs no escaping (ASCII or already escaped).</summary>
    public XmlSink Raw(string s)
    {
        int n = Encoding.UTF8.GetByteCount(s);
        Encoding.UTF8.GetBytes(s, Reserve(n));
        _length += n;
        return this;
    }

    public XmlSink Raw(ReadOnlySpan<byte> s)
    {
        s.CopyTo(Reserve(s.Length));
        _length += s.Length;
        return this;
    }

    /// <summary>Appends character data with &amp;, &lt; and &gt; escaped; control characters other than tab, line feed and carriage return become _xHHHH_ (ECMA-376 Part 1 §22.4.2.4).</summary>
    public XmlSink Text(string s) => Escape(s, attribute: false);

    private XmlSink Escape(string s, bool attribute)
    {
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            string? rep = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' when attribute => "&quot;",
                '\n' when attribute => "&#10;",
                '\r' when attribute => "&#13;",
                '\t' when attribute => "&#9;",
                '_' when LooksLikeEscape(s, i) => "_x005F_",
                _ when c < 0x20 && c != '\n' && c != '\r' && c != '\t' => "_x" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + "_",
                _ => null,
            };
            if (rep is null)
            {
                continue;
            }

            Raw(s.AsSpan(start, i - start));
            Raw(rep);
            start = i + 1;
        }

        Raw(s.AsSpan(start));
        return this;
    }

    // ECMA-376 Part 1 §22.4.2.4: text that already reads as _xHHHH_ is protected by escaping its underscore.
    private static bool LooksLikeEscape(string s, int i)
    {
        if (i + 6 >= s.Length || s[i + 1] != 'x' || s[i + 6] != '_')
        {
            return false;
        }

        for (int k = i + 2; k < i + 6; k++)
        {
            if (!char.IsAsciiHexDigit(s[k]))
            {
                return false;
            }
        }

        return true;
    }

    private XmlSink Raw(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty)
        {
            return this;
        }

        int n = Encoding.UTF8.GetByteCount(s);
        Encoding.UTF8.GetBytes(s, Reserve(n));
        _length += n;
        return this;
    }

    /// <summary>Appends <c> name="value"</c> with the value escaped.</summary>
    public XmlSink Attr(string name, string value)
    {
        Raw(" ").Raw(name).Raw("=\"");
        Escape(value, attribute: true);
        return Raw("\"");
    }

    public XmlSink Attr(string name, int value) => Raw(" ").Raw(name).Raw("=\"").Int(value).Raw("\"");

    public XmlSink Attr(string name, long value) => Raw(" ").Raw(name).Raw("=\"").Int(value).Raw("\"");

    public XmlSink Attr(string name, double value) => Raw(" ").Raw(name).Raw("=\"").Number(value).Raw("\"");

    public XmlSink Flag(string name, bool value) => Raw(" ").Raw(name).Raw(value ? "=\"1\"" : "=\"0\"");

    public XmlSink Int(long value)
    {
        Span<byte> s = Reserve(24);
        value.TryFormat(s, out int n, default, CultureInfo.InvariantCulture);
        _length += n;
        return this;
    }

    /// <summary>Appends the shortest decimal text that reads back as the same double.</summary>
    public XmlSink Number(double value)
    {
        Span<byte> s = Reserve(40);
        value.TryFormat(s, out int n, "R", CultureInfo.InvariantCulture);
        _length += n;
        return this;
    }

    public XmlSink Clear()
    {
        _length = 0;
        return this;
    }

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    public override string ToString() => Encoding.UTF8.GetString(_buffer, 0, _length);
}

/// <summary>The private markup that carries what the workbook model has no place for.</summary>
internal static class XlsbMarkup
{
    /// <summary>The namespace of the elements that carry records the model does not know (<c>xb:r</c>) and other binary-only facts.</summary>
    public const string Namespace = "urn:onedocument:xlsb";

    public const string Prefix = "xb";

    public const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Writes a record the model cannot hold as <c>&lt;xb:r t="type"&gt;base64&lt;/xb:r&gt;</c>, so a regenerated part writes it back where it was.</summary>
    public static void Raw(XmlSink x, int type, ReadOnlySpan<byte> data)
    {
        x.Raw("<xb:r t=\"").Int(type).Raw("\">").Raw(Convert.ToBase64String(data)).Raw("</xb:r>");
    }

    public static bool IsRaw(RecursiveAI.OneDocument.Markup.MarkupNode n) =>
        n.Kind == RecursiveAI.OneDocument.Markup.MarkupNodeKind.Element && n.NamespaceUri == Namespace && n.LocalName.SequenceEqual("r"u8);
}
