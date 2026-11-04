using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice;

/// <summary>
/// What every open passes down: the <see cref="OfficeLimits"/>, where defects go, and the password for encrypted input. One
/// context goes through every layer of one open, so the same limits bound the zip reader, the XML tokenizer and the formula parser,
/// and every repair lands in the same sink. It is immutable; the defect sink it holds is where the mutable state lives.
/// </summary>
public sealed class OpenContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="limits">The limits; null for <see cref="OfficeLimits.Default"/>. Validated: every limit must be positive.</param>
    /// <param name="defects">Where defects are reported; null for a new <see cref="DefectCollection"/> sized by <see cref="OfficeLimits.MaxDefects"/>.</param>
    /// <param name="password">The password for encrypted input, or null. Never stored in a document or a defect message.</param>
    /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
    public OpenContext(OfficeLimits? limits = null, IDefectSink? defects = null, string? password = null)
    {
        Limits = limits ?? OfficeLimits.Default;
        Limits.Validate();
        Defects = defects ?? new DefectCollection(Limits.MaxDefects);
        Password = password;
    }

    /// <summary>Gets the limits.</summary>
    public OfficeLimits Limits { get; }

    /// <summary>Gets the sink readers report defects to.</summary>
    public IDefectSink Defects { get; }

    /// <summary>Gets the password for encrypted input, or null.</summary>
    public string? Password { get; }
}
