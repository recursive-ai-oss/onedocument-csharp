namespace RecursiveAI.OneDocument.Packaging;

/// <summary>A read-only, seekable stream over memory that is not copied.</summary>
internal sealed class MemoryReadStream(ReadOnlyMemory<byte> memory) : Stream
{
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => memory.Length;

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = (int)Math.Min(value, memory.Length);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int n = Math.Min(buffer.Length, memory.Length - _position);
        memory.Span.Slice(_position, n).CopyTo(buffer);
        _position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => memory.Length + offset,
        };
        if (target < 0)
        {
            throw new IOException("An attempt was made to move before the beginning of the stream.");
        }

        _position = (int)Math.Min(target, memory.Length);
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Passes reads to a stream that belongs to a shared container reader, one read at a time under the container's lock, so several
/// threads (or several open parts on one thread) never use the reader at once.
/// </summary>
internal sealed class LockedStream(object gate, Stream inner) : Stream
{
    public override bool CanRead => true;

    // A part stream over a container can seek when the container's own stream can (a compound file's streams do, a compressed zip entry
    // does not); the position moves under the same lock as reads.
    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            lock (gate)
            {
                return inner.Length;
            }
        }
    }

    public override long Position
    {
        get
        {
            lock (gate)
            {
                return inner.Position;
            }
        }

        set
        {
            lock (gate)
            {
                inner.Position = value;
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        lock (gate)
        {
            return inner.Read(buffer);
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (gate)
        {
            return inner.Seek(offset, origin);
        }
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (gate)
            {
                inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
