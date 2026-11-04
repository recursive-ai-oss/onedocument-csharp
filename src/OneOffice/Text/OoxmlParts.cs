using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.TextExtraction;

/// <summary>
/// The package access the text extractors need: forward streams of parts by name, and relationships, over <see cref="OpcPackage"/>
/// (zip and Flat OPC). Opening reads only the directory, the content types and the package relationships; a part is read when the
/// extractor asks for it, as a stream, so a large part is never held whole.
/// </summary>
internal sealed class OoxmlParts : IDisposable
{
    private readonly OpcPackage _package;

    public OoxmlParts(IByteSource source, OpenContext context) => _package = OpcPackage.Open(new Borrowed(source), context);

    public bool Contains(string partName) => _package.FindPart(partName) is not null;

    /// <summary>Opens a part for forward reading.</summary>
    public Stream Open(string partName) => _package.FindPart(partName)!.OpenRead();

    /// <summary>Gets the main part, named by the package relationship of type <c>officeDocument</c> (ECMA-376 Part 2 §9.3).</summary>
    public string? FindMainPart()
    {
        foreach (Relationship rel in _package.GetRelationships(null))
        {
            if (rel.Type.EndsWith("/officeDocument", StringComparison.Ordinal) && Resolve(null, rel) is { } name && Contains(name))
            {
                return _package.FindPart(name)!.Name;
            }
        }

        return null;
    }

    /// <summary>Reads the relationships of a part (or of the package, for null).</summary>
    public List<Relationship> ReadRelationships(string? sourcePart) => [.. _package.GetRelationships(sourcePart)];

    /// <summary>Resolves an internal relationship target against the part that holds the relationship.</summary>
    public static string? Resolve(string? sourcePart, Relationship rel) =>
        rel.TargetMode == RelationshipTargetMode.Internal && OpcPackage.TryResolveTarget(sourcePart, rel, out PartUri target) ? target.Value : null;

    public void Dispose() => _package.Dispose();

    // The package owns the source it is given; the extractor's caller keeps theirs, so the package gets a view that disposes nothing.
    private sealed class Borrowed(IByteSource inner) : IByteSource
    {
        public long Length => inner.Length;

        public bool CanReadSync => inner.CanReadSync;

        public int Read(long offset, Span<byte> destination) => inner.Read(offset, destination);

        public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(offset, destination, cancellationToken);

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory) => inner.TryGetMemory(out memory);

        public void Dispose()
        {
        }
    }
}
