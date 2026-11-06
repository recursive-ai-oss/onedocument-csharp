using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using RecursiveAI.OneArchive.Cfb;

namespace RecursiveAI.OneDocument.Redaction.Tests;

/// <summary>
/// The reader the redaction has to defeat: it knows nothing about the redaction code and tries every common way of getting a string out of a
/// file. Zip entries, gzip and zlib streams, compound-file streams (with the macro-project decompression), XML text with character
/// references, <c>_xHHHH_</c> escapes, percent escapes, text joined across elements, base64 tokens, Unicode normalisation and case.
/// </summary>
internal static class Attacker
{
    private static readonly Regex Base64Token = new("[A-Za-z0-9+/]{20,}={0,2}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex XEscape = new("_x([0-9A-Fa-f]{4})_", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Every place in the zip where one of the secrets can be read, as <c>entry (how)</c>.</summary>
    public static List<string> Recover(byte[] zip, params string[] secrets)
    {
        var hits = new List<string>();
        Bytes(zip, string.Empty, secrets, hits, 0);
        return [.. hits.Distinct()];
    }

    /// <summary>The secret as the file stores it at the byte level, in the encodings a reader might try.</summary>
    private static IEnumerable<byte[]> Needles(string secret)
    {
        foreach (string s in new[] { secret, secret.ToLowerInvariant(), secret.ToUpperInvariant() })
        {
            yield return Encoding.UTF8.GetBytes(s);
            yield return Encoding.Unicode.GetBytes(s);
            yield return Encoding.BigEndianUnicode.GetBytes(s);
            yield return Encoding.Latin1.GetBytes(s);
        }
    }

    // Lower case, canonical decomposition, no combining marks, no format characters (zero-width, soft hyphen), no whitespace.
    internal static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c0 in s)
        {
            // The library runs with invariant globalization, where Normalize leaves non-ASCII text alone; map the accents the cases use by hand.
            char c = c0 switch { 'é' or 'è' or 'ê' => 'e', 'ü' => 'u', 'ö' => 'o', 'ä' => 'a', _ => c0 };
            UnicodeCategory cat = char.GetUnicodeCategory(c);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format or UnicodeCategory.SpaceSeparator || char.IsWhiteSpace(c))
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static void Text(string text, string where, string[] secrets, List<string> hits, int depth, string how)
    {
        foreach (string variant in Variants(text))
        {
            string folded = Fold(variant);
            foreach (string secret in secrets)
            {
                if (folded.Contains(Fold(secret), StringComparison.Ordinal))
                {
                    hits.Add($"{where} ({how})");
                    break;
                }
            }
        }

        if (depth < 6)
        {
            foreach (Match m in Base64Token.Matches(text))
            {
                try
                {
                    string t = m.Value;
                    byte[] decoded = Convert.FromBase64String(t.Length % 4 == 0 ? t : t + new string('=', 4 - (t.Length % 4)));
                    Bytes(decoded, where + "[base64]", secrets, hits, depth + 1);
                }
                catch (FormatException)
                {
                }
            }
        }
    }

