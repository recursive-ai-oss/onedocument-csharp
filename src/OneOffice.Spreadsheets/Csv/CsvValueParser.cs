using System.Globalization;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Spreadsheets.Csv;

/// <summary>What a field of text turned into.</summary>
public enum CsvValueKind : byte
{
    /// <summary>Nothing (an empty field).</summary>
    Empty = 0,

    /// <summary>Text, as read.</summary>
    Text = 1,

    /// <summary>A number in General format.</summary>
    Number = 2,

    /// <summary>A date; the number is the whole-day serial.</summary>
    Date = 3,

    /// <summary>A date and a time of day; the number is the serial with a fraction.</summary>
    DateTime = 4,

    /// <summary>A time of day; the number is the fraction of a day.</summary>
    Time = 5,

    /// <summary>A percentage; the number is the fraction (5% is 0.05).</summary>
    Percent = 6,

    /// <summary>TRUE or FALSE.</summary>
    Boolean = 7,

    /// <summary>An error value such as <c>#N/A</c>.</summary>
    Error = 8,
}

/// <summary>The result of reading a field as a value.</summary>
public readonly struct CsvValue
{
    internal CsvValue(CsvValueKind kind, double number = 0, string? formatCode = null, CellError error = default)
    {
        Kind = kind;
        Number = number;
        FormatCode = formatCode;
        Error = error;
    }

    /// <summary>Gets what the field is.</summary>
    public CsvValueKind Kind { get; }

    /// <summary>Gets the number of a number, date, time or percentage; 1 or 0 for a boolean; 0 otherwise.</summary>
    public double Number { get; }

    /// <summary>Gets the error of an <see cref="CsvValueKind.Error"/> value.</summary>
    public CellError Error { get; }

    /// <summary>Gets the number-format code Excel would give the cell (<c>m/d/yyyy</c>, <c>h:mm:ss</c>, <c>0.00%</c>…); null for General.</summary>
    public string? FormatCode { get; }

    /// <summary>Gets a value indicating whether the value is a boolean that is true.</summary>
    public bool Boolean => Kind == CsvValueKind.Boolean && Number != 0;
}

/// <summary>How <see cref="CsvValueParser"/> reads text as values.</summary>
public sealed class CsvValueOptions
{
    /// <summary>
    /// Gets the culture whose decimal and group separators, month names and day/month/year order are used. The default is English (United
    /// States), which is what Excel does on an English system: month first, <c>.</c> decimal, <c>,</c> groups. The library's own culture
    /// table is used, so the result does not depend on the platform's globalization data.
    /// </summary>
    public NumberFormatCulture Culture { get; init; } = NumberFormatCulture.EnglishUnitedStates;

