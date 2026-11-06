using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Inspection;

namespace RecursiveAI.OneDocument.Packaging;

/// <summary>
/// Brings an open package to the content of another package with the fewest changes, and runs the sanitiser on a package that is
/// open in a hub. Only parts whose bytes, content type or relationships differ are touched, so every other part is still written raw.
/// </summary>
internal static class PackageSync
{
    /// <summary>Makes <paramref name="target"/> hold what <paramref name="source"/> holds.</summary>
    public static void Apply(IOpcPackage target, IOpcPackage source)
    {
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IPackagePart part in source.Parts)
        {
            sourceNames.Add(part.Name);
        }

        foreach (IPackagePart part in target.Parts.ToArray())
        {
            if (!sourceNames.Contains(part.Name))
            {
                target.RemovePart(part.Name);
            }
        }

        foreach (IPackagePart part in source.Parts)
        {
            IPackagePart? existing = target.FindPart(part.Name);
            ReadOnlyMemory<byte> bytes = part.GetBytes();
            if (existing is null)
            {
                target.AddPart(part.Name, part.ContentType, bytes);
            }
            else if (!string.Equals(existing.ContentType, part.ContentType, StringComparison.Ordinal))
            {
                target.RemovePart(part.Name);
                target.AddPart(part.Name, part.ContentType, bytes);
            }
            else if (!existing.GetBytes().Span.SequenceEqual(bytes.Span))
            {
                existing.SetBytes(bytes);
            }
        }

        var owners = new List<string?> { null };
        foreach (IPackagePart part in source.Parts)
        {
            owners.Add(part.Name);
        }

        foreach (IPackagePart part in target.Parts)
        {
            owners.Add(part.Name);
        }

        foreach (string? owner in owners.Distinct(OwnerComparer.Instance))
        {
            IReadOnlyList<Relationship> want = SafeRelationships(source, owner);
            IReadOnlyList<Relationship> have = SafeRelationships(target, owner);
            if (!Enumerable.SequenceEqual(want, have))
            {
                target.SetRelationships(owner, want);
            }
        }
    }

    private sealed class OwnerComparer : IEqualityComparer<string?>
    {
        public static readonly OwnerComparer Instance = new();

        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(string? obj) => obj is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
    }

    private static IReadOnlyList<Relationship> SafeRelationships(IOpcPackage package, string? owner) =>
        owner is not null && package.FindPart(owner) is null ? [] : package.GetRelationships(owner);

    /// <summary>
    /// Sanitises the package a hub has open: the package is written to memory, the sanitiser runs on that copy (including its own
    /// verification, so nothing is changed when verification fails), and the package is brought to the result.
    /// </summary>
    public static SanitizeReport SanitizeInPlace(IOpcPackage package, SanitizeOptions? options, OpenContext context, CancellationToken cancellationToken)
    {
        using var input = new MemoryStream();
        if (package is OpcPackage opc)
        {
            opc.WriteTo(input, cancellationToken);
        }
        else
        {
            // Another kind of package (in memory) is copied into a zip package first, because the sanitiser reads a file.
            using OpcPackage copy = OpcPackage.Create(package.Limits);
            Apply(copy, package);
            copy.WriteTo(input, cancellationToken);
        }

        input.Position = 0;
        using var output = new MemoryStream();
        SanitizeReport report = Sanitizer.Sanitize(input, output, options, context, cancellationToken);
        output.Position = 0;
        using OpcPackage result = OpcPackage.Open(output, context, leaveOpen: true);
        Apply(package, result);
        return report;
    }
}
