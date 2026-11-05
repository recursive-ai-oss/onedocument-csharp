using System.Text;

namespace RecursiveAI.OneOffice.Spreadsheets.Csv;

/// <summary>How a CSV or TSV stream is split into records and fields, and how its bytes are decoded.</summary>
public sealed class CsvReadOptions
{
    /// <summary>
    /// Gets the field delimiter. Null (the default) detects it from the first 64 Ki characters: of comma, semicolon, tab and pipe the
    /// one that splits the first records (up to 20, outside double quotes) into the same number of fields, more than one, wins;
    /// when none is consistent, the one that occurs most; when none occurs, comma.
    /// </summary>
    public char? Delimiter { get; init; }

    /// <summary>Gets the quote character. The default is the double quote. It opens quoting only at the start of a field.</summary>
    public char Quote { get; init; } = '"';

    /// <summary>
    /// Gets the encoding to use. Null (the default) reads a byte-order mark (UTF-8, UTF-16 little or big endian); without one, UTF-8 if the
    /// first 64 KiB are valid UTF-8, otherwise Windows-1252 (with a <c>csv.encoding-assumed</c> defect). A caller-chosen encoding is used
    /// as given, and a byte-order mark of a different Unicode encoding still wins. Any <see cref="Encoding"/> works, but code pages other
    /// than the Unicode ones and Latin-1 need <c>CodePagesEncodingProvider</c> registered by the caller, which is a platform choice
    /// (it is not available in every trimmed or WebAssembly host).
    /// </summary>
    public Encoding? Encoding { get; init; }

    /// <summary>Gets a value indicating whether empty lines are skipped. The default is false: Excel keeps an empty line as an empty row.</summary>
    public bool SkipBlankLines { get; init; }
}
