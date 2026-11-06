using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Packaging;

/// <summary>A <c>Default</c> entry of <c>[Content_Types].xml</c>: the content type of every part with this file extension.</summary>
/// <param name="Extension">The extension without its dot, as written.</param>
/// <param name="ContentType">The media type.</param>
public readonly record struct ContentTypeDefault(string Extension, string ContentType);

/// <summary>An <c>Override</c> entry of <c>[Content_Types].xml</c>: the content type of one named part.</summary>
/// <param name="PartName">The part name as written, such as <c>/word/document.xml</c>.</param>
/// <param name="ContentType">The media type.</param>
public readonly record struct ContentTypeOverride(string PartName, string ContentType);

/// <summary>
/// The content-type map of an Open Packaging Conventions package: <c>Default</c> entries by extension and <c>Override</c> entries
/// by part name (ECMA-376 Part 2 §7.2.3). An override wins over a default; extensions and part names compare ignoring ASCII
/// case, and percent-escapes of unreserved characters are treated as the characters themselves.
/// </summary>
public sealed class ContentTypes
{
    /// <summary>The relationships part's content type, which every package uses for its <c>.rels</c> parts.</summary>
    public const string RelationshipsContentType = "application/vnd.openxmlformats-package.relationships+xml";

    private readonly List<ContentTypeDefault> _defaults = [];
    private readonly List<ContentTypeOverride> _overrides = [];
    private readonly Dictionary<string, string> _defaultByExtension = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _overrideByName = new(StringComparer.Ordinal);

    /// <summary>Gets the <c>Default</c> entries in stored order.</summary>
    public IReadOnlyList<ContentTypeDefault> Defaults => _defaults;

    /// <summary>Gets the <c>Override</c> entries in stored order.</summary>
    public IReadOnlyList<ContentTypeOverride> Overrides => _overrides;

    /// <summary>Finds the content type of a part.</summary>
    /// <param name="partName">The part name, such as <c>/word/document.xml</c>.</param>
    /// <returns>The override for the name, else the default for its extension, else null.</returns>
    public string? Resolve(string partName)
    {
        ArgumentNullException.ThrowIfNull(partName);
        if (_overrideByName.TryGetValue(OpcNames.Key(partName), out string? over))
        {
            return over;
        }

        string ext = ExtensionOf(partName);
        return ext.Length > 0 && _defaultByExtension.TryGetValue(OpcNames.Key(ext), out string? def) ? def : null;
    }

    internal bool Dirty { get; private set; }

    internal static string ExtensionOf(string partName)
    {
        int slash = partName.LastIndexOf('/');
        int dot = partName.LastIndexOf('.');
        return dot > slash && dot < partName.Length - 1 ? partName[(dot + 1)..] : string.Empty;
    }

    internal bool AddDefault(string extension, string contentType)
    {
        if (!_defaultByExtension.TryAdd(OpcNames.Key(extension), contentType))
        {
            return false;
        }

        _defaults.Add(new ContentTypeDefault(extension, contentType));
        return true;
    }

    internal bool AddOverride(string partName, string contentType)
    {
        if (!_overrideByName.TryAdd(OpcNames.Key(partName), contentType))
        {
            return false;
        }

        _overrides.Add(new ContentTypeOverride(partName, contentType));
        return true;
    }

