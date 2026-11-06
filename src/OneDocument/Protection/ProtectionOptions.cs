namespace RecursiveAI.OneDocument.Protection;

/// <summary>How a new protection password is written. Every property has a safe default.</summary>
public sealed class ProtectionOptions
{
    /// <summary>Gets the hash algorithm. Default <see cref="ProtectionHash.Sha512"/>.</summary>
    public ProtectionHash Algorithm { get; init; } = ProtectionHash.Sha512;

    /// <summary>Gets the number of hash iterations. Default 100,000, what Office writes; it must not be negative.</summary>
    public int SpinCount { get; init; } = PasswordVerifier.DefaultSpinCount;

    /// <summary>
    /// Gets the bytes the 16-byte salt is derived from. Default empty. The library never reads the clock or a random generator, so the
    /// same document, password and seed always give the same bytes; pass a seed that differs per document for a distinct salt in each.
    /// </summary>
    public ReadOnlyMemory<byte> Seed { get; init; }
}
