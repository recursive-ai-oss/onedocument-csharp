using RecursiveAI.OneArchive;

namespace RecursiveAI.OneOffice;

/// <summary>
/// The resource limits for reading and writing, as one immutable object that is passed down through every layer: containers,
/// markup, records, formulas, the hubs. All limits are on by default and bound what a hostile or broken file can make the library
/// do. A limit that is hit raises an <see cref="OfficeLimitException"/> naming the property that was exceeded; nothing is silently
/// truncated. Every value must be positive: to switch a limit off effectively, set it to <see cref="long.MaxValue"/> knowingly.
/// </summary>
/// <remarks>
/// The defaults suit documents people open and edit, with headroom over what Office itself produces. They are small enough that a
/// 100 KB file cannot ask for gigabytes, and large enough that a 1,000-page document or a 500-slide deck opens without notice.
/// Spreadsheets with hundreds of millions of cells are meant to be read with the streaming cursors, which are bound only by
/// <see cref="MaxTotalUncompressedSize"/> and <see cref="MaxExpansionRatio"/>; use <see cref="Large"/> for those inputs.
/// DTDs and entity declarations in XML are always refused; there is no limit to raise for them.
/// </remarks>
public sealed class OfficeLimits
{
    /// <summary>The default for <see cref="MaxTotalUncompressedSize"/>: 2 GiB.</summary>
    public const long DefaultMaxTotalUncompressedSize = 2L << 30;

    /// <summary>The default for <see cref="MaxExpansionRatio"/>: 200 to one. Office's own XML compresses 10 to 30 times; 200 leaves room for sparse sheets.</summary>
    public const long DefaultMaxExpansionRatio = 200;

    /// <summary>The default for <see cref="MaxPartCount"/>: 50,000.</summary>
    public const int DefaultMaxPartCount = 50_000;

    /// <summary>The default for <see cref="MaxPartSize"/>: 1 GiB.</summary>
    public const long DefaultMaxPartSize = 1L << 30;

    /// <summary>The default for <see cref="MaxXmlDepth"/>: 256 open elements.</summary>
    public const int DefaultMaxXmlDepth = 256;

    /// <summary>The default for <see cref="MaxXmlAttributeCount"/>: 256 per element.</summary>
    public const int DefaultMaxXmlAttributeCount = 256;

    /// <summary>The default for <see cref="MaxXmlNameLength"/>: 1,024 characters.</summary>
    public const int DefaultMaxXmlNameLength = 1024;

    /// <summary>The default for <see cref="MaxFormulaLength"/>: 8,192 characters, Excel's own limit.</summary>
    public const int DefaultMaxFormulaLength = 8192;

    /// <summary>The default for <see cref="MaxFormulaDepth"/>: 64 levels, Excel's own limit.</summary>
    public const int DefaultMaxFormulaDepth = 64;

    /// <summary>The default for <see cref="MaxRecursionDepth"/>: 256.</summary>
    public const int DefaultMaxRecursionDepth = 256;

    /// <summary>The default for <see cref="MaxRecordCount"/>: 100 million.</summary>
    public const long DefaultMaxRecordCount = 100_000_000;

    /// <summary>The default for <see cref="MaxRecordSize"/>: 256 MiB.</summary>
    public const int DefaultMaxRecordSize = 256 << 20;

    /// <summary>The default for <see cref="MaxAllocationBytes"/>: 1 GiB.</summary>
    public const long DefaultMaxAllocationBytes = 1L << 30;

    /// <summary>The default for <see cref="MaxDefects"/>: 10,000.</summary>
    public const int DefaultMaxDefects = 10_000;

    /// <summary>
    /// The number of produced bytes below which <see cref="MaxExpansionRatio"/> is not checked: 32 MiB. A tiny file that
    /// legitimately expands a lot is not a threat until it has expanded a lot. (This is the grace of the underlying zip reader.)
    /// </summary>
    public const long ExpansionRatioGraceBytes = ArchiveLimits.ExpansionRatioGraceBytes;

    /// <summary>Gets the shared default limits.</summary>
    public static OfficeLimits Default { get; } = new();

    /// <summary>
    /// Gets shared limits for very large inputs such as generated stress workbooks: 256 GiB total, parts up to about 2 GB
    /// (the most one buffer can hold), a million parts and an expansion ratio of 1,000. Every XML, formula and recursion limit keeps
    /// its default, so a deeply nested or hostile file is still refused.
    /// </summary>
    public static OfficeLimits Large { get; } = new()
    {
        MaxTotalUncompressedSize = 256L << 30,
        MaxPartSize = 2_000_000_000,
        MaxPartCount = 1_000_000,
        MaxExpansionRatio = 1000,
        MaxAllocationBytes = 2_000_000_000,
    };

