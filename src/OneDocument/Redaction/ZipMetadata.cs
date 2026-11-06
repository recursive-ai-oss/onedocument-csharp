using System.Buffers.Binary;

namespace RecursiveAI.OneDocument.Redaction;

// The parts of a zip file that are not entry content: the archive comment, per-entry comments, extra fields and local headers. Office never
// writes text there, but other tools do (comments, a second copy of the file name in an Info-ZIP extra field), and untouched entries are
// carried over with their metadata, so a secret can sit in the container outside every part.
// [APPNOTE 6.3.x] 4.3.7 local file header, 4.3.12 central directory header, 4.3.16 end of central directory record.
internal static class ZipMetadata
{
    private const uint EndOfCentralDirectory = 0x06054B50;
    private const uint CentralHeader = 0x02014B50;
    private const uint LocalHeader = 0x04034B50;
    private const ushort UnicodePathExtra = 0x7075;
    private const ushort UnicodeCommentExtra = 0x6375;
    private const ushort UnusedExtra = 0xF0F0;

    public readonly record struct Region(int Start, int Length, string What);

    // Every region of the file that holds header text: the local header (name and extra field), the central header (name, extra field,
    // comment) and the archive comment. Entry content is not included. Empty when the file is not a plain zip.
    public static List<Region> Regions(byte[] zip)
    {
        var regions = new List<Region>();
        if (!TryFindDirectory(zip, out int eocd, out int count, out int directoryStart))
        {
            return regions;
        }

        int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(eocd + 20));
        if (commentLength > 0 && eocd + 22 + commentLength <= zip.Length)
        {
            regions.Add(new Region(eocd + 22, commentLength, "archive comment"));
        }

        int p = directoryStart;
        for (int i = 0; i < count && p + 46 <= zip.Length && BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(p)) == CentralHeader; i++)
        {
            int n = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(p + 28));
            int m = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(p + 30));
            int k = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(p + 32));
            uint local = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(p + 42));
            if (p + 46 + n + m + k > zip.Length)
            {
                break;
            }

            regions.Add(new Region(p + 46, n + m + k, "central directory entry"));
            if (local != uint.MaxValue && local + 30L <= zip.Length && BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan((int)local)) == LocalHeader)
            {
                int ln = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan((int)local + 26));
                int lm = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan((int)local + 28));
                if (local + 30L + ln + lm <= zip.Length)
                {
                    regions.Add(new Region((int)local + 30, ln + lm, "local header"));
                }
            }

            p += 46 + n + m + k;
        }

        return regions;
    }

    // Blanks the comments and the extra fields that carry text (Info-ZIP's second copy of the name or comment) in place; names stay, they are the
    // part names. Returns the number of comments blanked. A comment becomes spaces, so no offset or length in the file changes.
    public static int Clear(byte[] zip)
    {
        if (!TryFindDirectory(zip, out int eocd, out int count, out int directoryStart))
        {
            return 0;
        }

        int cleared = 0;
        int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(eocd + 20));
        if (commentLength > 0 && eocd + 22 + commentLength <= zip.Length && Blank(zip.AsSpan(eocd + 22, commentLength)))
        {
            cleared++;
        }

        int p = directoryStart;
        for (int i = 0; i < count && p + 46 <= zip.Length && BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(p)) == CentralHeader; i++)
        {
            int n = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(p + 28));
            int m = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(p + 30));
            int k = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(p + 32));
            uint local = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(p + 42));
            if (p + 46 + n + m + k > zip.Length)
            {
                break;
            }

            ClearExtra(zip.AsSpan(p + 46 + n, m));
            if (k > 0 && Blank(zip.AsSpan(p + 46 + n + m, k)))
            {
                cleared++;
            }

            if (local != uint.MaxValue && local + 30L <= zip.Length && BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan((int)local)) == LocalHeader)
            {
                int ln = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan((int)local + 26));
                int lm = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan((int)local + 28));
                if (local + 30L + ln + lm <= zip.Length)
                {
                    ClearExtra(zip.AsSpan((int)local + 30 + ln, lm));
                }
            }

            p += 46 + n + m + k;
        }

        return cleared;
    }

    private static bool Blank(Span<byte> comment)
    {
        bool any = false;
        foreach (byte b in comment)
        {
            if (b != 0x20)
            {
                any = true;
                break;
            }
        }

        comment.Fill(0x20);
        return any;
    }

    private static void ClearExtra(Span<byte> extra)
    {
        int at = 0;
        while (at + 4 <= extra.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[at..]);
            int size = BinaryPrimitives.ReadUInt16LittleEndian(extra[(at + 2)..]);
            if (at + 4 + size > extra.Length)
            {
                break;
            }

            if (id is UnicodePathExtra or UnicodeCommentExtra)
            {
                // Info-ZIP keeps a second copy of the file name or comment here. The field stays the same length under an id nobody uses, so readers skip it.
                extra.Slice(at + 4, size).Clear();
                BinaryPrimitives.WriteUInt16LittleEndian(extra[at..], UnusedExtra);
            }

            at += 4 + size;
        }
    }

    private static bool TryFindDirectory(byte[] zip, out int eocd, out int count, out int directoryStart)
    {
        eocd = -1;
        count = 0;
        directoryStart = 0;
        int earliest = Math.Max(0, zip.Length - 22 - 65535);
        for (int i = zip.Length - 22; i >= earliest; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i)) == EndOfCentralDirectory
                && i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(i + 20)) == zip.Length)
            {
                eocd = i;
                break;
            }
        }

        if (eocd < 0)
        {
            return false;
        }

        count = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(eocd + 10));
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd + 16));
        if (count == 0xFFFF || start == uint.MaxValue || start >= zip.Length)
        {
            return false; // Zip64 archives are not interpreted here
        }

        directoryStart = (int)start;
        return true;
    }
}
