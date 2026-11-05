using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RecursiveAI.OneCompress.Checksums;
using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>
/// A zip writer for output that is produced in order and never held: each entry is deflated as it is written and its sizes and CRC-32
/// follow it in a data descriptor, so the output stream need not seek and no entry is buffered. Times are fixed so equal input gives
/// equal bytes.
/// </summary>
/// <remarks>PKWARE APPNOTE 4.3.9 (data descriptor), 4.4.4 (flag bits 3 and 11), 4.5.3 (zip64 extra field), 4.3.14 to 4.3.16 (zip64 end of central directory).</remarks>
internal sealed class StreamZipWriter : IDisposable
{
    private const uint LocalSignature = 0x04034b50;
    private const uint DescriptorSignature = 0x08074b50;
    private const uint CentralSignature = 0x02014b50;
    private const uint EndSignature = 0x06054b50;
    private const uint Zip64EndSignature = 0x06064b50;
    private const uint Zip64LocatorSignature = 0x07064b50;
    private const ushort FlagDescriptor = 0x0008;
    private const ushort FlagUtf8 = 0x0800;
    private const ushort DosDate = 0x0021; // 1980-01-01
    private const ushort DosTime = 0;

    private sealed record CentralEntry(byte[] Name, bool Deflated, uint Crc, long CompressedSize, long Size, long Offset, bool LocalZip64);

    private readonly CountingStream _out;
    private readonly bool _zip64;
    private readonly CompressionLevel _level;
    private readonly List<CentralEntry> _entries = [];
    private EntryStream? _open;
    private bool _finished;

    public StreamZipWriter(Stream output, bool zip64, CompressionLevel level)
    {
        _out = new CountingStream(output);
        _zip64 = zip64;
        _level = level;
    }

