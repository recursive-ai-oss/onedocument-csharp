namespace RecursiveAI.OneOffice.IO;

/// <summary>
/// Writes a file so that it is either the old content or the complete new content, never half of one. The new content goes to a
/// temporary file in the same directory, is flushed to disk, and then replaces the target in one rename. If the writer throws, the
/// process dies or the disk fills, the original is untouched and the temporary file is removed (or, after a crash, left behind
/// under a name starting with a dot and ending in <c>.tmp</c>).
/// </summary>
public static class AtomicFile
{
    private static int s_counter;

    /// <summary>Replaces a file with what <paramref name="write"/> produces.</summary>
    /// <param name="path">The target path. Its directory must exist.</param>
    /// <param name="write">Writes the new content to the stream it is given.</param>
    /// <exception cref="IOException">The file cannot be written or replaced.</exception>
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);
        (FileStream stream, string temp) = CreateTemp(path, FileOptions.None);
        bool committed = false;
        try
        {
            using (stream)
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                TryDelete(temp);
            }
        }
    }

    /// <summary>Replaces a file with what <paramref name="write"/> produces, asynchronously.</summary>
    /// <param name="path">The target path. Its directory must exist.</param>
    /// <param name="write">Writes the new content to the stream it is given.</param>
    /// <param name="cancellationToken">Cancels the save; the original stays as it was.</param>
    /// <returns>A task that completes when the target has been replaced.</returns>
    /// <exception cref="IOException">The file cannot be written or replaced.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);
        (FileStream stream, string temp) = CreateTemp(path, FileOptions.Asynchronous);
        bool committed = false;
        try
        {
            await using (stream.ConfigureAwait(false))
            {
                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: true);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                TryDelete(temp);
            }
        }
    }

    // The temporary name is the target's name plus the process id and a counter, created with CreateNew so two savers never share one.
    private static (FileStream Stream, string Path) CreateTemp(string path, FileOptions options)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full) ?? ".";
        string name = Path.GetFileName(full);
        for (int attempt = 0; ; attempt++)
        {
            string temp = Path.Combine(dir, $".{name}.{Environment.ProcessId}.{Interlocked.Increment(ref s_counter)}.tmp");
            try
            {
                return (new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, options), temp);
            }
            catch (IOException) when (attempt < 100 && File.Exists(temp))
            {
                // A leftover from an earlier crash has this name; try the next counter value.
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Nothing more can be done; the original is intact.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
