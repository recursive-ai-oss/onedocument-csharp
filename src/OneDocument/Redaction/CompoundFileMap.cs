using System.Linq;
using System.Buffers.Binary;
using System.Text;

namespace RecursiveAI.OneDocument.Redaction;

// Where the bytes of each stream of a compound file lie in the file. A stream's sectors are chained through the allocation table and need not be
// neighbours, so a string that crosses a sector boundary is not a run of bytes in the file and a search of the raw file misses it. Reading
// and writing through the extents treats the stream as the reader does and leaves every table, name and unused sector where it was.
// [MS-CFB] §2.2 header, §2.3 FAT and DIFAT chains, §2.4 mini stream, §2.6 directory entries.
internal sealed class CompoundFileMap
{
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint MaxRegularSector = 0xFFFFFFFA;
    private const int MaxEntries = 100_000;

    public readonly record struct Extent(int Offset, int Length);

    public sealed record StreamMap(string Path, long Size, IReadOnlyList<Extent> Extents)
    {
        public byte[] Read(byte[] file)
        {
            var data = new byte[Size];
            int at = 0;
            foreach (Extent e in Extents)
            {
                Buffer.BlockCopy(file, e.Offset, data, at, e.Length);
                at += e.Length;
            }

            return data;
        }

        public void Write(byte[] file, byte[] data)
        {
            int at = 0;
            foreach (Extent e in Extents)
            {
                Buffer.BlockCopy(data, at, file, e.Offset, e.Length);
                at += e.Length;
            }
        }
    }

    private readonly record struct DirectoryEntry(string Name, byte Type, uint Left, uint Right, uint Child, uint Start, long Size);

    public static bool IsCompoundFile(ReadOnlySpan<byte> b) =>
        b.Length >= 512 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0 && b[4] == 0xA1 && b[5] == 0xB1 && b[6] == 0x1A && b[7] == 0xE1;