    // A part added later needs its type to resolve: nothing is written when the map already says it, otherwise an override is added.
    internal void Ensure(string partName, string contentType)
    {
        string? current = Resolve(partName);
        if (string.Equals(current, contentType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string key = OpcNames.Key(partName);
        if (_overrideByName.ContainsKey(key))
        {
            _overrideByName[key] = contentType;
            int i = _overrides.FindIndex(o => OpcNames.Key(o.PartName) == key);
            _overrides[i] = new ContentTypeOverride(_overrides[i].PartName, contentType);
        }
        else
        {
            AddOverride(partName, contentType);
        }

        Dirty = true;
    }

    internal void RemoveOverride(string partName)
    {
        string key = OpcNames.Key(partName);
        if (_overrideByName.Remove(key))
        {
            _overrides.RemoveAll(o => OpcNames.Key(o.PartName) == key);
            Dirty = true;
        }
    }

    // Removing a part leaves its type's Default behind; a caller that knows no part still uses the extension drops it.
    internal bool RemoveDefault(string extension)
    {
        string key = OpcNames.Key(extension);
        if (!_defaultByExtension.Remove(key))
        {
            return false;
        }

        _defaults.RemoveAll(d => OpcNames.Key(d.Extension) == key);
        Dirty = true;
        return true;
    }

    internal void MarkClean() => Dirty = false;

    internal void MarkDirty() => Dirty = true;

    /// <summary>Reads <c>[Content_Types].xml</c> tolerantly: what can be read is kept, each repair is reported.</summary>
    internal static ContentTypes Parse(ReadOnlyMemory<byte> xml, string name, OfficeLimits limits, IDefectSink defects)
    {
        var map = new ContentTypes();
        using var reader = new OpcXmlReader(xml, limits, name);
        try
        {
            bool sawTypes = false;
            while (reader.Read())
            {
                if (reader.Token != OpcXmlToken.StartElement)
                {
                    continue;
                }

                if (reader.Depth == 1)
                {
                    if (reader.LocalName != "Types")
                    {
                        throw new OpcXmlException("The root element is not Types.", reader.TokenStart);
                    }

                    sawTypes = true;
                    continue;
                }

                if (reader.Depth != 2 || !sawTypes)
                {
                    continue;
                }

                if (reader.LocalName == "Default")
                {
                    string? ext = reader.GetAttribute("Extension");
                    string? type = reader.GetAttribute("ContentType");
                    if (string.IsNullOrEmpty(ext) || string.IsNullOrEmpty(type))
                    {
                        Report(defects, "packaging.bad-content-type-entry", DefectSeverity.Dropped, "A Default entry lacks its Extension or ContentType.", name, reader.TokenStart);
                    }
                    else if (!map.AddDefault(ext, type))
                    {
                        Report(defects, "packaging.duplicate-content-type", DefectSeverity.Dropped, $"The extension '{ext}' has more than one Default entry; the first is used.", name, reader.TokenStart);
                    }
                }
                else if (reader.LocalName == "Override")
                {
                    string? pn = reader.GetAttribute("PartName");
                    string? type = reader.GetAttribute("ContentType");
                    if (string.IsNullOrEmpty(pn) || string.IsNullOrEmpty(type) || !PartUri.IsValid(pn))
                    {
                        Report(defects, "packaging.bad-content-type-entry", DefectSeverity.Dropped, "An Override entry lacks a valid PartName or its ContentType.", name, reader.TokenStart);
                    }
                    else if (!map.AddOverride(pn, type))
                    {
                        Report(defects, "packaging.duplicate-content-type", DefectSeverity.Dropped, $"The part '{pn}' has more than one Override entry; the first is used.", name, reader.TokenStart);
                    }
                }
            }

            if (!sawTypes)
            {
                throw new OpcXmlException("There is no Types element.", 0);
            }
        }
        catch (OpcXmlException ex)
        {
            if (ex.IsDtd)
            {
                throw new OfficeFormatException(DefectKind.Packaging, "packaging.dtd-refused", "A document type declaration is not allowed.", DefectLocation.InPart(name, ex.Offset), ex);
            }

            if (map._defaults.Count == 0 && map._overrides.Count == 0)
            {
                throw new OfficeFormatException(DefectKind.Packaging, "packaging.bad-content-types", ex.Message, DefectLocation.InPart(name, ex.Offset), ex);
            }

            Report(defects, "packaging.bad-content-types", DefectSeverity.Repaired, ex.Message + " The entries before it are used.", name, ex.Offset);
        }

        return map;
    }

    /// <summary>Writes <c>[Content_Types].xml</c>: defaults first, then overrides, in the stored order.</summary>
    internal byte[] ToXml()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        foreach (ContentTypeDefault d in _defaults)
        {
            sb.Append("<Default Extension=\"").Append(OpcXmlWriter.Escape(d.Extension)).Append("\" ContentType=\"").Append(OpcXmlWriter.Escape(d.ContentType)).Append("\"/>");
        }

        foreach (ContentTypeOverride o in _overrides)
        {
            sb.Append("<Override PartName=\"").Append(OpcXmlWriter.Escape(o.PartName)).Append("\" ContentType=\"").Append(OpcXmlWriter.Escape(o.ContentType)).Append("\"/>");
        }

        sb.Append("</Types>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    internal static void Report(IDefectSink sink, string code, DefectSeverity severity, string message, string part, long offset) =>
        sink.Report(new Defect(DefectKind.Packaging, code, severity, message, DefectLocation.InPart(part, offset)));
}

/// <summary>Part-name comparison and the mapping between part names and zip item names.</summary>
internal static class OpcNames
{
    /// <summary>
    /// Makes the key under which two spellings of one part name collide. ECMA-376 Part 2 §6.2.2.3: names are equivalent when they
    /// differ only in ASCII case. §6.2.2.2 forbids percent-encoded unreserved characters in a part name, and §7.3.5 un-encodes
    /// non-ASCII characters, so those spellings are folded to the characters too (an input that uses them is repaired, not refused).
    /// </summary>
    public static string Key(string name)
    {
        bool plain = true;
        foreach (char c in name)
        {
            if (c == '%' || c is >= 'a' and <= 'z')
            {
                plain = false;
                break;
            }
        }

        if (plain)
        {
            return name;
        }

        string folded = Decode(name, unreservedToo: true);
        return string.Create(folded.Length, folded, static (span, s) =>
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                span[i] = c is >= 'a' and <= 'z' ? (char)(c - 32) : c;
            }
        });
    }

    /// <summary>Decodes the percent-escapes of non-ASCII characters in a relative reference, as for zip item names.</summary>
    public static string UnescapeNonAscii(string s) => Decode(s, unreservedToo: false);

    /// <summary>ECMA-376 Part 2 §7.3.5: a zip item name becomes a logical item name by un-percent-encoding non-ASCII characters and adding the leading slash.</summary>
    public static string FromZipName(string zipName) => "/" + Decode(zipName, unreservedToo: false);

    /// <summary>ECMA-376 Part 2 §7.3.4: a logical item name becomes a zip item name by removing the leading slash and percent-encoding every non-ASCII character.</summary>
    public static string ToZipName(string partName)
    {
        string name = partName.Length > 0 && partName[0] == '/' ? partName[1..] : partName;
        bool ascii = true;
        foreach (char c in name)
        {
            if (c > 127)
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 16);
        foreach (System.Text.Rune r in name.EnumerateRunes())
        {
            if (r.Value < 128)
            {
                sb.Append((char)r.Value);
                continue;
            }

            Span<byte> bytes = new byte[4]; // At most four UTF-8 bytes per scalar value.
            int n = r.EncodeToUtf8(bytes);
            for (int i = 0; i < n; i++)
            {
                sb.Append('%').Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    // Decodes %XX runs that make valid non-ASCII UTF-8 into characters, and (when asked) escapes of unreserved ASCII characters. Any
    // other escape is kept, with its hex digits in upper case so two spellings of one escape are equal.
    private static string Decode(string s, bool unreservedToo)
    {
        if (s.IndexOf('%', StringComparison.Ordinal) < 0)
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] != '%' || !TryByte(s, i, out int b))
            {
                sb.Append(s[i]);
                i++;
                continue;
            }

            if (b >= 0x80)
            {
                var bytes = new List<byte>();
                int j = i;
                while (j < s.Length && s[j] == '%' && TryByte(s, j, out int nb) && nb >= 0x80)
                {
                    bytes.Add((byte)nb);
                    j += 3;
                }

                int consumed = 0;
                string decoded = DecodeUtf8Prefix(bytes, out consumed);
                if (consumed > 0)
                {
                    sb.Append(decoded);
                    i += consumed * 3;
                    continue;
                }
            }
            else if (unreservedToo && IsUnreserved((char)b))
            {
                sb.Append((char)b);
                i += 3;
                continue;
            }

            sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            i += 3;
        }

        return sb.ToString();
    }

    private static string DecodeUtf8Prefix(List<byte> bytes, out int consumed)
    {
        var sb = new StringBuilder();
        consumed = 0;
        int k = 0;
        byte[] all = bytes.ToArray();
        while (k < all.Length)
        {
            ReadOnlySpan<byte> span = all.AsSpan(k);
            if (System.Text.Rune.DecodeFromUtf8(span, out System.Text.Rune rune, out int used) != System.Buffers.OperationStatus.Done)
            {
                break;
            }

            sb.Append(rune.ToString());
            k += used;
            consumed = k;
        }

        return sb.ToString();
    }

    private static bool TryByte(string s, int i, out int value)
    {
        value = 0;
        if (i + 2 >= s.Length)
        {
            return false;
        }

        return int.TryParse(s.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsUnreserved(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '.' or '_' or '~';
}

/// <summary>Escaping for the XML the packaging layer writes.</summary>
internal static class OpcXmlWriter
{
    private static readonly System.Buffers.SearchValues<char> s_special = System.Buffers.SearchValues.Create("&<>\"\r\n\t");

    /// <summary>Escapes text for an attribute value in double quotes.</summary>
    public static string Escape(string value)
    {
        if (value.AsSpan().IndexOfAny(s_special) < 0)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\r': sb.Append("&#13;"); break;
                case '\n': sb.Append("&#10;"); break;
                case '\t': sb.Append("&#9;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}
