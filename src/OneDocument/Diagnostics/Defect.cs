namespace RecursiveAI.OneDocument.Diagnostics;

/// <summary>
/// One thing a reader found wrong in its input and what it did about it. Reading is tolerant: it opens what Office opens, so a
/// repair is not an error, but it is never silent. Every repair is a defect with a location, written to an <see cref="IDefectSink"/>.
/// </summary>
/// <param name="Kind">The broad class of the problem.</param>
/// <param name="Code">
/// A stable, lower-case, dot-separated identifier for the precise problem, such as <c>opc.content-type-missing</c>. The first segment
/// names the layer (<c>opc</c>, <c>cfb</c>, <c>xml</c>, <c>biff</c>, <c>doc</c>, <c>sheet</c>, <c>formula</c>, <c>limit</c>, …). Codes are
/// never reused for another meaning; each layer lists its codes in the defect catalogue.
/// </param>
/// <param name="Severity">What the reader did.</param>
/// <param name="Message">A one-line description in English for people. Not for matching: use <paramref name="Code"/>.</param>
/// <param name="Location">Where it was found.</param>
public sealed record Defect(DefectKind Kind, string Code, DefectSeverity Severity, string Message, DefectLocation Location)
{
    /// <summary>Returns <c>severity code at location: message</c>, for logs.</summary>
    /// <returns>The text form.</returns>
    public override string ToString()
    {
        string where = Location.ToString();
        return where.Length == 0
            ? $"{Severity} {Code}: {Message}"
            : $"{Severity} {Code} at {where}: {Message}";
    }
}
