using System.Globalization;
using RecursiveAI.OneDocument.Protection;
using P = RecursiveAI.OneDocument.Schema.Presentation;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>
/// The modify password of a presentation (<c>p:modifyVerifier</c>): PowerPoint opens the file read-only unless the password is given.
/// The password is stored as a salted, iterated hash (SHA-512 by default).
/// </summary>
/// <remarks>
/// <b>Protection is not encryption.</b> The slides stay readable and anyone who edits the XML can remove the element. Encrypt the file to
/// keep it confidential. A verifier in the older spelling (<c>p:cryptAlgorithmSid</c>, <c>p:hashData</c>, <c>p:saltData</c>) is verified with
/// the same hash procedure on the password as UTF-16LE text, which no PowerPoint-written file has confirmed yet.
/// </remarks>
public sealed class PresentationProtection
{
    private readonly Presentation _owner;

    internal PresentationProtection(Presentation owner)
    {
        _owner = owner;
    }

    private P.ModifyVerifier? Element => _owner.View.ModifyVerifier;

    /// <summary>Gets a value indicating whether the presentation has a modify password.</summary>
    public bool IsProtected => Element is not null;

    /// <summary>Gets the stored verifier, or null when none is stored in a readable spelling.</summary>
    public PasswordVerifier? Verifier
    {
        get
        {
            if (Element is not { } e)
            {
                return null;
            }

            if (PasswordVerifier.TryRead(e.AlgorithmName, e.HashValue, e.SaltValue, e.SpinValue?.ToString(CultureInfo.InvariantCulture), out PasswordVerifier? v))
            {
                return v;
            }

            return LegacyAlgorithm(e.CryptAlgorithmSid) is { } name
                && PasswordVerifier.TryRead(name, e.HashData, e.SaltData, e.SpinCount?.ToString(CultureInfo.InvariantCulture), out PasswordVerifier? old)
                ? old
                : null;
        }
    }

    /// <summary>Checks a password against the stored hash.</summary>
    /// <param name="password">The password to test.</param>
    /// <returns><see cref="PasswordCheck.Match"/> when it matches or none is stored; <see cref="PasswordCheck.Mismatch"/>; <see cref="PasswordCheck.UnsupportedAlgorithm"/> for an algorithm that cannot be computed.</returns>
    /// <exception cref="OfficeLimitException">The stored spin count is larger than <see cref="OfficeLimits.MaxEncryptionSpinCount"/>.</exception>
    public PasswordCheck Verify(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (Element is not { } e)
        {
            return PasswordCheck.Match;
        }

        if (Verifier is { } v)
        {
            return v.Check(password, _owner.Context.Limits);
        }

        return string.IsNullOrEmpty(e.HashValue) && string.IsNullOrEmpty(e.HashData) ? PasswordCheck.Match : PasswordCheck.UnsupportedAlgorithm;
    }

    /// <summary>Sets the modify password, replacing any.</summary>
    /// <param name="password">The password; not empty.</param>
    /// <param name="options">How it is hashed; null means SHA-512, 100,000 iterations.</param>
    /// <exception cref="ArgumentException">The password is empty.</exception>
    public void Protect(string password, ProtectionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        options ??= new ProtectionOptions();
        PasswordVerifier v = PasswordVerifier.Create(password, options.Algorithm, options.SpinCount, options.Seed.Span, "modifyVerifier");
        _ = _owner.View.RemoveModifyVerifier();
        P.ModifyVerifier e = _owner.View.GetOrAddModifyVerifier();
        e.AlgorithmName = v.AlgorithmName;
        e.HashValue = v.HashBase64;
        e.SaltValue = v.SaltBase64;
        e.SpinValue = v.SpinCount;
    }

    /// <summary>Removes the modify password without asking for it.</summary>
    /// <returns>True when there was one.</returns>
    public bool Remove() => _owner.View.RemoveModifyVerifier();

    /// <summary>Removes the modify password when the given one matches.</summary>
    /// <param name="password">The password.</param>
    /// <returns>The check's outcome; removed only on <see cref="PasswordCheck.Match"/>.</returns>
    public PasswordCheck TryRemove(string password)
    {
        PasswordCheck c = Verify(password);
        if (c == PasswordCheck.Match)
        {
            _ = Remove();
        }

        return c;
    }

    // ECMA-376 Part 4 §16.3.1.3 (cryptAlgorithmSid): CryptoAPI algorithm ids.
    private static string? LegacyAlgorithm(long? sid) => sid switch
    {
        3 => "MD5",
        4 => "SHA-1",
        12 => "SHA-256",
        13 => "SHA-384",
        14 => "SHA-512",
        _ => null,
    };
}

public sealed partial class Presentation
{
    private PresentationProtection? _protection;

    /// <summary>Gets the modify password (<c>p:modifyVerifier</c>). See <see cref="PresentationProtection"/>: protection is not encryption.</summary>
    public PresentationProtection Protection => _protection ??= new PresentationProtection(this);
}