    /// <summary>Gets a value indicating whether anything but text is recognised. False keeps every field as text (or empty).</summary>
    public bool Infer { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a whole number written with a leading zero (<c>007</c>, a postcode) stays text. The default is true.
    /// Excel reads it as the number 7 and loses the zeros; set false to match.
    /// </summary>
    public bool PreserveLeadingZeros { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a whole number of more than 15 digits (a card number, an identifier) stays text. The default is true.
    /// Excel keeps 15 significant digits and replaces the rest with zeros; set false to match.
    /// </summary>
    public bool PreserveLongNumbers { get; init; } = true;

    /// <summary>Gets a value indicating whether dates and times are recognised. The default is true.</summary>
    public bool InferDates { get; init; } = true;

    /// <summary>
    /// Gets the year given to a date written without one (<c>1/2</c>, <c>Jan 5</c>, <c>5-Jan</c>). Excel uses the current year; this library
    /// never reads the clock, so the default 0 keeps such text as text. Set the year to match Excel.
    /// </summary>
    public int DefaultYear { get; init; }

    /// <summary>
    /// Gets the two-digit year at which the century changes: below it years are 20xx, from it 19xx. The default 50 is Windows' default
    /// window (1950 to 2049), which is what Excel uses unless the system setting was changed.
    /// </summary>
    public int TwoDigitYearPivot { get; init; } = 50;

    /// <summary>Gets the date system dates are numbered in. The default is the 1900 system.</summary>
    public DateSystem DateSystem { get; init; } = DateSystem.Date1900;
}

/// <summary>
/// Reads the text of a field as Excel does when it opens a CSV file: numbers (signs, group separators, currency, parentheses, exponent,
/// percent), booleans, error values, dates and times. It allocates nothing per call. What is not one of these is text.
/// </summary>
/// <remarks>
/// Differences from Excel, on purpose: a date without a year (<c>1/2</c>, <c>Jan 5</c>) stays text because Excel would use today's year and
/// the result would change with the clock; whole numbers with leading zeros or more than 15 digits stay text unless the options say otherwise.
/// Text such as <c>1-2</c> or <c>3/4</c> is text for the same reason.
/// </remarks>
public sealed class CsvValueParser
{
    private const int MaxDigits = 320;

    private readonly CsvValueOptions _options;
    private readonly char _decimal;
    private readonly char _group;
    private readonly string _currency;
    private readonly bool _currencyAfter;
    private readonly string _currencyWhole;
    private readonly string _currencyDecimals;
    private readonly string _euroWhole;
    private readonly string _euroDecimals;
    private readonly string[] _months;
    private readonly string[] _monthsAbbreviated;
    private readonly int _order; // 0 = month day year, 1 = day month year, 2 = year month day
    private readonly bool _dotDate;
    private readonly string _numericDateCode;
    private readonly string _dateTimeCode;

    /// <summary>Creates a parser.</summary>
    /// <param name="options">The options; null for the defaults.</param>
    public CsvValueParser(CsvValueOptions? options = null)
    {
        _options = options ?? new CsvValueOptions();
        NumberFormatCulture c = _options.Culture;
        _decimal = c.DecimalSeparator.Length == 1 ? c.DecimalSeparator[0] : '.';
        _group = c.GroupSeparator.Length == 1 ? c.GroupSeparator[0] : ',';
        _currency = c.CurrencySymbol.Length == 0 || c.CurrencySymbol == "\u00A4" ? "$" : c.CurrencySymbol;
        _currencyAfter = c.CurrencyAfterNumber;
        string q = _currency == "$" ? "$" : "\"" + _currency + "\"";
        _currencyWhole = q + "#,##0_);[Red](" + q + "#,##0)";
        _currencyDecimals = q + "#,##0.00_);[Red](" + q + "#,##0.00)";
        _euroWhole = "[$\u20AC-2] #,##0;[Red]-[$\u20AC-2] #,##0";
        _euroDecimals = "[$\u20AC-2] #,##0.00;[Red]-[$\u20AC-2] #,##0.00";
        _months = new string[12];
        _monthsAbbreviated = new string[12];
        for (int k = 0; k < 12; k++)
        {
            _months[k] = c.GetMonthName(k + 1, abbreviated: false);
            _monthsAbbreviated[k] = c.GetMonthName(k + 1, abbreviated: true);
        }

        string pattern = c.ShortDatePattern.ToLowerInvariant(); // the table writes month as mm, Excel style
        int d = pattern.IndexOf('d');
        int m = pattern.IndexOf('m');
        int y = pattern.IndexOf('y');
        _order = y >= 0 && y < d && y < m ? 2 : d >= 0 && d < m ? 1 : 0;
        _numericDateCode = c.Lcid == 0x0409 ? "m/d/yyyy" : c.ShortDatePattern;
        _dateTimeCode = _numericDateCode + " h:mm";
        _dotDate = pattern.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>Reads text as a value.</summary>
    /// <param name="text">The field, unquoted.</param>
    /// <returns>The value; <see cref="CsvValueKind.Text"/> with no number when nothing else fits.</returns>
    public CsvValue Parse(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
        {
            return default;
        }

        if (!_options.Infer)
        {
            return new CsvValue(CsvValueKind.Text);
        }

        // Booleans and errors must be the whole field: Excel keeps " TRUE" as text.
        char c0 = text[0];
        if (c0 == '#')
        {
            return CellErrors.TryParse(text, out CellError e) && e != CellError.External ? new CsvValue(CsvValueKind.Error, error: e) : new CsvValue(CsvValueKind.Text);
        }

        if (c0 is 'T' or 't' or 'F' or 'f')
        {
            if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                return new CsvValue(CsvValueKind.Boolean, 1);
            }

            if (text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            {
                return new CsvValue(CsvValueKind.Boolean, 0);
            }
        }

        ReadOnlySpan<char> s = text.Trim(' ');
        if (s.Length == 0)
        {
            return new CsvValue(CsvValueKind.Text);
        }

        if (TryParseNumber(s, out CsvValue number))
        {
            return number;
        }

        if (_options.InferDates && TryParseDateTime(s, text[0] == ' ', out CsvValue date))
        {
            return date;
        }

        return new CsvValue(CsvValueKind.Text);
    }

    // ---- numbers ----

    // Counts the signs and spaces at the front; flips says whether an odd number of them are minus signs.
    private static int SkipSigns(ReadOnlySpan<char> s, out bool flips)
    {
        int i = 0;
        flips = false;
        while (i < s.Length && s[i] is '+' or '-' or ' ')
        {
            flips ^= s[i] == '-';
            i++;
        }

        return i;
    }

    // Excel's English reading takes $ and the euro sign as currency whatever the culture says; the culture's own symbol too.
    private int CurrencyLength(ReadOnlySpan<char> s, out bool euro)
    {
        euro = false;
        if (s.Length == 0)
        {
            return 0;
        }

        if (_currency.Length > 0 && s.StartsWith(_currency, StringComparison.Ordinal))
        {
            euro = _currency == "\u20AC";
            return _currency.Length;
        }

        if (s[0] == '$')
        {
            return 1;
        }

        euro = s[0] == '\u20AC';
        return euro ? 1 : 0;
    }

    // What Excel does with a number written in a text file, found by opening generated files in Excel (see docs/csv.md).
    private bool TryParseNumber(ReadOnlySpan<char> s, out CsvValue value)
    {
        value = default;
        bool negative = false;
        bool parentheses = false;
        bool percent = false;
        bool currency = false;
        string? whole = _currencyWhole;
        string? fraction = _currencyDecimals;

        // Excel reads the front of a number like an expression: any run of signs (each - flips), a currency symbol, signs again.
        int skipped = SkipSigns(s, out bool flips);
        negative ^= flips;
        s = s[skipped..];
        int symbol = CurrencyLength(s, out bool euro);
        if (symbol > 0)
        {
            currency = true;
            (whole, fraction) = euro ? (_euroWhole, _euroDecimals) : (whole, fraction);
            s = s[symbol..];
            bool charsBefore = skipped > 0; // signs or spaces ahead of the symbol
            skipped = SkipSigns(s, out flips);
            if (skipped > 0 && charsBefore && s[..skipped].IndexOfAny('+', '-') >= 0)
            {
                return false; // -$-5 and $--5 are text; $-5 and -$5 are numbers
            }

            if (s[..skipped].Count('-') + s[..skipped].Count('+') > 1)
            {
                return false;
            }

            negative ^= flips;
            s = s[skipped..];
        }

        if (s.Length > 1 && s[0] == '%')
        {
            percent = true;
            s = s[1..].TrimStart(' ');
        }

        if (s.Length >= 2 && s[0] == '(')
        {
            // Parentheses make a negative number; a sign inside them is not a number. A percent sign may follow the closing one.
            int close = s.LastIndexOf(')');
            if (close < 0)
            {
                return false;
            }

            ReadOnlySpan<char> after = s[(close + 1)..].Trim(' ');
            if (after.Length > 0)
            {
                if (after is not "%" || percent)
                {
                    return false;
                }

                percent = true;
            }

            s = s[1..close].Trim(' ');
            if (!currency && (symbol = CurrencyLength(s, out euro)) > 0)
            {
                currency = true;
                (whole, fraction) = euro ? (_euroWhole, _euroDecimals) : (whole, fraction);
                s = s[symbol..].TrimStart(' ');
            }

            if (s.Length > 0 && s[0] is '+' or '-')
            {
                return false;
            }

            parentheses = true;
        }
        else if (_currencyAfter && _currency.Length > 0 && s.EndsWith(_currency, StringComparison.Ordinal))
        {
            currency = true;
            s = s[..^_currency.Length].TrimEnd(' ');
        }

        if (s.Length > 1 && s[^1] == '%')
        {
            if (percent)
            {
                return false;
            }

            percent = true;
            s = s[..^1].TrimEnd(' ');
        }

        if (currency && percent)
        {
            return false;
        }

        if (s.Length == 0)
        {
            return false;
        }

        // The body: digits, group separators, one decimal separator, an exponent.
        Span<char> clean = stackalloc char[MaxDigits + 8]; // bodies longer than MaxDigits are refused below
        int n = 0;
        int intDigits = 0;
        int fractionDigits = 0;
        int significant = 0;
        int groupDigits = 0; // digits in the current group; -1 until a group separator is seen
        int groups = 0;
        bool hasDecimal = false;
        bool hasExponent = false;
        bool anyNonZero = false;
        int i = 0;
        groupDigits = -1;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c is >= '\u0660' and <= '\u0669')
            {
                c = (char)('0' + (c - '\u0660')); // Excel reads Arabic-Indic digits as digits
            }

            if (c is >= '0' and <= '9')
            {
                if (i >= MaxDigits)
                {
                    return false;
                }

                if (c != '0')
                {
                    anyNonZero = true;
                }

                if (anyNonZero)
                {
                    significant++;
                }

                // Excel keeps 15 significant digits: later integer digits become zeros, later fraction digits are dropped.
                if (significant > 15)
                {
                    if (!hasDecimal)
                    {
                        clean[n++] = '0';
                    }
                }
                else
                {
                    clean[n++] = c;
                }

                if (!hasDecimal)
                {
                    intDigits++;
                    if (groupDigits >= 0)
                    {
                        groupDigits++;
                    }
                }
                else
                {
                    fractionDigits++;
                }
            }
            else if ((c == _group || (c == ' ' && _group is '\u00A0' or '\u202F')) && !hasDecimal && !hasExponent)
            {
                // Groups: one to three digits first (no leading zero), then three or more in each later group.
                if (groups == 0 ? intDigits < 1 || clean[0] == '0' : groupDigits < 3)
                {
                    return false;
                }

                groups++;
                groupDigits = 0;
            }
            else if (c == _decimal && !hasDecimal)
            {
                if (groups > 0 && groupDigits < 3)
                {
                    return false;
                }

                hasDecimal = true;
                groupDigits = -1;
                clean[n++] = '.';
            }
            else if (c is 'e' or 'E')
            {
                hasExponent = true;
                break;
            }
            else
            {
                return false;
            }
        }

        if (groups > 0 && !hasDecimal && groupDigits < 3)
        {
            return false;
        }

        if (intDigits + fractionDigits == 0)
        {
            return false;
        }

        if (hasExponent)
        {
            int j = i + 1;
            if (j >= s.Length)
            {
                return false;
            }

            clean[n++] = 'E';
            if (s[j] is '+' or '-')
            {
                clean[n++] = s[j];
                j++;
            }

            int expDigits = 0;
            for (; j < s.Length; j++)
            {
                if (s[j] is < '0' or > '9' || expDigits >= 4)
                {
                    return false;
                }

                clean[n++] = s[j];
                expDigits++;
            }

            if (expDigits == 0)
            {
                return false;
            }
        }

        if (!hasDecimal && !hasExponent && !percent && groups == 0)
        {
            if (_options.PreserveLeadingZeros && intDigits > 1 && s[0] == '0')
            {
                return false;
            }

            if (_options.PreserveLongNumbers && significant > 15)
            {
                return false;
            }
        }

        if (!double.TryParse(clean[..n], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
        {
            return false;
        }

        // Excel holds 2.2251E-308 to 9.99999999999999E+307. Above that the text stays text; below it, down to 1E-308, the number is 0;
        // a nonzero text that parses to nothing, or to a magnitude past 1E-308, stays text.
        double magnitude = Math.Abs(v);
        if (double.IsInfinity(v) || magnitude > 9.99999999999999E+307)
        {
            return false;
        }

        if (anyNonZero && (magnitude == 0 || Math.Floor(Math.Log10(magnitude)) < -308))
        {
            return false;
        }

        if (magnitude < 2.2251E-308)
        {
            v = 0;
        }

        if (negative || parentheses)
        {
            v = -v;
        }

        if (percent && hasExponent)
        {
            value = new CsvValue(CsvValueKind.Number, v / 100, "0.00E+00");
        }
        else if (percent)
        {
            value = new CsvValue(CsvValueKind.Percent, v / 100, fractionDigits > 0 ? "0.00%" : "0%");
        }
        else
        {
            string? code = hasExponent ? "0.00E+00" : currency ? hasDecimal ? fraction : whole
                : groups > 0 ? hasDecimal ? "#,##0.00" : "#,##0" : null;
            value = new CsvValue(CsvValueKind.Number, v, code);
        }

        return true;
    }

    // ---- dates and times ----
    // What Excel accepts was found by opening generated files in Excel; the forms are listed in docs/csv.md.

    private const int MaxTokens = 4;

    private bool TryParseDateTime(ReadOnlySpan<char> s, bool leadingSpace, out CsvValue value)
    {
        value = default;
        if (s.Length < 2 || s.Length > 64 || leadingSpace)
        {
            return false; // Excel keeps " 1/2/2020" as text
        }

        // A time of day starts at the number before the first colon, if spaces (or the start) come before it.
        ReadOnlySpan<char> datePart = s;
        ReadOnlySpan<char> timePart = default;
        int colon = s.IndexOf(':');
        if (colon >= 0)
        {
            int k = colon;
            while (k > 0 && s[k - 1] is >= '0' and <= '9')
            {
                k--;
            }

            if (k == colon)
            {
                return false;
            }

            if (k == 0)
            {
                timePart = s;
                datePart = default;
            }
            else if (s[k - 1] == ' ')
            {
                datePart = s[..(k - 1)].TrimEnd(' ');
                timePart = s[k..];
            }
            else
            {
                return false;
            }
        }

        int dateSerial = 0;
        string? dateCode = null;
        bool hasDate = datePart.Length > 0;
        if (hasDate && !TryParseDate(datePart, out dateSerial, out dateCode))
        {
            return false;
        }

        if (timePart.Length == 0)
        {
            value = new CsvValue(CsvValueKind.Date, dateSerial, dateCode);
            return true;
        }

        if (!TryParseTime(timePart, out double seconds, out int hours, out bool hasSeconds, out bool fraction, out bool ampm, out bool overflow))
        {
            return false;
        }

        double days = seconds / 86400.0;
        if (!hasDate)
        {
            // Excel shows a duration of a day or more as elapsed time, and General when minutes or seconds ran past 59.
            string? code = overflow ? null : fraction ? "mm:ss.0" : days >= 1 ? hours >= 24 ? "[h]:mm:ss" : null : ampm ? hasSeconds ? "h:mm:ss AM/PM" : "h:mm AM/PM" : hasSeconds ? "h:mm:ss" : "h:mm";
            value = new CsvValue(CsvValueKind.Time, days, code);
        }
        else
        {
            string? code = overflow ? null : fraction ? "mm:ss.0" : days >= 1 ? null : _dateTimeCode;
            value = new CsvValue(CsvValueKind.DateTime, dateSerial + days, code);
        }

        return true;
    }

    // h:mm, h:mm:ss, h:mm:ss.fff, h:, each optionally followed by spaces and AM, PM, A or P. Returns the seconds since midnight.
    // Minutes and seconds may run past 59 (1:99 is 2:39); fractions of a second keep three digits.
    private static bool TryParseTime(ReadOnlySpan<char> t, out double totalSeconds, out int hours, out bool hasSeconds, out bool fraction, out bool ampm, out bool overflow)
    {
        totalSeconds = 0;
        hours = 0;
        hasSeconds = false;
        fraction = false;
        ampm = false;
        overflow = false;
        bool pm = false;
        int space = t.LastIndexOf(' ');
        if (space > 0)
        {
            ReadOnlySpan<char> marker = t[(space + 1)..];
            if (marker.Equals("AM", StringComparison.OrdinalIgnoreCase) || marker.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                ampm = true;
            }
            else if (marker.Equals("PM", StringComparison.OrdinalIgnoreCase) || marker.Equals("P", StringComparison.OrdinalIgnoreCase))
            {
                ampm = true;
                pm = true;
            }
            else
            {
                return false;
            }

            t = t[..space].TrimEnd(' ');
        }

        int pos = 0;
        if (!ReadInt(t, ref pos, 4, out int h) || pos >= t.Length || t[pos] != ':')
        {
            return false;
        }

        pos++;
        int m = 0;
        int sec = 0;
        double frac = 0;
        if (pos < t.Length)
        {
            if (!ReadInt(t, ref pos, 2, out m))
            {
                return false;
            }

            if (pos < t.Length)
            {
                if (t[pos] != ':')
                {
                    return false;
                }

                pos++;
                if (!ReadInt(t, ref pos, 2, out sec))
                {
                    return false;
                }

                hasSeconds = true;
                if (pos < t.Length && t[pos] == '.')
                {
                    pos++;
                    double scale = 0.1;
                    int digits = 0;
                    while (pos < t.Length && t[pos] is >= '0' and <= '9')
                    {
                        if (digits < 3)
                        {
                            frac += (t[pos] - '0') * scale;
                            scale /= 10;
                        }

                        digits++;
                        pos++;
                    }

                    fraction = digits > 0;
                }
            }
        }

        if (pos != t.Length)
        {
            return false;
        }

        if (ampm)
        {
            if (h is < 1 or > 12 && !(h == 0))
            {
                return false;
            }

            if (m > 59 || sec > 59)
            {
                return false; // Excel keeps "12:60 PM" as text
            }

            h = h % 12 + (pm ? 12 : 0);
        }

        overflow = m > 59 || sec > 59;
        hours = h;
        totalSeconds = h * 3600.0 + m * 60 + sec + frac;
        return true;
    }

    private static bool ReadInt(ReadOnlySpan<char> s, ref int pos, int maxDigits, out int value)
    {
        value = 0;
        int start = pos;
        while (pos < s.Length && pos - start < maxDigits && s[pos] is >= '0' and <= '9')
        {
            value = value * 10 + (s[pos] - '0');
            pos++;
        }

        return pos > start;
    }

    // Tokens of a date: numbers and month names, with the separators between them. A separator is spaces around one of / - ,
    // or spaces alone; Excel does not care which of / and - is used where, or about spaces next to them.
    private bool TryParseDate(ReadOnlySpan<char> s, out int serial, out string? code)
    {
        serial = 0;
        code = null;
        Span<int> vals = stackalloc int[MaxTokens]; // a fifth token fails the parse
        Span<int> digs = stackalloc int[MaxTokens]; // digits of a number, 0 for a month name
        Span<char> seps = stackalloc char[MaxTokens]; // separator after each token: '/', '-', ',', '.', or ' ' for spaces alone
        int n = 0;
        int i = 0;
        while (i < s.Length)
        {
            if (n == MaxTokens)
            {
                return false;
            }

            if (s[i] is >= '0' and <= '9')
            {
                int start = i;
                int v = 0;
                while (i < s.Length && s[i] is >= '0' and <= '9' && i - start < 6)
                {
                    v = v * 10 + (s[i] - '0');
                    i++;
                }

                vals[n] = v;
                digs[n] = i - start;
            }
            else if (char.IsLetter(s[i]))
            {
                int start = i;
                while (i < s.Length && char.IsLetter(s[i]))
                {
                    i++;
                }

                int month = MonthOf(s[start..i]);
                if (month == 0)
                {
                    return false;
                }

                vals[n] = month;
                digs[n] = 0;
            }
            else
            {
                return false;
            }

            if (i < s.Length)
            {
                int spaces = 0;
                while (i < s.Length && s[i] == ' ')
                {
                    i++;
                    spaces++;
                }

                if (i < s.Length && s[i] is '/' or '-' or ',' or '.')
                {
                    char c = s[i];
                    if (c == '.' && !_dotDate)
                    {
                        return false;
                    }

                    seps[n] = c == ',' && !(i + 1 < s.Length && s[i + 1] == ' ') ? ';' : c; // "Jan 2,2020" is text
                    i++;
                    while (i < s.Length && s[i] == ' ')
                    {
                        i++;
                    }
                }
                else if (spaces > 0)
                {
                    seps[n] = ' ';
                }
                else
                {
                    return false;
                }

                if (i >= s.Length)
                {
                    return false; // a trailing separator
                }
            }

            n++;
        }

        int names = 0;
        for (int k = 0; k < n; k++)
        {
            if (digs[k] == 0)
            {
                names++;
            }
        }

        int y;
        int m;
        int d;
        int yearDigits;
        if (names == 0)
        {
            // Numeric: / and - mix freely; a dot only where the culture writes dates with dots (and then only dots).
            bool dots = seps[0] == '.';
            for (int k = 0; k < n - 1; k++)
            {
                if (dots ? seps[k] != '.' : seps[k] is not ('/' or '-'))
                {
                    return false;
                }
            }

            if (n == 3)
            {
                if (digs[0] == 4)
                {
                    y = vals[0];
                    m = vals[1];
                    d = vals[2];
                    yearDigits = 4;
                    if (digs[1] > 2 || digs[2] > 2 || dots)
                    {
                        return false;
                    }
                }
                else
                {
                    (int a, int b, int c) = _order switch
                    {
                        1 => (vals[1], vals[0], vals[2]),
                        2 => (vals[1], vals[2], vals[0]),
                        _ => (vals[0], vals[1], vals[2]),
                    };
                    int yi = _order == 2 ? 0 : 2;
                    if (_order == 2 && digs[0] != 4)
                    {
                        return false;
                    }

                    if (digs[0] > 2 && yi != 0 || digs[1] > 2 || (yi == 2 && digs[2] > 4) || digs[yi] == 3)
                    {
                        return false;
                    }

                    m = a;
                    d = b;
                    y = c;
                    yearDigits = digs[yi];
                }
            }
            else if (n == 2 && _options.DefaultYear > 0 && digs[0] <= 2 && digs[1] <= 2)
            {
                // A month and a day: Excel uses the current year, which would make the result depend on the clock.
                (m, d) = _order == 1 ? (vals[1], vals[0]) : (vals[0], vals[1]);
                code = "d-mmm";
                return ExcelDate.TryToSerial(_options.DefaultYear, m, d, _options.DateSystem, out serial);
            }
            else
            {
                return false;
            }

            if (!YearOf(ref y, yearDigits) || !ExcelDate.TryToSerial(y, m, d, _options.DateSystem, out serial))
            {
                return false;
            }

            code = _numericDateCode;
            return true;
        }

        if (names != 1)
        {
            return false;
        }

        // 2-Jan-2020, 2 Jan 20, 5/May/2020: day, month name, year, joined by - / or spaces
        if (n == 3 && digs[1] == 0 && digs[0] is >= 1 and <= 2 && digs[2] is 2 or 4 && seps[0] is '-' or '/' or ' ' or '.' && seps[1] is '-' or '/' or ' ')
        {
            (d, m, y, yearDigits) = (vals[0], vals[1], vals[2], digs[2]);
        }
        else if (n == 3 && digs[0] == 0 && seps[0] == ' ' && seps[1] == ',' && digs[1] is >= 1 and <= 2 && digs[2] is 2 or 4)
        {
            // Jan 2, 2020
            (m, d, y, yearDigits) = (vals[0], vals[1], vals[2], digs[2]);
        }
        else if (n == 2 && digs[0] == 0 && digs[1] is 2 or 4 && seps[0] is ' ' or '-')
        {
            // Jan 2020, Jan-2020: the first of the month. A two-digit number is a day when it can be one (Jan 20 is the 20th of the
            // current year, which needs DefaultYear) and otherwise a year (Jan 32 is January 2032).
            m = vals[0];
            y = vals[1];
            if (digs[1] == 2 && y <= DaysIn(m))
            {
                if (_options.DefaultYear <= 0)
                {
                    return false; // the 20th, or the year 2020: Excel decides with today's date
                }

                if (ExcelDate.TryToSerial(_options.DefaultYear, m, y, _options.DateSystem, out serial))
                {
                    code = "d-mmm";
                    return true;
                }
            }

            return FirstOfMonth(m, y, digs[1], out serial, out code);
        }
        else if (n == 2 && digs[0] == 0 && digs[1] == 1 && seps[0] is ' ' or '-' && _options.DefaultYear > 0)
        {
            m = vals[0];
            code = "d-mmm";
            return ExcelDate.TryToSerial(_options.DefaultYear, m, vals[1], _options.DateSystem, out serial);
        }
        else if (n == 2 && digs[1] == 0 && digs[0] is >= 1 and <= 2 && seps[0] is ' ' or '-' && _options.DefaultYear > 0)
        {
            // 2-Jan: no year
            code = "d-mmm";
            return ExcelDate.TryToSerial(_options.DefaultYear, vals[1], vals[0], _options.DateSystem, out serial);
        }
        else
        {
            return false;
        }

        if (!YearOf(ref y, yearDigits) || !ExcelDate.TryToSerial(y, m, d, _options.DateSystem, out serial))
        {
            return false;
        }

        code = "d-mmm-yy";
        return true;
    }

    private static int DaysIn(int month) => month == 2 ? 29 : month is 4 or 6 or 9 or 11 ? 30 : 31;

    private bool FirstOfMonth(int month, int year, int digits, out int serial, out string? code)
    {
        code = "mmm-yy";
        serial = 0;
        return YearOf(ref year, digits) && ExcelDate.TryToSerial(year, month, 1, _options.DateSystem, out serial);
    }

    private bool YearOf(ref int year, int digits)
    {
        if (digits == 4)
        {
            return true;
        }

        if (digits is 1 or 2)
        {
            // Two-digit years: below the pivot are 20xx, from it 19xx (Windows' default window is 1950 to 2049).
            year += year < _options.TwoDigitYearPivot ? 2000 : 1900;
            return true;
        }

        return false;
    }

    // A month name is the English or culture name in full, its abbreviation, or any prefix of the full name of three or more letters
    // (Excel reads Janu, Marc and Septe).
    private int MonthOf(ReadOnlySpan<char> name)
    {
        if (name.Length < 3)
        {
            return 0;
        }

        for (int k = 0; k < 12; k++)
        {
            if (name.Length <= _months[k].Length && _months[k].AsSpan(0, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return k + 1;
            }

            if (name.Equals(_monthsAbbreviated[k], StringComparison.OrdinalIgnoreCase))
            {
                return k + 1;
            }
        }

        return 0;
    }
}
