using System.IO.Compression;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>Small valid images made from a description, so fixtures need no binary files.</summary>
internal static class TestImages
{
    /// <summary>A solid-colour PNG of the given size.</summary>
    public static byte[] Png(int width, int height, byte r, byte g, byte b)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, width);
        WriteBe(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk(ms, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                for (int x = 0; x < width; x++)
                {
                    z.WriteByte(r);
                    z.WriteByte(g);
                    z.WriteByte(b);
                }
            }
        }

        Chunk(ms, "IDAT", raw.ToArray());
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteBe(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 24);
        b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8);
        b[at + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, data.Length);
        s.Write(len);
        byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte x in t.Concat(data))
        {
            crc ^= x;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        crc ^= 0xFFFFFFFF;
        var c = new byte[4];
        WriteBe(c, 0, (int)crc);
        s.Write(c);
    }
}
