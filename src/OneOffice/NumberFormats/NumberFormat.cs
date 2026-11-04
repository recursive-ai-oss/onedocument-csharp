using System;
using System.Collections.Generic;
using System.Globalization;

namespace RecursiveAI.OneOffice.NumberFormats;

/// <summary>What a format code displays; the hub and the calc engine use it for type coercion and <c>CELL("format")</c>.</summary>
public enum NumberFormatCategory
{
    /// <summary>The General format.</summary>
    General = 0,

    /// <summary>Plain numbers (digit placeholders, thousands separators).</summary>
    Number = 1,

    /// <summary>Numbers with a currency symbol.</summary>
    Currency = 2,

    /// <summary>Currency with the symbol padded to the cell edge (<c>*</c> fill), as the built-in accounting formats do.</summary>
    Accounting = 3,

    /// <summary>A date without a time of day.</summary>
    Date = 4,

    /// <summary>A time of day or an elapsed time without a date.</summary>
    Time = 5,

    /// <summary>A date and a time of day.</summary>
    DateTime = 6,

    /// <summary>A number multiplied by 100 and shown with a percent sign.</summary>
    Percentage = 7,

    /// <summary>A fraction such as <c># ?/?</c>.</summary>
    Fraction = 8,

    /// <summary>Scientific notation.</summary>
    Scientific = 9,

    /// <summary>The text format <c>@</c> or a format whose displayed content is only text.</summary>
    Text = 10,

    /// <summary>Anything else (for example only literal text).</summary>
    Custom = 11,
}

/// <summary>Why a format code is invalid. Codes are stable and listed in the number-format notes.</summary>
/// <param name="Code">The stable code, <c>numfmt.problem</c>.</param>
/// <param name="Message">A one-line description in English.</param>
/// <param name="Position">The zero-based index in the format code where the problem was found.</param>
public sealed record NumberFormatDefect(string Code, string Message, int Position)
{
    /// <summary>Returns <c>code at position: message</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"{Code} at {Position.ToString(CultureInfo.InvariantCulture)}: {Message}";
}

/// <summary>A section colour: one of the eight named colours or <c>[Color1]</c> to <c>[Color56]</c> from the legacy 56-colour palette.</summary>
/// <param name="Rgb">The colour as 0xRRGGBB.</param>
/// <param name="PaletteIndex">1 to 56 for <c>[ColorN]</c>; 0 for a named colour.</param>
/// <param name="Name">The name (<c>Red</c>) for a named colour; empty for <c>[ColorN]</c>.</param>
public readonly record struct NumberFormatColor(int Rgb, int PaletteIndex, string Name);

/// <summary>A comparison condition on a section, such as <c>[&gt;100]</c>.</summary>
/// <param name="Operator">One of <c>=</c>, <c>&lt;&gt;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&gt;=</c>.</param>
/// <param name="Operand">The number the value is compared with.</param>
public readonly record struct NumberFormatCondition(string Operator, double Operand)
{
    /// <summary>Tests a value against the condition.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Whether the section applies.</returns>
    public bool Matches(double value) => Operator switch
    {
        "=" => value == Operand,
        "<>" => value != Operand,
        "<" => value < Operand,
        ">" => value > Operand,
        "<=" => value <= Operand,
        _ => value >= Operand,
    };
}

/// <summary>What a section does with a number.</summary>
public enum NumberFormatSectionKind
{
    /// <summary>The section is empty and displays nothing.</summary>
    Empty = 0,

    /// <summary>Only literal text and, for text values, <c>@</c>.</summary>
    Literal = 1,

    /// <summary>The General format, possibly with literal text around it.</summary>
    General = 2,

    /// <summary>Digit placeholders, percent, scaling.</summary>
    Number = 3,

    /// <summary>Scientific notation (<c>E+</c>, <c>E-</c>).</summary>
    Scientific = 4,

    /// <summary>A fraction.</summary>
    Fraction = 5,

    /// <summary>A date, a time or an elapsed time.</summary>
    DateTime = 6,
}

