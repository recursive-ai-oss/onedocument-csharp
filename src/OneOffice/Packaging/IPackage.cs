using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Packaging;

/// <summary>Whether a relationship's target is inside the package or outside it.</summary>
public enum RelationshipTargetMode
{
    /// <summary>The target is a part of the same package (the default).</summary>
    Internal = 0,

    /// <summary>The target is outside the package: a link, a linked image, an external workbook. Never followed without a resolver the caller supplies.</summary>
    External = 1,
}

/// <summary>One relationship of a part (or of the package) in an Open Packaging Conventions package.</summary>
/// <param name="Id">The relationship id, unique within its owner, such as <c>rId1</c>.</param>
/// <param name="Type">The relationship type URI. Kept as written, so Transitional and Strict spellings both round-trip.</param>
/// <param name="Target">The target as written: relative to the owner part for internal targets, a URI for external ones.</param>
/// <param name="TargetMode">Inside the package or outside it.</param>
public sealed record Relationship(string Id, string Type, string Target, RelationshipTargetMode TargetMode = RelationshipTargetMode.Internal);

/// <summary>The kind of container a package sits on.</summary>
public enum PackageKind
{
    /// <summary>An Open Packaging Conventions package on zip (OOXML).</summary>
    Opc = 0,

    /// <summary>A compound file (legacy binary formats, encrypted OOXML).</summary>
    CompoundFile = 1,

    /// <summary>An OpenDocument package on zip.</summary>
    Odf = 2,

    /// <summary>A package held only in memory, with no file format of its own (tests and generated documents).</summary>
    InMemory = 3,
}

/// <summary>
/// One part of a <see cref="IPackage"/>: a named blob with a content type (OPC) or a stream in a compound file. Its bytes load
/// lazily and are not decoded further here.
/// </summary>
/// <remarks>
/// The <see cref="IsUntouched"/> state is the heart of the fidelity contract: a part nobody changed is copied by the writer exactly
/// as stored (a zip entry's compressed bytes, a stream's sectors' content), never decompressed and recompressed, so it comes out
/// byte-identical. Reading never touches a part. Replacing its content or opening it for writing does, for good.
/// </remarks>
public interface IPackagePart
{
    /// <summary>
    /// Gets the part's name: the part URI such as <c>/word/document.xml</c> for OPC and ODF, or the path of a stream such as
    /// <c>/WordDocument</c> in a compound file (where names may contain characters a part URI cannot).
    /// </summary>
    string Name { get; }

    /// <summary>Gets the content type (OPC), or the empty string where the container has none.</summary>
    string ContentType { get; }

    /// <summary>Gets the uncompressed length in bytes, or -1 when not known without reading it.</summary>
    long Length { get; }

    /// <summary>Gets a value indicating whether the part is exactly as it was when the package was opened, so the writer may copy it raw. A part added in this session is never untouched.</summary>
    bool IsUntouched { get; }

    /// <summary>Opens the part's content for reading, as a stream of the uncompressed bytes. The caller disposes it.</summary>
    /// <returns>A readable stream positioned at zero.</returns>
    /// <exception cref="OfficeException">The part is damaged or exceeds a limit.</exception>
    Stream OpenRead();

    /// <summary>
    /// Gets the whole content, loaded on first use and kept. Do not change the bytes: they may be shared with the open file.
    /// </summary>
    /// <returns>The uncompressed content.</returns>
    /// <exception cref="OfficeLimitException">The part is longer than <see cref="OfficeLimits.MaxPartSize"/>.</exception>
    /// <exception cref="OfficeException">The part is damaged.</exception>
    ReadOnlyMemory<byte> GetBytes();

    /// <summary>Replaces the content. The part stops being untouched.</summary>
    /// <param name="content">The new content; not copied, so do not change it afterwards.</param>
    void SetBytes(ReadOnlyMemory<byte> content);

    /// <summary>Opens a stream that replaces the content when it is disposed, for writers that stream. The part stops being untouched at once.</summary>
    /// <returns>A writable stream.</returns>
    Stream OpenWrite();
}

/// <summary>
/// A package of named parts over a container: the shape the packaging reader and the compound-file reader both present, so the
/// layers above read and write parts without caring which container is below. Packages are not safe for concurrent mutation;
/// reading parts from several threads is safe after open.
/// </summary>
public interface IPackage : IDisposable
{
    /// <summary>Gets the container the package sits on.</summary>
    PackageKind Kind { get; }

    /// <summary>Gets the limits the package was opened with.</summary>
    OfficeLimits Limits { get; }

    /// <summary>Gets the file the package was opened from, or null (opened from a source that is not a file, or created in memory).</summary>
    string? FilePath { get; }

    /// <summary>Gets the parts in the order they are stored, which is the order the writer keeps for untouched ones.</summary>
    IReadOnlyList<IPackagePart> Parts { get; }

    /// <summary>Gets a value indicating whether anything differs from what was opened: a part touched, added or removed, or package-level data changed.</summary>
    bool IsModified { get; }

    /// <summary>Finds a part by name.</summary>
    /// <param name="name">The name, compared ignoring case for OPC and ODF, exactly for compound files.</param>
    /// <returns>The part, or null.</returns>
    IPackagePart? FindPart(string name);

    /// <summary>Adds a part.</summary>
    /// <param name="name">The name.</param>
    /// <param name="contentType">The content type (empty where the container has none).</param>
    /// <param name="content">The content; not copied.</param>
    /// <returns>The new part.</returns>
    /// <exception cref="ArgumentException">A part of that name exists, or the name is not valid for the container.</exception>
    IPackagePart AddPart(string name, string contentType, ReadOnlyMemory<byte> content);

    /// <summary>Removes a part.</summary>
    /// <param name="name">The name.</param>
    /// <returns>True if a part was removed.</returns>
    bool RemovePart(string name);

    /// <summary>
    /// Writes the package to a stream: untouched parts raw, the rest as they are now. The destination receives a complete,
    /// valid container or the call throws; it is not required to be seekable.
    /// </summary>
    /// <param name="destination">The stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    void WriteTo(Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves to the file the package was opened from, atomically: the new package is written to a temporary file beside it, flushed
    /// and renamed over the original, so an interrupted save leaves the original as it was (<see cref="IO.AtomicFile"/>).
    /// </summary>
    /// <param name="cancellationToken">Cancels the save; the original stays as it was.</param>
    /// <exception cref="InvalidOperationException">The package has no <see cref="FilePath"/>.</exception>
    void Save(CancellationToken cancellationToken = default);

    /// <summary>Saves to a path, atomically as <see cref="Save"/> does.</summary>
    /// <param name="path">The target file.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    void SaveAs(string path, CancellationToken cancellationToken = default);
}

/// <summary>An <see cref="IPackage"/> that follows the Open Packaging Conventions: content types and relationships.</summary>
public interface IOpcPackage : IPackage
{
    /// <summary>Gets the relationships of a part, or of the package itself.</summary>
    /// <param name="sourcePartName">The owner part's name, or null for the package-level relationships (<c>/_rels/.rels</c>).</param>
    /// <returns>The relationships in stored order; empty when there are none.</returns>
    IReadOnlyList<Relationship> GetRelationships(string? sourcePartName = null);

    /// <summary>Replaces the relationships of a part, or of the package itself.</summary>
    /// <param name="sourcePartName">The owner part's name, or null for the package.</param>
    /// <param name="relationships">The new list, in the order to store.</param>
    void SetRelationships(string? sourcePartName, IReadOnlyList<Relationship> relationships);
}
