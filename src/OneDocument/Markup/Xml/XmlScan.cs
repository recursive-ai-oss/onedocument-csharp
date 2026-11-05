using System.Buffers;
using System.Text.Unicode;

namespace RecursiveAI.OneDocument.Markup.Xml;

/// <summary>The lexical rules of XML 1.0 that the tokenizer, the markup tree and the writer share.</summary>
internal static class XmlScan
{
    private const byte NameStartBit = 1;
    private const byte NameBit = 2;
    private const byte SpaceBit = 4;
    private const byte HighBit = 8;

    private static readonly byte[] s_class = BuildClassTable();

    // XML 1.0 5th ed. §2.2 Char: control characters other than tab, line feed and carriage return are not allowed.
    private static readonly SearchValues<byte> s_textStop = SearchValues.Create(Controls(include: [(byte)'&', (byte)'>']));
    private static readonly SearchValues<byte> s_attributeStop = SearchValues.Create(Controls(include: [(byte)'&', (byte)'<']));
    private static readonly SearchValues<byte> s_rawStop = SearchValues.Create(Controls(include: []));
    private static readonly SearchValues<byte> s_notWhitespace = SearchValues.Create([(byte)' ', (byte)'\t', (byte)'\n', (byte)'\r']);

    internal enum DataMode
    {
        /// <summary>Element content: references are checked and <c>]]&gt;</c> is refused.</summary>
        Text,

        /// <summary>Attribute value: references are checked and <c>&lt;</c> is refused.</summary>
        Attribute,

        /// <summary>Comment, CDATA section or processing-instruction data: only characters are checked.</summary>
        Raw,
    }

    private static byte[] Controls(byte[] include)
    {
        var list = new List<byte>();
        for (byte b = 0; b < 0x20; b++)
        {
            if (b is not ((byte)'\t' or (byte)'\n' or (byte)'\r'))
            {
                list.Add(b);
            }
        }

        list.AddRange(include);
        return [.. list];
    }

    private static byte[] BuildClassTable()
    {
        var t = new byte[256];
        for (int c = 'a'; c <= 'z'; c++)
        {
            t[c] = NameStartBit | NameBit;
            t[c - 32] = NameStartBit | NameBit;
        }

        t['_'] = NameStartBit | NameBit;
        t[':'] = NameStartBit | NameBit;
        for (int c = '0'; c <= '9'; c++)
        {
            t[c] = NameBit;
        }

        t['-'] = NameBit;
        t['.'] = NameBit;
        t[' '] = SpaceBit;
        t['\t'] = SpaceBit;
        t['\n'] = SpaceBit;
        t['\r'] = SpaceBit;
        for (int c = 0x80; c < 256; c++)
        {
            t[c] = NameStartBit | NameBit | HighBit;
        }

        return t;
    }

    internal static bool IsSpace(byte b) => (s_class[b] & SpaceBit) != 0;

    internal static bool IsNameStart(byte b) => (s_class[b] & NameStartBit) != 0;

    internal static int SkipSpace(ReadOnlySpan<byte> d, int p)
    {
        while (p < d.Length && (s_class[d[p]] & SpaceBit) != 0)
        {
            p++;
        }

        return p;
    }

    /// <summary>Returns the index after the name that starts at <paramref name="p"/>, or <paramref name="p"/> if no name starts there.</summary>
    internal static int ScanName(ReadOnlySpan<byte> d, int p)
    {
        if (p >= d.Length || (s_class[d[p]] & NameStartBit) == 0)
        {
            return p;
        }

        p++;
        while (p < d.Length && (s_class[d[p]] & NameBit) != 0)
        {
            p++;
        }

        return p;
    }

    internal static bool IsWhitespaceOnly(ReadOnlySpan<byte> s) => s.IndexOfAnyExcept(s_notWhitespace) < 0;