/// <summary>Options for one formatting call. The defaults are the 1900 date system, US English separators and an 11-character General.</summary>
/// <param name="DateSystem">How date serials are read.</param>
/// <param name="Culture">Decimal and group separators; <see langword="null"/> means <see cref="NumberFormatCulture.EnglishUnitedStates"/>. A <c>[$-xxxx]</c> tag in the code selects names and calendar for dates, not separators.</param>
/// <param name="GeneralWidth">The width in characters that General fits a number into (Excel's default column shows 11). Excluding the sign.</param>
/// <param name="MaxWidth">The width the caller has to draw in. 0 means unlimited; otherwise a number that would need more characters returns <see cref="FormatStatus.DoesNotFit"/> and as many <c>#</c> as fit.</param>
public readonly record struct FormatOptions(
    DateSystem DateSystem = DateSystem.Date1900,
    NumberFormatCulture? Culture = null,
    int GeneralWidth = 11,
    int MaxWidth = 0);

/// <summary>The outcome of a formatting call.</summary>
public enum FormatStatus
{
    /// <summary>The text was produced.</summary>
    Ok = 0,

    /// <summary>The destination span was too small; nothing useful was written.</summary>
    DestinationTooSmall = 1,

    /// <summary>A date or time format met a negative number or a serial past 9999-12-31; Excel shows a row of <c>#</c>.</summary>
    DateOutOfRange = 2,

    /// <summary>The caller's <see cref="FormatOptions.MaxWidth"/> is too small; the output is <c>#</c> characters.</summary>
    DoesNotFit = 3,

    /// <summary>The value was NaN or infinite; Excel cannot hold these (the output is <c>#NUM!</c>).</summary>
    InvalidNumber = 4,

    /// <summary>The value does not fit what the format can show (a fraction whose numerator exceeds 32 bits); Excel shows a row of <c>#</c>.</summary>
    NotRepresentable = 5,
}

/// <summary>The result of <see cref="NumberFormat.TryFormat(double, Span{char}, FormatOptions)"/>: the status, the length written and the layout hints.</summary>
/// <param name="Status">Whether the text was produced.</param>
/// <param name="Length">Characters written.</param>
/// <param name="Color">The section's colour, if it has one.</param>
/// <param name="FillPosition">Index in the output where a <c>*c</c> fill expands to the cell width, or -1.</param>
/// <param name="FillCharacter">The character to repeat at <paramref name="FillPosition"/>.</param>
public readonly record struct FormatResult(
    FormatStatus Status, int Length, NumberFormatColor? Color = null, int FillPosition = -1, char FillCharacter = '\0');

/// <summary>One section of a format code, split on <c>;</c>.</summary>
public sealed class NumberFormatSection
{
    internal NumberFormatSection(string source, NumberFormatSectionKind kind, Element[] elements)
    {
        Source = source;
        Kind = kind;
        Elements = elements;
    }

    /// <summary>The section's code text.</summary>
    public string Source { get; }

    /// <summary>What the section does with a number.</summary>
    public NumberFormatSectionKind Kind { get; }

    /// <summary>The explicit condition, or <see langword="null"/>.</summary>
    public NumberFormatCondition? Condition { get; internal set; }

    /// <summary>The colour, or <see langword="null"/>.</summary>
    public NumberFormatColor? Color { get; internal set; }

    /// <summary>The language identifier from a <c>[$-xxxx]</c> tag, or 0 when none.</summary>
    public int LocaleId { get; internal set; }

    /// <summary>The Windows calendar identifier from the high bits of a locale tag (3 Japanese, 4 Minguo, 7 Thai Buddhist), or 0.</summary>
    public int CalendarId { get; internal set; }

    /// <summary>1 to 3 for <c>[DBNum1]</c> to <c>[DBNum3]</c>, or 0.</summary>
    public int DbNum { get; internal set; }

    /// <summary>Whether the section contains <c>@</c>.</summary>
    public bool ContainsText { get; internal set; }

    /// <summary>Whether the section has a date code.</summary>
    public bool HasDateCodes { get; internal set; }

