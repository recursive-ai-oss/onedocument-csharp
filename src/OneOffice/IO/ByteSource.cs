using Microsoft.Win32.SafeHandles;

namespace RecursiveAI.OneOffice.IO;

/// <summary>Creates <see cref="IByteSource"/> values over files, streams, memory and remote ranges.</summary>
public static class ByteSource
{
    /// <summary>Creates a source over memory. Nothing is copied: the caller must not change the bytes while the source is in use.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The source.</returns>
    public static IByteSource FromMemory(ReadOnlyMemory<byte> bytes) => new MemoryByteSource(bytes);

    /// <summary>Creates a source over a file. The file is opened for reading, shared with other readers, and closed when the source is disposed. Concurrent reads are safe.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The source.</returns>
    /// <exception cref="IOException">The file cannot be opened (the usual <see cref="FileNotFoundException"/>, <see cref="UnauthorizedAccessException"/> and so on apply).</exception>
    public static IByteSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            return new FileByteSource(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Creates a source over a seekable, readable stream. Reads are serialised on the stream's position, so the caller must not use the stream meanwhile.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the source is disposed.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentException">The stream cannot seek or cannot read.</exception>
    public static IByteSource FromStream(Stream stream, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable and seekable. Copy a forward-only stream to memory first.", nameof(stream));
        }

        return new StreamByteSource(stream, leaveOpen);
    }

    /// <summary>Creates a source over a remote or ranged object. It can only be read asynchronously.</summary>
    /// <param name="length">The object's length in bytes.</param>
    /// <param name="readRange">Reads up to <c>destination.Length</c> bytes at <c>offset</c> and returns how many were read; it is called concurrently and must be safe for that.</param>
    /// <returns>The source; <see cref="IByteSource.CanReadSync"/> is false.</returns>
    public static IByteSource FromRanged(long length, Func<long, Memory<byte>, CancellationToken, ValueTask<int>> readRange)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(readRange);
        return new RangedByteSource(length, readRange);
    }

    /// <summary>Reads a whole source into memory, so that a reader that needs synchronous access can have it.</summary>
    /// <param name="source">The source.</param>
    /// <param name="maxLength">The most bytes to accept; a longer source raises <see cref="OfficeLimitException"/> before anything is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A memory source (the same object if <paramref name="source"/> already is one).</returns>
    /// <exception cref="OfficeLimitException">The source is longer than <paramref name="maxLength"/>.</exception>
    public static async ValueTask<IByteSource> ToMemoryAsync(IByteSource source, long maxLength, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.TryGetMemory(out _))
        {
            return source;
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), maxLength, source.Length);
        if (source.Length > Array.MaxLength)
        {
            throw new OfficeLimitException(nameof(OfficeLimits.MaxAllocationBytes), Array.MaxLength, source.Length);
        }

        byte[] buffer = new byte[(int)source.Length];
        int done = 0;
        while (done < buffer.Length)
        {
            int n = await source.ReadAsync(done, buffer.AsMemory(done), cancellationToken).ConfigureAwait(false);
            if (n <= 0)
            {
                throw new IOException("The source ended before its declared length.");
            }

            done += n;
        }

        return new MemoryByteSource(buffer);
    }

    /// <summary>
    /// Presents a source as a read-only seekable <see cref="Stream"/>, for the stream-based archive readers. Disposing the stream does
    /// not dispose the source. Reading the stream synchronously needs <see cref="IByteSource.CanReadSync"/>.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A new stream positioned at zero.</returns>
    public static Stream AsStream(IByteSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ByteSourceStream(source);
    }

    private sealed class MemoryByteSource(ReadOnlyMemory<byte> bytes) : IByteSource
    {
        private bool _disposed;

        public long Length => bytes.Length;

        public bool CanReadSync => true;

        public int Read(long offset, Span<byte> destination)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (offset >= bytes.Length)
            {
                return 0;
            }

            int n = (int)Math.Min(destination.Length, bytes.Length - offset);
            bytes.Span.Slice((int)offset, n).CopyTo(destination);
            return n;
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<int>(Read(offset, destination.Span));
        }

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            memory = bytes;
            return true;
        }

        public void Dispose() => _disposed = true;
    }

    private sealed class FileByteSource : IByteSource
    {
        private readonly SafeFileHandle _handle;

        public FileByteSource(SafeFileHandle handle)
        {
            _handle = handle;
            Length = RandomAccess.GetLength(handle);
        }

        public long Length { get; }

        public bool CanReadSync => true;

        public int Read(long offset, Span<byte> destination)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            int total = 0;
            while (total < destination.Length && offset + total < Length)
            {
                int n = RandomAccess.Read(_handle, destination[total..], offset + total);
                if (n <= 0)
                {
                    break;
                }

                total += n;
            }

            return total;
        }

        public async ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            int total = 0;
            while (total < destination.Length && offset + total < Length)
            {
                int n = await RandomAccess.ReadAsync(_handle, destination[total..], offset + total, cancellationToken).ConfigureAwait(false);
                if (n <= 0)
                {
                    break;
                }

                total += n;
            }

            return total;
        }

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory)
        {
            memory = default;
            return false;
        }

        public void Dispose() => _handle.Dispose();
    }

    private sealed class StreamByteSource(Stream stream, bool leaveOpen) : IByteSource
    {
        private readonly SemaphoreSlim _async = new(1, 1);
        private bool _disposed;

        public long Length => stream.Length;

        public bool CanReadSync => true;

        public int Read(long offset, Span<byte> destination)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Sync and async reads share one gate, because both move the stream's position.
            _async.Wait();
            try
            {
                stream.Position = offset;
                return stream.ReadAtLeast(destination, destination.Length, throwOnEndOfStream: false);
            }
            finally
            {
                _async.Release();
            }
        }

        public async ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _async.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                stream.Position = offset;
                return await stream.ReadAtLeastAsync(destination, destination.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _async.Release();
            }
        }

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory)
        {
            memory = default;
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _async.Dispose();
            if (!leaveOpen)
            {
                stream.Dispose();
            }
        }
    }

    private sealed class RangedByteSource(long length, Func<long, Memory<byte>, CancellationToken, ValueTask<int>> readRange) : IByteSource
    {
        private bool _disposed;

        public long Length => length;

        public bool CanReadSync => false;

        public int Read(long offset, Span<byte> destination) =>
            throw new NotSupportedException("A ranged source can only be read asynchronously; check CanReadSync, or call ByteSource.ToMemoryAsync first.");

        public async ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (offset >= length)
            {
                return 0;
            }

            Memory<byte> wanted = destination[..(int)Math.Min(destination.Length, length - offset)];
            int total = 0;
            while (total < wanted.Length)
            {
                int n = await readRange(offset + total, wanted[total..], cancellationToken).ConfigureAwait(false);
                if (n <= 0)
                {
                    break;
                }

                total += n;
            }

            return total;
        }

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory)
        {
            memory = default;
            return false;
        }

        public void Dispose() => _disposed = true;
    }
}
