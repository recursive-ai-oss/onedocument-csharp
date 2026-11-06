using System.Buffers.Binary;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// A minimal reader for the Office drawing stream ([MS-ODRAW] §2.1.1 OfficeArtRecordHeader): a 2-byte version and instance field,
/// a 2-byte type and a 4-byte length. Containers (version 0xF) hold more records. Only what the picture conversion needs is exposed;
/// the bytes of the stream stay untouched.
/// </summary>
internal readonly record struct EscherRecord(int Version, int Instance, int Type, int Offset, int DataOffset, int Length)
{
    public bool IsContainer => Version == 0xF;

    public int End => DataOffset + Length;
}

internal static class EscherReader
{
    public const int DggContainer = 0xF000;
    public const int BStoreContainer = 0xF001;
    public const int DgContainer = 0xF002;
    public const int SpgrContainer = 0xF003;
    public const int SpContainer = 0xF004;
    public const int Bse = 0xF007;
    public const int Opt = 0xF00B;
    public const int ClientAnchor = 0xF010;

    /// <summary>Reads the records that sit directly in <c>[start, end)</c>; a record that runs past <paramref name="end"/> is clipped to it.</summary>
    public static List<EscherRecord> Children(ReadOnlySpan<byte> data, int start, int end)
    {
        var list = new List<EscherRecord>();
        int p = start;
        end = Math.Min(end, data.Length);
        while (p + 8 <= end)
        {
            int verInst = data[p] | (data[p + 1] << 8);
            int type = data[p + 2] | (data[p + 3] << 8);
            long len = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p + 4, 4));
            int dataOffset = p + 8;
            int length = (int)Math.Min(len, end - dataOffset);
            if (type < 0xF000)
            {
                break;
            }

            list.Add(new EscherRecord(verInst & 0xF, verInst >> 4, type, p, dataOffset, length));
            p = dataOffset + length;
            if (len > end - dataOffset)
            {
                break;
            }
        }

        return list;
    }
}
