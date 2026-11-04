using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice;

/// <summary>
/// The base of every exception OneOffice raises for the content of its input. This is the contract the parsers honour: malformed,
/// hostile, truncated, encrypted or unsupported input surfaces as an <see cref="OfficeException"/> (never as an
/// <see cref="InvalidCastException"/>, <see cref="IndexOutOfRangeException"/>, <see cref="OutOfMemoryException"/> or the like), and the
/// exception carries the <see cref="Defect"/> that says what and where. Caller mistakes (null arguments, a disposed object) and
/// failures of the caller's own streams (<see cref="IOException"/>, <see cref="OperationCanceledException"/>) are the only other exceptions.
/// Each format also offers a <c>TryOpen</c> beside its <c>Open</c> that returns false instead of raising these.
/// </summary>
#pragma warning disable CA1032 // The constructors carry the defect; the standard parameterless forms would leave it empty.
public abstract class OfficeException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="defect">The fatal defect that explains it.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    protected OfficeException(Defect defect, Exception? innerException = null)
        : base(defect?.ToString() ?? throw new ArgumentNullException(nameof(defect)), innerException)
    {
        Defect = defect;
    }

    /// <summary>Gets the defect that caused the exception; its severity is <see cref="DefectSeverity.Fatal"/>.</summary>
    public Defect Defect { get; }

    /// <summary>Gets where in the input the problem is.</summary>
    public DefectLocation Location => Defect.Location;
}

/// <summary>The input is not valid or is too damaged to read, and the reader cannot repair it.</summary>
public class OfficeFormatException : OfficeException
{
    /// <summary>Creates the exception from a defect.</summary>
    /// <param name="defect">The fatal defect.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public OfficeFormatException(Defect defect, Exception? innerException = null)
        : base(defect, innerException)
    {
    }

    /// <summary>Creates the exception, building the fatal defect.</summary>
    /// <param name="kind">The broad class of the problem.</param>
    /// <param name="code">The defect code (see <see cref="Defect.Code"/>).</param>
    /// <param name="message">The description.</param>
    /// <param name="location">Where the problem is.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public OfficeFormatException(DefectKind kind, string code, string message, DefectLocation location = default, Exception? innerException = null)
        : base(new Defect(kind, code, DefectSeverity.Fatal, message, location), innerException)
    {
    }
}

/// <summary>A resource limit in <see cref="OfficeLimits"/> was reached.</summary>
public sealed class OfficeLimitException : OfficeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="limitName">The name of the <see cref="OfficeLimits"/> property that was exceeded.</param>
    /// <param name="limit">The limit's value.</param>
    /// <param name="actual">The value the input asked for, or -1 if not known (for example a running total).</param>
    /// <param name="location">Where the input asked for it.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public OfficeLimitException(string limitName, long limit, long actual = -1, DefectLocation location = default, Exception? innerException = null)
        : base(
            new Defect(
                DefectKind.Limit,
                "limit." + ToCode(limitName),
                DefectSeverity.Fatal,
                actual >= 0 ? $"{limitName} is {limit}; the input needs {actual}." : $"{limitName} of {limit} was exceeded.",
                location),
            innerException)
    {
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }

    /// <summary>Gets the name of the <see cref="OfficeLimits"/> property that was exceeded, such as <c>MaxXmlDepth</c>.</summary>
    public string LimitName { get; }

    /// <summary>Gets the limit's value.</summary>
    public long Limit { get; }

    /// <summary>Gets the value the input asked for, or -1 if not known.</summary>
    public long Actual { get; }

    /// <summary>Raises <see cref="OfficeLimitException"/> when <paramref name="actual"/> is more than <paramref name="limit"/>.</summary>
    /// <param name="limitName">The name of the limit property.</param>
    /// <param name="limit">The limit's value.</param>
    /// <param name="actual">What the input asks for.</param>
    /// <param name="location">Where the input asks for it.</param>
    /// <exception cref="OfficeLimitException"><paramref name="actual"/> is greater than <paramref name="limit"/>.</exception>
    public static void ThrowIfExceeded(string limitName, long limit, long actual, DefectLocation location = default)
    {
        if (actual > limit)
        {
            throw new OfficeLimitException(limitName, limit, actual, location);
        }
    }

    // MaxXmlDepth becomes max-xml-depth: the defect code is the property name in kebab case.
    private static string ToCode(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsAsciiLetterUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('-');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

/// <summary>Why an encrypted input could not be opened.</summary>
public enum EncryptionProblem
{
    /// <summary>The input is encrypted and no password was given.</summary>
    PasswordRequired = 0,

    /// <summary>A password was given and it does not open the input.</summary>
    PasswordIncorrect = 1,
}

/// <summary>The input is encrypted and cannot be opened with the password given (or none).</summary>
public sealed class OfficeEncryptedException : OfficeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="problem">Why it cannot be opened.</param>
    /// <param name="location">Where the encryption was detected, if known.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public OfficeEncryptedException(EncryptionProblem problem, DefectLocation location = default, Exception? innerException = null)
        : base(
            new Defect(
                DefectKind.Encryption,
                problem == EncryptionProblem.PasswordRequired ? "encryption.password-required" : "encryption.password-incorrect",
                DefectSeverity.Fatal,
                problem == EncryptionProblem.PasswordRequired ? "The input is encrypted and needs a password." : "The password does not open the input.",
                location),
            innerException)
    {
        Problem = problem;
    }

    /// <summary>Gets why the input cannot be opened.</summary>
    public EncryptionProblem Problem { get; }
}

/// <summary>The input uses something the format allows but this library does not handle (an unknown encryption scheme, a feature outside the supported subset).</summary>
public class OfficeUnsupportedException : OfficeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="feature">A short name of what is not supported.</param>
    /// <param name="location">Where it was met, if known.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public OfficeUnsupportedException(string feature, DefectLocation location = default, Exception? innerException = null)
        : this(feature, "unsupported.feature", location, innerException)
    {
    }

    /// <summary>Creates the exception with a specific defect code.</summary>
    /// <param name="feature">A short name of what is not supported.</param>
    /// <param name="code">The defect code.</param>
    /// <param name="location">Where it was met, if known.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    protected OfficeUnsupportedException(string feature, string code, DefectLocation location, Exception? innerException)
        : base(new Defect(DefectKind.Unsupported, code, DefectSeverity.Fatal, feature + " is not supported.", location), innerException)
    {
        Feature = feature;
    }

    /// <summary>Gets the short name of what is not supported.</summary>
    public string Feature { get; }
}

/// <summary>
/// No reader or writer for a format is registered: the kind's package is not referenced or not implemented yet. Raised instead of
/// guessing, so a missing package is a clear error and not a wrong result.
/// </summary>
public sealed class FormatNotAvailableException : OfficeUnsupportedException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="format">The format that has no handler.</param>
    /// <param name="operation">What was asked, such as <c>read</c>, <c>write</c> or <c>extract text from</c>.</param>
    public FormatNotAvailableException(OfficeFormat format, string operation)
        : base(operation + " " + format, "unsupported.format-not-available", default, null)
    {
        Format = format;
    }

    /// <summary>Gets the format that has no handler.</summary>
    public OfficeFormat Format { get; }
}
#pragma warning restore CA1032