    private static IEnumerable<string> Variants(string text)
    {
        yield return text;
        if (text.Contains("_x", StringComparison.Ordinal))
        {
            yield return XEscape.Replace(text, static m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
        }

        if (text.Contains('%', StringComparison.Ordinal))
        {
            string? unescaped = null;
            try
            {
                unescaped = Uri.UnescapeDataString(text);
            }
            catch (UriFormatException)
            {
            }

            if (unescaped is not null)
            {
                yield return unescaped;
            }
        }
    }

    private static void Bytes(byte[] data, string where, string[] secrets, List<string> hits, int depth)
    {
        if (depth > 8 || data.Length == 0)
        {
            return;
        }

        foreach (byte[] needle in Needles(secrets[0]).Concat(secrets.Skip(1).SelectMany(Needles)))
        {
            if (data.AsSpan().IndexOf(needle) >= 0)
            {
                hits.Add($"{where} (raw bytes)");
                break;
            }
        }

        if (data.Length > 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 3 && data[3] == 4)
        {
            try
            {
                using var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
                foreach (ZipArchiveEntry e in archive.Entries)
                {
                    Text(e.FullName, where + e.FullName, secrets, hits, depth + 6, "entry name");
                    using var ms = new MemoryStream();
                    using (Stream s = e.Open())
                    {
                        s.CopyTo(ms);
                    }

                    Bytes(ms.ToArray(), where + e.FullName + "!", secrets, hits, depth + 1);
                }
            }
            catch (InvalidDataException)
            {
            }

            return;
        }

        if (data.Length > 20 && data[0] == 0x1F && data[1] == 0x8B)
        {
            Inflate(() => new GZipStream(new MemoryStream(data), CompressionMode.Decompress), where + "[gzip]", secrets, hits, depth);
        }

        if (data.Length > 6 && data[0] == 0x78 && data[1] is 0x01 or 0x5E or 0x9C or 0xDA)
        {
            Inflate(() => new ZLibStream(new MemoryStream(data), CompressionMode.Decompress), where + "[zlib]", secrets, hits, depth);
        }

        if (data.Length > 512 && data[0] == 0xD0 && data[1] == 0xCF && data[2] == 0x11 && data[3] == 0xE0)
        {
            CompoundFile(data, where, secrets, hits, depth);
        }

        string? xml = TryXml(data, where, secrets, hits, depth);
        if (xml is null)
        {
            ZlibInside(data, where, secrets, hits, depth);
            Text(Encoding.Latin1.GetString(data), where, secrets, hits, depth, "text");
        }
    }

    // A zlib stream can start anywhere in a binary part (a PNG text chunk, an object header): try every plausible start.
    private static void ZlibInside(byte[] data, string where, string[] secrets, List<string> hits, int depth)
    {
        int tried = 0;
        for (int i = 1; i + 8 < data.Length && tried < 4000; i++)
        {
            if (data[i] == 0x78 && data[i + 1] is 0x01 or 0x5E or 0x9C or 0xDA && ((data[i] << 8) | data[i + 1]) % 31 == 0)
            {
                tried++;
                int at = i;
                Inflate(() => new ZLibStream(new MemoryStream(data, at, data.Length - at), CompressionMode.Decompress), where + "[zlib@" + at + "]", secrets, hits, depth);
            }
        }
    }

    private static void Inflate(Func<Stream> open, string where, string[] secrets, List<string> hits, int depth)
    {
        try
        {
            using Stream s = open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Bytes(ms.ToArray(), where, secrets, hits, depth + 1);
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void CompoundFile(byte[] data, string where, string[] secrets, List<string> hits, int depth)
    {
        try
        {
            using var reader = new CompoundFileReader(data);
            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.Kind == RecursiveAI.OneArchive.ArchiveEntryKind.Directory)
                {
                    continue;
                }

                using var ms = new MemoryStream();
                using (Stream s = reader.OpenEntryStream())
                {
                    s.CopyTo(ms);
                }

                byte[] stream = ms.ToArray();
                string at = where + "[cfb:" + entry.Name + "]";
                Bytes(stream, at, secrets, hits, depth + 1);
                Text(Encoding.Unicode.GetString(stream), at, secrets, hits, depth + 6, "utf-16 stream");
                foreach (byte[] source in Ovba(stream))
                {
                    Bytes(source, at + "[ovba]", secrets, hits, depth + 1);
                    Text(Encoding.Latin1.GetString(source), at + "[ovba]", secrets, hits, depth + 6, "macro source");
                }
            }
        }
        catch (Exception)
        {
        }
    }

    // [MS-OVBA] 2.4.1.3.1: a compressed container is 0x01 and chunks; each chunk has a 16-bit header (size-3 in 12 bits, 0b011 signature, flag).
    private static IEnumerable<byte[]> Ovba(byte[] s)
    {
        for (int i = 0; i + 3 < s.Length; i++)
        {
            if (s[i] != 1)
            {
                continue;
            }

            int header = s[i + 1] | (s[i + 2] << 8);
            if (((header >> 12) & 7) != 3)
            {
                continue;
            }

            byte[]? result = Decompress(s, i);
            if (result is { Length: > 4 })
            {
                yield return result;
            }
        }
    }

    private static byte[]? Decompress(byte[] s, int start)
    {
        var output = new List<byte>();
        int p = start + 1;
        while (p + 2 <= s.Length)
        {
            int header = s[p] | (s[p + 1] << 8);
            if (((header >> 12) & 7) != 3)
            {
                break;
            }

            int size = (header & 0xFFF) + 3;
            bool compressed = (header & 0x8000) != 0;
            int end = Math.Min(p + size, s.Length);
            int chunkStart = output.Count;
            p += 2;
            if (!compressed)
            {
                for (int k = 0; k < 4096 && p < end; k++)
                {
                    output.Add(s[p++]);
                }

                continue;
            }

            while (p < end)
            {
                byte flags = s[p++];
                for (int bit = 0; bit < 8 && p < end; bit++)
                {
                    if ((flags & (1 << bit)) == 0)
                    {
                        output.Add(s[p++]);
                        continue;
                    }

                    if (p + 1 >= s.Length)
                    {
                        return output.ToArray();
                    }

                    int token = s[p] | (s[p + 1] << 8);
                    p += 2;
                    int pos = output.Count - chunkStart;
                    int bits = Math.Max(4, (int)Math.Ceiling(Math.Log2(Math.Max(pos, 1))));
                    int length = (token & (0xFFFF >> bits)) + 3;
                    int offset = (token >> (16 - bits)) + 1;
                    if (offset > pos)
                    {
                        return output.ToArray();
                    }

                    for (int k = 0; k < length; k++)
                    {
                        output.Add(output[^offset]);
                    }
                }
            }
        }

        return output.ToArray();
    }

    private static string? TryXml(byte[] data, string where, string[] secrets, List<string> hits, int depth)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, CheckCharacters = false };
            using var reader = XmlReader.Create(new MemoryStream(data), settings);
            var doc = new XmlDocument { XmlResolver = null };
            doc.Load(reader);
            Node(doc.DocumentElement!, where, secrets, hits, depth);
            return string.Empty;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static void Node(XmlNode node, string where, string[] secrets, List<string> hits, int depth)
    {
        switch (node.NodeType)
        {
            case XmlNodeType.Element:
                Text(node.Name, where, secrets, hits, depth, "element name");
                Text(node.NamespaceURI, where, secrets, hits, depth, "namespace");
                foreach (XmlAttribute a in node.Attributes!)
                {
                    Text(a.Name, where, secrets, hits, depth, "attribute name");
                    Text(a.Value, where, secrets, hits, depth, "attribute " + a.Name);
                }

                // The text of a paragraph-like element as a reader sees it, whatever its runs are split into.
                string local = node.LocalName;
                if (local is "p" or "si" or "is" or "text" or "t" or "comment")
                {
                    Text(node.InnerText, where, secrets, hits, depth, "text of " + local);
                }

                foreach (XmlNode child in node.ChildNodes)
                {
                    Node(child, where, secrets, hits, depth);
                }

                break;
            case XmlNodeType.Text:
            case XmlNodeType.CDATA:
            case XmlNodeType.Comment:
            case XmlNodeType.ProcessingInstruction:
                Text(node.Value ?? string.Empty, where, secrets, hits, depth, node.NodeType.ToString().ToLowerInvariant());
                break;
        }
    }
}
