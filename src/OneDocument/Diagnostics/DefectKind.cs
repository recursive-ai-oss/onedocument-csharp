namespace RecursiveAI.OneDocument.Diagnostics;

/// <summary>
/// The broad class of a <see cref="Defect"/>. The set is closed: it says where in the stack the problem was found, so a caller can
/// group and filter without knowing every code. The precise problem is the defect's <see cref="Defect.Code"/>, which each layer
/// owns and documents.
/// </summary>
public enum DefectKind
{
    /// <summary>The container is damaged: a zip, compound file or part directory (missing or duplicate parts, bad relationships, wrong content types).</summary>
    Packaging = 0,

    /// <summary>The text is not well-formed markup: unclosed or mismatched tags, bad entities, invalid characters.</summary>
    Markup = 1,

    /// <summary>The syntax is fine but the content breaks the format's structure: a missing or misplaced element, an out-of-order sequence.</summary>
    Structure = 2,

    /// <summary>A reference points at nothing: a relationship, style, numbering or shared-string index, a cell range.</summary>
    Reference = 3,

    /// <summary>A value cannot be used as written: out of range, not a number, not a date.</summary>
    Value = 4,

    /// <summary>A binary record is damaged: bad length, wrong type, truncated.</summary>
    Record = 5,

    /// <summary>A text encoding problem: invalid bytes, an unknown code page.</summary>
    Encoding = 6,

    /// <summary>A formula that does not parse or exceeds the formula limits.</summary>
    Formula = 7,

    /// <summary>A resource limit was reached (see <see cref="OfficeLimits"/>).</summary>
    Limit = 8,

    /// <summary>A feature the format allows but this library does not handle.</summary>
    Unsupported = 9,

    /// <summary>Encryption: a password is needed or wrong, or the scheme is not supported.</summary>
    Encryption = 10,
}