    /// <summary>Whether the section has a time code (hour, minute, second, AM/PM).</summary>
    public bool HasTimeCodes { get; internal set; }

    /// <summary>Whether a literal currency symbol or a currency tag is present.</summary>
    public bool HasCurrency { get; internal set; }

    /// <summary>Whether a <c>%</c> is present.</summary>
    public bool HasPercent { get; internal set; }

    /// <summary>Whether a <c>*</c> fill is present.</summary>
    public bool HasFill { get; internal set; }

    /// <summary>The number of digit placeholders left of the decimal point (or of the whole part of a fraction).</summary>
    public int IntegerPlaceholders { get; internal set; }

    /// <summary>The number of digit placeholders right of the decimal point.</summary>
    public int FractionPlaceholders { get; internal set; }

    /// <summary>Whether the section groups thousands.</summary>
    public bool UsesGrouping { get; internal set; }

    internal Element[] Elements { get; }

    internal int ScaleCommas { get; set; }

    internal int PercentCount { get; set; }

    internal bool HasPoint { get; set; }

    internal int ExponentDigits { get; set; }

    internal int ExponentIndex { get; set; } = -1;

    internal int NumeratorStart { get; set; } = -1;

    internal int SlashIndex { get; set; } = -1;

    internal int DenominatorPlaceholders { get; set; }

    internal int FixedDenominator { get; set; }

    internal int FractionSecondDigits { get; set; } = -1;

    internal bool HasAmPm { get; set; }

    internal EraStyle? Era { get; set; }
}

internal enum EK : byte
{
    Literal,
    Zero,
    Hash,
    Q,
    Point,
    Comma,
    Percent,
    Slash,
    Exp,
    At,
    General,
    Fill,
    Digit,
    Year,
    Month,
    Day,
    Hour,
    Minute,
    Second,
    ElapsedHour,
    ElapsedMinute,
    ElapsedSecond,
    AmPm,
    FractionSecond,
    EraYear,
    EraName,
    BuddhistYear,
}

internal readonly record struct Element(EK Kind, int N = 0, string? Text = null, char Ch = '\0');

/// <summary>
/// A compiled Excel number-format code: up to four sections with conditions, colours and locale tags, parsed once and immutable.
/// Formatting a value allocates nothing beyond the result string, and <see cref="TryFormat(double, Span{char}, FormatOptions)"/> allocates nothing.
/// </summary>
/// <remarks>
/// The language and its rules are written down in <c>docs/number-formats.md</c>. Instances are safe to share between threads.
/// </remarks>
public sealed partial class NumberFormat
{
    private readonly NumberFormatSection[] _sections;

    internal NumberFormat(string code, NumberFormatSection[] sections)
    {
        Code = code;
        _sections = sections;
        Category = DeriveCategory();
    }

    /// <summary>The General format.</summary>
    public static NumberFormat General { get; } = Parse("General");

    /// <summary>The format code this instance was parsed from.</summary>
    public string Code { get; }

    /// <summary>The sections, in order.</summary>
    public IReadOnlyList<NumberFormatSection> Sections => _sections;

    /// <summary>What the format displays, taken from its first section that applies to numbers.</summary>
    public NumberFormatCategory Category { get; }

    /// <summary>Whether numbers are shown as dates or times (any numeric section has a date or time code).</summary>
    public bool IsDateTime => Category is NumberFormatCategory.Date or NumberFormatCategory.Time or NumberFormatCategory.DateTime;

    /// <summary>
    /// Parses a format code.
    /// </summary>
    /// <param name="code">The code; null or empty means General.</param>
    /// <returns>The compiled format.</returns>
    /// <exception cref="OfficeFormatException">The code is invalid; the defect code starts with <c>numfmt.</c> and the message names the position.</exception>
    public static NumberFormat Parse(string? code)
    {
        if (TryParse(code, out NumberFormat? f, out NumberFormatDefect? d))
        {
            return f;
        }

        throw new OfficeFormatException(Diagnostics.DefectKind.Value, d.Code, d.Message + " (position " + d.Position.ToString(CultureInfo.InvariantCulture) + ")");
    }

