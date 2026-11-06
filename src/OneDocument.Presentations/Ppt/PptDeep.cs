using System.Buffers.Binary;
using RecursiveAI.OneDocument.OfficeArt;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>A presentation comment from a slide's PowerPoint 2002 extension data ([MS-PPT] §2.5.25).</summary>
/// <param name="Author">The author's name.</param>
/// <param name="Initials">The author's initials.</param>
/// <param name="Text">The comment text.</param>
/// <param name="Index">The comment's index (part of its label).</param>
/// <param name="Time">The creation time as written, without a time zone.</param>
/// <param name="X">The label's horizontal position in master units.</param>
/// <param name="Y">The label's vertical position in master units.</param>
public sealed record PptComment(string Author, string Initials, string Text, int Index, DateTime Time, int X, int Y);

/// <summary>Searches record trees, including the extension data blobs that PowerPoint stores as opaque record sequences.</summary>
public static class PptDeep
{
    /// <summary>
    /// Every record of the given type below <paramref name="root"/>. The binary tag data blob of the programmable tags holds records without a
    /// container header; its payload is parsed on the way. Depth and record count follow the limits in <paramref name="context"/>.
    /// </summary>
    /// <param name="root">The record to search below.</param>
    /// <param name="type">The record type to find.</param>
    /// <param name="context">Limits and defect sink.</param>
    public static IReadOnlyList<ArtRecord> FindAll(ArtRecord root, int type, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(context);
        var found = new List<ArtRecord>();
        Walk(root, type, context, found, 0);
        return found;
    }

    private static void Walk(ArtRecord record, int type, ArtReadContext context, List<ArtRecord> found, int depth)
    {
        if (depth > context.Limits.MaxRecursionDepth)
        {
            return;
        }

        if (record.Type == type)
        {
            found.Add(record);
        }

        IReadOnlyList<ArtRecord> children = record.Children;
        if (children.Count == 0 && record.Type == 0x138B && record.Payload.Length >= ArtRecordHeader.Size)
        {
            children = ArtRecords.Parse(record.Payload, record.Offset + ArtRecordHeader.Size, context);
        }

        foreach (ArtRecord child in children)
        {
            Walk(child, type, context, found, depth + 1);
        }
    }

    /// <summary>The comments of a slide, in file order.</summary>
    /// <param name="slideContainer">The slide container.</param>
    /// <param name="context">Limits and defect sink.</param>
    public static IReadOnlyList<PptComment> Comments(ArtRecord slideContainer, ArtReadContext context)
    {
        var list = new List<PptComment>();
        foreach (ArtRecord c in FindAll(slideContainer, PptRecordType.Comment10, context))
        {
            string author = "";
            string initials = "";
            string text = "";
            int index = 0;
            DateTime time = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            int x = 0;
            int y = 0;
            foreach (ArtRecord child in c.Children)
            {
                switch (child.Type)
                {
                    case PptRecordType.CString when child.Header.Instance == 0:
                        author = Utf16(child.Payload.Span);
                        break;
                    case PptRecordType.CString when child.Header.Instance == 1:
                        text = Utf16(child.Payload.Span);
                        break;
                    case PptRecordType.CString when child.Header.Instance == 2:
                        initials = Utf16(child.Payload.Span);
                        break;
                    case PptRecordType.Comment10Atom when child.Payload.Length >= 28:
                        ReadOnlySpan<byte> p = child.Payload.Span;
                        index = BinaryPrimitives.ReadInt32LittleEndian(p);
                        // DateTimeStruct: year, month, day of week, day, hour, minute, second, milliseconds as 16-bit values.
                        int year = BinaryPrimitives.ReadUInt16LittleEndian(p[4..]);
                        int month = BinaryPrimitives.ReadUInt16LittleEndian(p[6..]);
                        int day = BinaryPrimitives.ReadUInt16LittleEndian(p[10..]);
                        int hour = BinaryPrimitives.ReadUInt16LittleEndian(p[12..]);
                        int minute = BinaryPrimitives.ReadUInt16LittleEndian(p[14..]);
                        int second = BinaryPrimitives.ReadUInt16LittleEndian(p[16..]);
                        if (year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month) && hour < 24 && minute < 60 && second < 60)
                        {
                            time = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
                        }

                        x = BinaryPrimitives.ReadInt32LittleEndian(p[20..]);
                        y = BinaryPrimitives.ReadInt32LittleEndian(p[24..]);
                        break;
                }
            }

            list.Add(new PptComment(author, initials, text, index, time, x, y));
        }

        return list;
    }

    private static string Utf16(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length / 2];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
        }

        int end = Array.IndexOf(chars, '\0');
        return new string(chars, 0, end < 0 ? chars.Length : end);
    }
}