    /// <summary>Gets the most uncompressed bytes all parts of a package may declare together. Default 2 GiB.</summary>
    public long MaxTotalUncompressedSize { get; init; } = DefaultMaxTotalUncompressedSize;

    /// <summary>
    /// Gets the most bytes a reader may produce per byte of compressed input (zip-bomb guard). Checked only after
    /// <see cref="ExpansionRatioGraceBytes"/> bytes were produced. Default 200.
    /// </summary>
    public long MaxExpansionRatio { get; init; } = DefaultMaxExpansionRatio;

    /// <summary>Gets the most parts (zip entries, compound-file streams) a package may hold. Default 50,000.</summary>
    public int MaxPartCount { get; init; } = DefaultMaxPartCount;

    /// <summary>
    /// Gets the most uncompressed bytes one part may have when it is read whole into memory. Streaming cursors, which never hold
    /// the whole part, are not bound by it. Default 1 GiB.
    /// </summary>
    public long MaxPartSize { get; init; } = DefaultMaxPartSize;

    /// <summary>Gets the deepest element nesting the XML tokenizer accepts. Default 256.</summary>
    public int MaxXmlDepth { get; init; } = DefaultMaxXmlDepth;

    /// <summary>Gets the most attributes one XML element may carry (namespace declarations included). Default 256.</summary>
    public int MaxXmlAttributeCount { get; init; } = DefaultMaxXmlAttributeCount;

    /// <summary>Gets the longest element, attribute or prefix name, in characters. Default 1,024.</summary>
    public int MaxXmlNameLength { get; init; } = DefaultMaxXmlNameLength;

    /// <summary>Gets the longest formula text the parser accepts, in characters. Default 8,192.</summary>
    public int MaxFormulaLength { get; init; } = DefaultMaxFormulaLength;

    /// <summary>Gets the deepest formula nesting (calls, parentheses, array constants) the parser and evaluator accept. Default 64.</summary>
    public int MaxFormulaDepth { get; init; } = DefaultMaxFormulaDepth;

    /// <summary>Gets the deepest recursion any other tree walk, group tree or evaluator may reach. Default 256.</summary>
    public int MaxRecursionDepth { get; init; } = DefaultMaxRecursionDepth;

    /// <summary>Gets the most records one binary stream (BIFF, XLSB, DOC, PPT, RTF groups) may hold. Default 100 million.</summary>
    public long MaxRecordCount { get; init; } = DefaultMaxRecordCount;

    /// <summary>Gets the most bytes one binary record may declare. Default 256 MiB.</summary>
    public int MaxRecordSize { get; init; } = DefaultMaxRecordSize;

    /// <summary>Gets the most bytes any single allocation driven by input may request (a table, a string, a buffer). Default 1 GiB.</summary>
    public long MaxAllocationBytes { get; init; } = DefaultMaxAllocationBytes;

    /// <summary>Gets the most defects a <see cref="Diagnostics.DefectCollection"/> made for an open keeps. Default 10,000.</summary>
    public int MaxDefects { get; init; } = DefaultMaxDefects;

    /// <summary>
    /// Maps the limits that apply to the container layer onto the underlying archive reader's limits: total size, entry count and
    /// expansion ratio. Path length and depth keep the archive reader's defaults.
    /// </summary>
    /// <returns>Archive limits with the container-level values of this object.</returns>
    public ArchiveLimits ToArchiveLimits() => new()
    {
        MaxTotalSize = MaxTotalUncompressedSize,
        MaxEntryCount = (int)Math.Min(MaxPartCount, int.MaxValue),
        MaxExpansionRatio = MaxExpansionRatio,
    };

    /// <summary>Checks that every limit is positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A limit is zero or negative.</exception>
    public void Validate()
    {
        Positive(MaxTotalUncompressedSize, nameof(MaxTotalUncompressedSize));
        Positive(MaxExpansionRatio, nameof(MaxExpansionRatio));
        Positive(MaxPartCount, nameof(MaxPartCount));
        Positive(MaxPartSize, nameof(MaxPartSize));
        Positive(MaxXmlDepth, nameof(MaxXmlDepth));
        Positive(MaxXmlAttributeCount, nameof(MaxXmlAttributeCount));
        Positive(MaxXmlNameLength, nameof(MaxXmlNameLength));
        Positive(MaxFormulaLength, nameof(MaxFormulaLength));
        Positive(MaxFormulaDepth, nameof(MaxFormulaDepth));
        Positive(MaxRecursionDepth, nameof(MaxRecursionDepth));
        Positive(MaxRecordCount, nameof(MaxRecordCount));
        Positive(MaxRecordSize, nameof(MaxRecordSize));
        Positive(MaxAllocationBytes, nameof(MaxAllocationBytes));
        Positive(MaxDefects, nameof(MaxDefects));
    }

    private static void Positive(long value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "A limit must be positive.");
        }
    }
}
