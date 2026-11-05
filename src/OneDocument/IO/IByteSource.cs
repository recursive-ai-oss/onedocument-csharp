namespace RecursiveAI.OneDocument.IO;

/// <summary>
/// Input bytes, however they are held: a file, a stream, memory, or a remote object read by ranges. Every reader in OneDocument opens
/// from an <see cref="IByteSource"/>, so opening needs only the bytes it touches (a lazy open of a large zip reads the central
/// directory and nothing else) and a memory source hands its bytes out without copying.
/// </summary>
/// <remarks>
/// Reads are positioned (offset + buffer), not tied to a cursor, so one source may serve several readers at once; implementations
/// are safe for concurrent reads. A source never reads past <see cref="Length"/>. Sources are read-only. Use <see cref="ByteSource"/> to
/// create them. A source created over something the caller owns (a stream passed with <c>leaveOpen</c>) does not close it on dispose.
/// </remarks>
public interface IByteSource : IDisposable
{
    /// <summary>Gets the total length in bytes.</summary>
    long Length { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Read"/> may be called. False for remote sources, which can only be read with
    /// <see cref="ReadAsync"/>; such a source can be turned into a memory source with <see cref="ByteSource.ToMemoryAsync"/>.
    /// </summary>
    bool CanReadSync { get; }

    /// <summary>Reads bytes at an offset.</summary>
    /// <param name="offset">The offset of the first byte to read; at least zero.</param>
    /// <param name="destination">Where to put the bytes.</param>
    /// <returns>The number of bytes read: the length of <paramref name="destination"/>, or fewer only when the source ends first (zero at or beyond the end).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative.</exception>
    /// <exception cref="NotSupportedException"><see cref="CanReadSync"/> is false.</exception>
    /// <exception cref="ObjectDisposedException">The source is disposed.</exception>
    /// <exception cref="IOException">The underlying file or stream failed.</exception>
    int Read(long offset, Span<byte> destination);

    /// <summary>Reads bytes at an offset, asynchronously.</summary>
    /// <param name="offset">The offset of the first byte to read; at least zero.</param>
    /// <param name="destination">Where to put the bytes.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of bytes read, as for <see cref="Read"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative.</exception>
    /// <exception cref="ObjectDisposedException">The source is disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="IOException">The underlying file, stream or transport failed.</exception>
    ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default);

    /// <summary>Gets all the bytes as memory without copying, if the source holds them in memory.</summary>
    /// <param name="memory">The bytes, valid until the source is disposed.</param>
    /// <returns>True for a memory source; false otherwise, in which case use <see cref="Read"/> or <see cref="ReadAsync"/>.</returns>
    bool TryGetMemory(out ReadOnlyMemory<byte> memory);
}