    /// <summary>Starts an entry and returns the stream its content is written to. The previous entry must have been closed.</summary>
    public Stream BeginEntry(string name, bool deflate = true)
    {
        if (_open is not null)
        {
            throw new InvalidOperationException("The previous entry is still open.");
        }

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        long offset = _out.Position;
        Span<byte> h = stackalloc byte[30]; // fixed part of a local file header
        BinaryPrimitives.WriteUInt32LittleEndian(h, LocalSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], (ushort)(_zip64 ? 45 : 20));
        BinaryPrimitives.WriteUInt16LittleEndian(h[6..], FlagDescriptor | FlagUtf8);
        BinaryPrimitives.WriteUInt16LittleEndian(h[8..], deflate ? (ushort)8 : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(h[10..], DosTime);
        BinaryPrimitives.WriteUInt16LittleEndian(h[12..], DosDate);
        BinaryPrimitives.WriteUInt32LittleEndian(h[14..], 0); // crc and sizes follow in the descriptor
        BinaryPrimitives.WriteUInt32LittleEndian(h[18..], _zip64 ? 0xFFFFFFFF : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(h[22..], _zip64 ? 0xFFFFFFFF : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(h[26..], (ushort)nameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(h[28..], (ushort)(_zip64 ? 20 : 0));
        _out.Write(h);
        _out.Write(nameBytes);
        if (_zip64)
        {
            Span<byte> x = stackalloc byte[20];
            BinaryPrimitives.WriteUInt16LittleEndian(x, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(x[2..], 16);
            x[4..].Clear(); // sizes are in the descriptor
            _out.Write(x);
        }

        _open = new EntryStream(this, nameBytes, deflate, offset);
        return _open;
    }

    public void WriteEntry(string name, ReadOnlySpan<byte> content)
    {
        using Stream s = BeginEntry(name);
        s.Write(content);
    }

    private void CloseEntry(EntryStream e)
    {
        long csize = e.CompressedLength;
        long size = e.Size;
        uint crc = e.Crc;
        bool needsZip64 = csize >= 0xFFFFFFFFL || size >= 0xFFFFFFFFL;
        if (needsZip64 && !_zip64)
        {
            throw new OfficeUnsupportedException("a part of 4 GiB or more without the Zip64 option");
        }

        Span<byte> d = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(d, DescriptorSignature);
        BinaryPrimitives.WriteUInt32LittleEndian(d[4..], crc);
        if (_zip64)
        {
            BinaryPrimitives.WriteInt64LittleEndian(d[8..], csize);
            BinaryPrimitives.WriteInt64LittleEndian(d[16..], size);
            _out.Write(d);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(d[8..], (uint)csize);
            BinaryPrimitives.WriteUInt32LittleEndian(d[12..], (uint)size);
            _out.Write(d[..16]);
        }

        _entries.Add(new CentralEntry(e.Name, e.Deflated, crc, csize, size, e.Offset, _zip64));
        _open = null;
    }

    /// <summary>Writes the central directory. The last entry must be closed.</summary>
    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        if (_open is not null)
        {
            throw new InvalidOperationException("An entry is still open.");
        }

        _finished = true;
        long cdStart = _out.Position;
        Span<byte> r = stackalloc byte[46];
        Span<byte> x64 = stackalloc byte[28];
        foreach (CentralEntry e in _entries)
        {
            bool big = e.CompressedSize >= 0xFFFFFFFFL || e.Size >= 0xFFFFFFFFL || e.Offset >= 0xFFFFFFFFL;
            int extra = big ? 28 : 0;
            r.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(r, CentralSignature);
            BinaryPrimitives.WriteUInt16LittleEndian(r[4..], (ushort)(big || e.LocalZip64 ? 45 : 20)); // made by
            BinaryPrimitives.WriteUInt16LittleEndian(r[6..], (ushort)(big || e.LocalZip64 ? 45 : 20));
            BinaryPrimitives.WriteUInt16LittleEndian(r[8..], FlagDescriptor | FlagUtf8);
            BinaryPrimitives.WriteUInt16LittleEndian(r[10..], e.Deflated ? (ushort)8 : (ushort)0);
            BinaryPrimitives.WriteUInt16LittleEndian(r[12..], DosTime);
            BinaryPrimitives.WriteUInt16LittleEndian(r[14..], DosDate);
            BinaryPrimitives.WriteUInt32LittleEndian(r[16..], e.Crc);
            BinaryPrimitives.WriteUInt32LittleEndian(r[20..], big ? 0xFFFFFFFF : (uint)e.CompressedSize);
            BinaryPrimitives.WriteUInt32LittleEndian(r[24..], big ? 0xFFFFFFFF : (uint)e.Size);
            BinaryPrimitives.WriteUInt16LittleEndian(r[28..], (ushort)e.Name.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(r[30..], (ushort)extra);
            BinaryPrimitives.WriteUInt32LittleEndian(r[42..], big ? 0xFFFFFFFF : (uint)e.Offset);
            _out.Write(r);
            _out.Write(e.Name);
            if (big)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(x64, 1);
                BinaryPrimitives.WriteUInt16LittleEndian(x64[2..], 24);
                BinaryPrimitives.WriteInt64LittleEndian(x64[4..], e.Size);
                BinaryPrimitives.WriteInt64LittleEndian(x64[12..], e.CompressedSize);
                BinaryPrimitives.WriteInt64LittleEndian(x64[20..], e.Offset);
                _out.Write(x64);
            }
        }

        long cdSize = _out.Position - cdStart;
        bool zip64End = _entries.Count >= 0xFFFF || cdStart >= 0xFFFFFFFFL || cdSize >= 0xFFFFFFFFL;
        if (zip64End)
        {
            long end64 = _out.Position;
            Span<byte> z = stackalloc byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(z, Zip64EndSignature);
            BinaryPrimitives.WriteInt64LittleEndian(z[4..], 44);
            BinaryPrimitives.WriteUInt16LittleEndian(z[12..], 45);
            BinaryPrimitives.WriteUInt16LittleEndian(z[14..], 45);
            BinaryPrimitives.WriteInt64LittleEndian(z[24..], _entries.Count);
            BinaryPrimitives.WriteInt64LittleEndian(z[32..], _entries.Count);
            BinaryPrimitives.WriteInt64LittleEndian(z[40..], cdSize);
            BinaryPrimitives.WriteInt64LittleEndian(z[48..], cdStart);
            _out.Write(z);
            Span<byte> l = stackalloc byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(l, Zip64LocatorSignature);
            BinaryPrimitives.WriteInt64LittleEndian(l[8..], end64);
            BinaryPrimitives.WriteUInt32LittleEndian(l[16..], 1);
            _out.Write(l);
        }

        Span<byte> t = stackalloc byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(t, EndSignature);
        ushort count = _entries.Count >= 0xFFFF ? (ushort)0xFFFF : (ushort)_entries.Count;
        BinaryPrimitives.WriteUInt16LittleEndian(t[8..], count);
        BinaryPrimitives.WriteUInt16LittleEndian(t[10..], count);
        BinaryPrimitives.WriteUInt32LittleEndian(t[12..], cdSize >= 0xFFFFFFFFL ? 0xFFFFFFFF : (uint)cdSize);
        BinaryPrimitives.WriteUInt32LittleEndian(t[16..], cdStart >= 0xFFFFFFFFL ? 0xFFFFFFFF : (uint)cdStart);
        _out.Write(t);
        _out.Flush();
    }

    public void Dispose()
    {
        // The caller owns the output stream.
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Position0;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => Position0;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Position0 += buffer.Length;
        }
    }

    private sealed class EntryStream : Stream
    {
        private readonly StreamZipWriter _owner;
        private readonly long _startPosition;
        private readonly DeflateStream? _deflate;
        private Crc32 _crc;
        private bool _closed;

        public EntryStream(StreamZipWriter owner, byte[] name, bool deflate, long offset)
        {
            _owner = owner;
            Name = name;
            Deflated = deflate;
            Offset = offset;
            _startPosition = owner._out.Position;
            _deflate = deflate ? new DeflateStream(owner._out, owner._level, leaveOpen: true) : null;
        }

        public byte[] Name { get; }

        public bool Deflated { get; }

        public long Offset { get; }

        public uint Crc => _crc.GetCurrentValue();

        public long CompressedLength { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Size;

        public override long Position
        {
            get => Size;
            set => throw new NotSupportedException();
        }

        public long Size { get; private set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _crc.Append(buffer);
            Size += buffer.Length;
            if (_deflate is not null)
            {
                _deflate.Write(buffer);
            }
            else
            {
                _owner._out.Write(buffer);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                _deflate?.Dispose();
                CompressedLength = _owner._out.Position - _startPosition;
                _owner.CloseEntry(this);
            }

            base.Dispose(disposing);
        }
    }
}
