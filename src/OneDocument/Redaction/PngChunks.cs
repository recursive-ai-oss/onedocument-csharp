using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace RecursiveAI.OneDocument.Redaction;

// PNG text chunks. tEXt holds Latin-1 text, zTXt and (optionally) iTXt hold deflated text, so a search of the file's bytes cannot see inside
// them. A chunk whose text has a match is dropped whole: the other chunks and their checksums stay valid and the picture is unchanged.
// PNG Specification (W3C, third edition) §11.3.3 Textual information: tEXt, zTXt and iTXt.
internal static class PngChunks
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const int MaxText = 1 << 22;

    public static bool IsPng(ReadOnlySpan<byte> b) => b.Length >= 12 && b[..8].SequenceEqual(Signature);

    public static bool RemoveMatching(byte[] png, RedactionContext ctx, out byte[]? result, out int removed)
    {
        result = null;
        removed = 0;
        if (!IsPng(png))
        {
            return false;
        }

        var drop = new List<(int Start, int Length)>();
        var spans = new List<(int Start, int Length)>();
        foreach ((int start, int length, string type) in Chunks(png))
        {
            if (type is "tEXt" or "zTXt" or "iTXt" && Decode(type, png.AsSpan(start + 8, length)) is { } text && ctx.FindSpans(text, "png", spans))
            {
                drop.Add((start, length + 12));
            }
        }

        if (drop.Count == 0)
        {
            return false;
        }

        using var ms = new MemoryStream(png.Length);
        int at = 0;
        foreach ((int start, int length) in drop)
        {
            ms.Write(png, at, start - at);
            at = start + length;
        }

        ms.Write(png, at, png.Length - at);
        result = ms.ToArray();
        removed = drop.Count;
        return true;
    }

    // (offset of the length field, data length, type) for each chunk that lies wholly inside the file.
    private static IEnumerable<(int Start, int Length, string Type)> Chunks(byte[] png)
    {
        int p = 8;
        while (p + 12 <= png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(p));
            if (length > int.MaxValue || p + 12L + length > png.Length)
            {
                yield break;
            }

            yield return (p, (int)length, Encoding.ASCII.GetString(png, p + 4, 4));
            p += 12 + (int)length;
        }
    }

    private static string? Decode(string type, ReadOnlySpan<byte> data)
    {
        int nul = data.IndexOf((byte)0);
        if (nul < 0)
        {
            return null;
        }

        string keyword = Encoding.Latin1.GetString(data[..nul]);
        ReadOnlySpan<byte> rest = data[(nul + 1)..];
        switch (type)
        {
            case "tEXt":
                return keyword + " " + Encoding.Latin1.GetString(rest);
            case "zTXt":
                return rest.Length > 1 ? keyword + " " + Inflate(rest[1..]) : null;
            default:
                // iTXt: compression flag, method, language tag, translated keyword, text.
                if (rest.Length < 2)
                {
                    return null;
                }

                bool compressed = rest[0] == 1;
                ReadOnlySpan<byte> after = rest[2..];
                int lang = after.IndexOf((byte)0);
                if (lang < 0)
                {
                    return null;
                }

                after = after[(lang + 1)..];
                int translated = after.IndexOf((byte)0);
                if (translated < 0)
                {
                    return null;
                }

                ReadOnlySpan<byte> body = after[(translated + 1)..];
                return keyword + " " + Encoding.UTF8.GetString(after[..translated]) + " " + (compressed ? Inflate(body) : Encoding.UTF8.GetString(body));
        }
    }

    private static string Inflate(ReadOnlySpan<byte> deflated)
    {
        try
        {
            using var input = new MemoryStream(deflated.ToArray());
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int n;
            while ((n = z.Read(buffer, 0, buffer.Length)) > 0 && output.Length < MaxText)
            {
                output.Write(buffer, 0, n);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (InvalidDataException)
        {
            return string.Empty;
        }
    }
}