    /// <summary>Parses a format code without throwing.</summary>
    /// <param name="code">The code; null or empty means General.</param>
    /// <param name="format">The compiled format when the method returns <see langword="true"/>.</param>
    /// <param name="defect">Why the code is invalid when the method returns <see langword="false"/>.</param>
    /// <returns>Whether the code is valid.</returns>
    public static bool TryParse(string? code, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NumberFormat? format, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out NumberFormatDefect? defect) =>
        FormatParser.TryParse(code ?? string.Empty, out format, out defect);

    /// <summary>Returns the text of a built-in format ID, or <see langword="null"/> when the ID has no definition.</summary>
    /// <param name="id">The <c>numFmtId</c> (0 to 49 and the East Asian IDs 50 to 58).</param>
    /// <param name="culture">The culture for the locale-dependent IDs (5 to 8, 14, 22, 41 to 44); <see langword="null"/> means US English.</param>
    /// <returns>The format code, or <see langword="null"/>.</returns>
    public static string? GetBuiltInCode(int id, NumberFormatCulture? culture = null) => BuiltInFormats.GetCode(id, culture ?? NumberFormatCulture.EnglishUnitedStates);

    /// <summary>Returns the compiled built-in format for an ID.</summary>
    /// <param name="id">The <c>numFmtId</c>.</param>
    /// <param name="culture">The culture for the locale-dependent IDs; <see langword="null"/> means US English.</param>
    /// <returns>The compiled format, or <see langword="null"/> when the ID has no definition.</returns>
    public static NumberFormat? GetBuiltIn(int id, NumberFormatCulture? culture = null)
    {
        string? code = GetBuiltInCode(id, culture);
        return code is null ? null : Parse(code);
    }

    /// <summary>Returns the built-in ID whose US English code equals <paramref name="code"/>.</summary>
    /// <param name="code">A format code.</param>
    /// <returns>The ID (0 to 49), or -1 when the code is not built in.</returns>
    public static int FindBuiltInId(string code) => BuiltInFormats.FindId(code);

    /// <summary>The text of the <c>CELL("format")</c> result for this format: <c>G</c>, <c>F0</c>, <c>,2</c>, <c>C0</c>, <c>P2</c>, <c>S2</c>, <c>D1</c> to <c>D9</c> and the sign suffixes.</summary>
    /// <returns>The code; <c>G</c> for formats Excel has no code for.</returns>
    public string GetCellFormatCode() => CellFormatCodes.For(this);

    internal NumberFormatSection[] SectionArray => _sections;

    private NumberFormatCategory DeriveCategory()
    {
        NumberFormatSection? s = null;
        foreach (NumberFormatSection x in _sections)
        {
            if (x.Kind != NumberFormatSectionKind.Empty && !(x.Kind == NumberFormatSectionKind.Literal && x.ContainsText))
            {
                s = x;
                break;
            }
        }

        s ??= _sections[0];
        return s.Kind switch
        {
            NumberFormatSectionKind.General => NumberFormatCategory.General,
            NumberFormatSectionKind.DateTime => s.HasDateCodes && s.HasTimeCodes ? NumberFormatCategory.DateTime : s.HasDateCodes ? NumberFormatCategory.Date : NumberFormatCategory.Time,
            NumberFormatSectionKind.Scientific => NumberFormatCategory.Scientific,
            NumberFormatSectionKind.Fraction => NumberFormatCategory.Fraction,
            NumberFormatSectionKind.Number => s.HasPercent ? NumberFormatCategory.Percentage
                : s.HasCurrency ? (s.HasFill ? NumberFormatCategory.Accounting : NumberFormatCategory.Currency)
                : s.HasFill ? NumberFormatCategory.Accounting : NumberFormatCategory.Number,
            NumberFormatSectionKind.Literal => s.ContainsText ? NumberFormatCategory.Text : NumberFormatCategory.Custom,
            _ => _sections.Length == 1 && _sections[0].ContainsText ? NumberFormatCategory.Text : NumberFormatCategory.Custom,
        };
    }
}