    /// <summary>
    /// Checks a qualified name as Namespaces in XML 1.0 §3 defines it: at most one colon, neither part empty, and the local part
    /// does not start with a digit, hyphen or full stop. Non-ASCII bytes are accepted when they are valid UTF-8.
    /// </summary>
    /// <returns>The index of the colon in <paramref name="name"/>, or -1.</returns>
    internal static int ValidateQualifiedName(ReadOnlySpan<byte> name, long offset, bool allowColon)
    {
        int colon = name.IndexOf((byte)':');
        bool ok = name.Length > 0;
        if (ok && colon >= 0)
        {
            ok = allowColon && colon > 0 && colon < name.Length - 1 && name[(colon + 1)..].IndexOf((byte)':') < 0 && IsNameStart(name[colon + 1]);
        }

        if (ok)
        {
            foreach (byte b in name)
            {
                if (b >= 0x80)
                {
                    ok = Utf8.IsValid(name);
                    break;
                }
            }
        }

        if (!ok)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidName, offset, "The name is not a valid XML name.");
        }

        return colon;
    }

    /// <summary>Checks character data. <paramref name="offset"/> is the absolute offset of <paramref name="s"/> for error reports.</summary>
    internal static void ValidateData(ReadOnlySpan<byte> s, long offset, DataMode mode)
    {
        SearchValues<byte> stop = mode switch { DataMode.Text => s_textStop, DataMode.Attribute => s_attributeStop, _ => s_rawStop };
        int i = 0;
        while (true)
        {
            int k = s[i..].IndexOfAny(stop);
            if (k < 0)
            {
                break;
            }

            i += k;
            byte b = s[i];
            if (b == '&')
            {
                i = ParseReference(s, i, offset);
                continue;
            }

            if (b == '>')
            {
                // XML 1.0 §2.4: the literal sequence "]]>" must not appear in character data.
                if (i >= 2 && s[i - 1] == ']' && s[i - 2] == ']')
                {
                    throw new XmlSyntaxException(XmlErrorCode.InvalidCharacter, offset + i - 2, "\"]]>\" is not allowed in character data.");
                }

                i++;
                continue;
            }

            if (b == '<')
            {
                throw new XmlSyntaxException(XmlErrorCode.InvalidCharacter, offset + i, "\"<\" is not allowed in an attribute value.");
            }

            throw new XmlSyntaxException(XmlErrorCode.InvalidCharacter, offset + i, "A control character is not allowed in XML.");
        }

        if (!Utf8.IsValid(s))
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidEncoding, offset + FirstInvalidUtf8(s), "The bytes are not valid UTF-8.");
        }
    }

    private static int FirstInvalidUtf8(ReadOnlySpan<byte> s)
    {
        int i = 0;
        while (i < s.Length)
        {
            OperationStatus st = System.Text.Rune.DecodeFromUtf8(s[i..], out _, out int used);
            if (st != OperationStatus.Done)
            {
                return i;
            }

            i += used;
        }

        return 0;
    }

    /// <summary>Validates the reference that starts with the ampersand at <paramref name="i"/> and returns the index after its semicolon.</summary>
    private static int ParseReference(ReadOnlySpan<byte> s, int i, long offset)
    {
        int semi = s[i..].IndexOf((byte)';');
        if (semi < 0 || semi > 12)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidReference, offset + i, "A reference is not terminated by a semicolon.");
        }

        ReadOnlySpan<byte> body = s.Slice(i + 1, semi - 1);
        if (body.Length > 0 && body[0] == '#')
        {
            if (!TryParseCharReference(body, out _))
            {
                throw new XmlSyntaxException(XmlErrorCode.InvalidReference, offset + i, "A character reference does not name an XML character.");
            }
        }
        else if (!(body.SequenceEqual("amp"u8) || body.SequenceEqual("lt"u8) || body.SequenceEqual("gt"u8) || body.SequenceEqual("quot"u8) || body.SequenceEqual("apos"u8)))
        {
            // No DTD is ever read, so only the five predefined entities exist.
            throw new XmlSyntaxException(XmlErrorCode.InvalidReference, offset + i, "The entity is not one of the five predefined entities.");
        }

        return i + semi + 1;
    }

    /// <summary>Parses the body of a character reference (the text between the ampersand and the semicolon).</summary>
    internal static bool TryParseCharReference(ReadOnlySpan<byte> body, out int codePoint)
    {
        codePoint = 0;
        bool hex = body.Length > 1 && body[1] == 'x';
        ReadOnlySpan<byte> digits = body[(hex ? 2 : 1)..];
        if (digits.Length == 0 || digits.Length > 8)
        {
            return false;
        }

        long v = 0;
        foreach (byte b in digits)
        {
            int dv;
            if (b >= '0' && b <= '9')
            {
                dv = b - '0';
            }
            else if (hex && b >= 'a' && b <= 'f')
            {
                dv = b - 'a' + 10;
            }
            else if (hex && b >= 'A' && b <= 'F')
            {
                dv = b - 'A' + 10;
            }
            else
            {
                return false;
            }

            v = (v * (hex ? 16 : 10)) + dv;
        }

        // XML 1.0 §2.2 Char.
        bool valid = v is 0x9 or 0xA or 0xD || (v >= 0x20 && v <= 0xD7FF) || (v >= 0xE000 && v <= 0xFFFD) || (v >= 0x10000 && v <= 0x10FFFF);
        codePoint = valid ? (int)v : 0;
        return valid;
    }
}