    // False when the file is not a well-formed compound file; nothing is guessed then.
    public static bool TryRead(byte[] file, out List<StreamMap> streams)
    {
        streams = [];
        if (!IsCompoundFile(file))
        {
            return false;
        }

        int sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(30));
        int miniShift = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(32));
        if (sectorShift is not (9 or 12) || miniShift != 6)
        {
            return false;
        }

        int sectorSize = 1 << sectorShift;
        long sectorCount = (file.LongLength / sectorSize) - 1;
        uint fatCount = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(44));
        uint firstDirectory = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(48));
        uint cutoff = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(56));
        uint firstMiniFat = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(60));
        uint firstDifat = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(68));
        uint difatCount = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(72));
        if (fatCount > sectorCount || difatCount > sectorCount)
        {
            return false;
        }

        bool Inside(uint sector) => sector <= MaxRegularSector && sector < sectorCount;
        int At(uint sector) => (int)((sector + 1L) * sectorSize);

        // The sectors that hold the allocation table: 109 in the header, the rest through the DIFAT chain.
        var fatSectors = new List<uint>();
        for (int i = 0; i < 109 && fatSectors.Count < fatCount; i++)
        {
            uint s = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(76 + (i * 4)));
            if (!Inside(s))
            {
                break;
            }

            fatSectors.Add(s);
        }

        uint difat = firstDifat;
        for (uint d = 0; d < difatCount && Inside(difat) && fatSectors.Count < fatCount; d++)
        {
            int baseAt = At(difat);
            for (int i = 0; i < (sectorSize / 4) - 1 && fatSectors.Count < fatCount; i++)
            {
                uint s = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(baseAt + (i * 4)));
                if (Inside(s))
                {
                    fatSectors.Add(s);
                }
            }

            difat = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(baseAt + sectorSize - 4));
        }

        var fat = new uint[fatSectors.Count * (sectorSize / 4)];
        for (int f = 0; f < fatSectors.Count; f++)
        {
            for (int i = 0; i < sectorSize / 4; i++)
            {
                fat[(f * (sectorSize / 4)) + i] = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(At(fatSectors[f]) + (i * 4)));
            }
        }

        static List<uint>? Chain(uint start, uint[] table, long limit)
        {
            var chain = new List<uint>();
            uint s = start;
            while (s != EndOfChain)
            {
                if (s > MaxRegularSector || s >= table.Length || chain.Count > limit)
                {
                    return null; // free, reserved, out of range or a cycle
                }

                chain.Add(s);
                s = table[s];
            }

            return chain;
        }

        List<uint>? directoryChain = Chain(firstDirectory, fat, sectorCount);
        if (directoryChain is null || directoryChain.Exists(s => !Inside(s)))
        {
            return false;
        }

        var entries = new List<DirectoryEntry>();
        foreach (uint s in directoryChain)
        {
            for (int e = 0; e < sectorSize / 128; e++)
            {
                int at = At(s) + (e * 128);
                int nameBytes = Math.Min((int)BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 64)), 64);
                string name = nameBytes >= 2 ? Encoding.Unicode.GetString(file, at, nameBytes - 2) : string.Empty;
                long size = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 120));
                if (sectorSize == 4096)
                {
                    size |= (long)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 124)) << 32;
                }

                entries.Add(new DirectoryEntry(name, file[at + 66], BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 68)), BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 72)),
                    BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 76)), BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 116)), size));
                if (entries.Count > MaxEntries)
                {
                    return false;
                }
            }
        }

        if (entries.Count == 0 || entries[0].Type != 5)
        {
            return false;
        }

        // The mini stream is the root entry's stream; the mini FAT chains 64-byte pieces of it.
        List<uint>? miniStreamChain = entries[0].Size > 0 ? Chain(entries[0].Start, fat, sectorCount) : null;
        var miniFat = new List<uint>();
        if (firstMiniFat != EndOfChain && Chain(firstMiniFat, fat, sectorCount) is { } miniFatChain)
        {
            foreach (uint s in miniFatChain.Where(Inside))
            {
                for (int i = 0; i < sectorSize / 4; i++)
                {
                    miniFat.Add(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(At(s) + (i * 4))));
                }
            }
        }

        uint[] miniTable = [.. miniFat];
        var found = new List<StreamMap>();

        void Add(string path, DirectoryEntry entry)
        {
            if (entry.Type != 2 || entry.Size <= 0 || entry.Size > file.Length)
            {
                return;
            }

            var extents = new List<Extent>();
            long remaining = entry.Size;
            if (entry.Size < cutoff)
            {
                if (miniStreamChain is null || Chain(entry.Start, miniTable, miniTable.Length) is not { } chain)
                {
                    return;
                }

                foreach (uint m in chain)
                {
                    long offset = m * 64L;
                    long sector = offset / sectorSize;
                    if (sector >= miniStreamChain.Count || !Inside(miniStreamChain[(int)sector]))
                    {
                        return;
                    }

                    int length = (int)Math.Min(64, remaining);
                    extents.Add(new Extent(At(miniStreamChain[(int)sector]) + (int)(offset % sectorSize), length));
                    remaining -= length;
                    if (remaining <= 0)
                    {
                        break;
                    }
                }
            }
            else
            {
                if (Chain(entry.Start, fat, sectorCount) is not { } chain)
                {
                    return;
                }

                foreach (uint s in chain)
                {
                    if (!Inside(s))
                    {
                        return;
                    }

                    int length = (int)Math.Min(sectorSize, remaining);
                    extents.Add(new Extent(At(s), length));
                    remaining -= length;
                    if (remaining <= 0)
                    {
                        break;
                    }
                }
            }

            if (remaining <= 0)
            {
                found.Add(new StreamMap(path, entry.Size, extents));
            }
        }

        // Walk the directory tree: each storage's children form a binary tree through left and right.
        var visited = new HashSet<uint>();
        void Walk(uint index, string parent)
        {
            if (index >= entries.Count || !visited.Add(index))
            {
                return;
            }

            DirectoryEntry entry = entries[(int)index];
            Walk(entry.Left, parent);
            Walk(entry.Right, parent);
            string path = parent.Length == 0 ? entry.Name : parent + "/" + entry.Name;
            if (entry.Type == 2)
            {
                Add(path, entry);
            }
            else if (entry.Type is 1 or 5)
            {
                Walk(entry.Child, path);
            }
        }

        Walk(entries[0].Child, string.Empty);
        streams = found;
        return true;
    }
}
