using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Tools.DocxProbe;

internal static partial class Program
{
    /// <summary>
    /// <c>DocxProbe lazyopen FILE</c>: bytes read from the file by <c>Document.Open</c>, by reading the style definitions, and by
    /// the first paragraph (which parses the main part).
    /// </summary>
    private static int LazyOpen(string file)
    {
        var counting = new CountingSource(ByteSource.FromFile(file));
        using Document doc = Document.Open(counting);
        Console.WriteLine($"{file}: {counting.Length / 1024} KiB file; open read {counting.BytesRead / 1024} KiB");
        long before = counting.BytesRead;
        int styles = doc.Styles.Styles.Count;
        Console.WriteLine($"styles ({styles}): {(counting.BytesRead - before) / 1024} KiB more");
        before = counting.BytesRead;
        int length = doc.Paragraphs.GetEnumerator().MoveNext() ? 1 : 0;
        Console.WriteLine($"first paragraph ({length}): {(counting.BytesRead - before) / 1024} KiB more");
        return 0;
    }

    private sealed class CountingSource(IByteSource inner) : IByteSource
    {
        public long BytesRead { get; private set; }

        public long Length => inner.Length;

        public bool CanReadSync => inner.CanReadSync;

        public int Read(long offset, Span<byte> destination)
        {
            int n = inner.Read(offset, destination);
            BytesRead += n;
            return n;
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default) => inner.ReadAsync(offset, destination, cancellationToken);

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory) => inner.TryGetMemory(out memory);

        public void Dispose() => inner.Dispose();
    }
}
