using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Spreadsheets.Pivots;

/// <summary>The kinds of value a pivot cache holds (ECMA-376 Part 1 §18.10.1.x: <c>m</c>, <c>n</c>, <c>s</c>, <c>b</c>, <c>e</c>, <c>d</c>).</summary>
internal enum PivotKind : byte
{
    Blank = 0,
    Number = 1,
    Text = 2,
    Boolean = 3,
    Error = 4,
    Date = 5,
}

/// <summary>
/// One value of a pivot cache. Text compares without regard to case, because a pivot cache lists "a" and "A" as one item; the first spelling seen
/// is kept. Dates are serial numbers in the workbook's date system, flagged so the cache writes them as <c>d</c> items.
/// </summary>
internal readonly struct PivotValue : IEquatable<PivotValue>
{
    public PivotValue(PivotKind kind, double number, string? text)
    {
        Kind = kind;
        Number = number;
        Text = text;
    }

    public static PivotValue Blank => default;

    public PivotKind Kind { get; }

    /// <summary>The number or date serial; 1 or 0 for a boolean.</summary>
    public double Number { get; }

    /// <summary>The text of a string, or the error text of an error.</summary>
    public string? Text { get; }

    public bool IsBlank => Kind == PivotKind.Blank;

    public bool IsNumeric => Kind is PivotKind.Number or PivotKind.Date;

    public static PivotValue FromNumber(double v) => new(PivotKind.Number, v, null);

    public static PivotValue FromDate(double v) => new(PivotKind.Date, v, null);

    public static PivotValue FromText(string v) => new(PivotKind.Text, 0, v);

    public static PivotValue FromBoolean(bool v) => new(PivotKind.Boolean, v ? 1 : 0, null);

    public static PivotValue FromError(string text) => new(PivotKind.Error, 0, text);

    public bool Equals(PivotValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            PivotKind.Blank => true,
            PivotKind.Text or PivotKind.Error => string.Equals(Text, other.Text, StringComparison.OrdinalIgnoreCase),
            _ => Number.Equals(other.Number),
        };
    }

    public override bool Equals(object? obj) => obj is PivotValue v && Equals(v);

    public override int GetHashCode() => Kind switch
    {
        PivotKind.Blank => 0,
        PivotKind.Text or PivotKind.Error => HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Text ?? string.Empty)),
        _ => HashCode.Combine(Kind, Number),
    };

    /// <summary>The label a pivot table shows for the item: text as it is, numbers in General, dates through the field's number format, blanks as <c>(blank)</c>.</summary>
    public string Label(DateSystem system)
    {
        switch (Kind)
        {
            case PivotKind.Blank:
                return "(blank)";
            case PivotKind.Text:
            case PivotKind.Error:
                return Text ?? string.Empty;
            case PivotKind.Boolean:
                return Number != 0 ? "TRUE" : "FALSE";
            case PivotKind.Date:
                return ExcelDate.TryToDateTime(Number, system, out DateTime d)
                    ? d.ToString(d.TimeOfDay == TimeSpan.Zero ? "M/d/yyyy" : "M/d/yyyy H:mm:ss", CultureInfo.InvariantCulture)
                    : Number.ToString("R", CultureInfo.InvariantCulture);
            default:
                return Number.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Orders items the way a pivot table sorts an axis: numbers and dates, then text, then booleans, then errors, then blanks.</summary>
    public static int Compare(PivotValue a, PivotValue b)
    {
        int ra = Rank(a.Kind);
        int rb = Rank(b.Kind);
        if (ra != rb)
        {
            return ra.CompareTo(rb);
        }

        return a.Kind switch
        {
            PivotKind.Text or PivotKind.Error => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase) is var c && c != 0 ? c : string.CompareOrdinal(a.Text, b.Text),
            PivotKind.Blank => 0,
            _ => a.Number.CompareTo(b.Number),
        };
    }

    private static int Rank(PivotKind k) => k switch
    {
        PivotKind.Number or PivotKind.Date => 0,
        PivotKind.Text => 1,
        PivotKind.Boolean => 2,
        PivotKind.Error => 3,
        _ => 4,
    };
}

/// <summary>Small XML and number helpers for writing pivot parts.</summary>
internal static class PivotXml
{
    public static string Esc(string s)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            string? rep = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\t' => "&#9;",
                '\n' => "&#10;",
                '\r' => "&#13;",
                _ => null,
            };
            if (rep is null && (c < 0x20 || c == 0xFFFE || c == 0xFFFF))
            {
                rep = string.Empty;
            }

            if (rep is not null && sb is null)
            {
                sb = new StringBuilder(s.Length + 16).Append(s, 0, i);
            }

            if (sb is not null)
            {
                sb.Append(rep ?? c.ToString());
            }
        }

        return sb?.ToString() ?? s;
    }

    public static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static bool IsInteger(double v) => v == Math.Floor(v) && Math.Abs(v) < 1e15;

    public static int IntAttr(string? s, int dflt) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : dflt;

    public static double DoubleAttr(string? s, double dflt) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : dflt;

    /// <summary>Reads an XML boolean attribute (<c>1</c>/<c>true</c>), with the schema default when absent.</summary>
    public static bool Bool(string? s, bool dflt) => s switch { null => dflt, "1" or "true" => true, "0" or "false" => false, _ => dflt };

    public static string DateText(double serial, DateSystem system) =>
        ExcelDate.TryToDateTime(serial, system, out DateTime d) ? d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : "1900-01-01T00:00:00";

    public static bool TryParseDate(string? s, DateSystem system, out double serial)
    {
        serial = 0;
        return s is not null && DateTime.TryParseExact(s, ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d)
            && ExcelDate.TryToSerial(d, system, out serial);
    }

    /// <summary>The XML for one cache item (<c>m</c>, <c>n</c>, <c>s</c>, <c>b</c>, <c>e</c>, <c>d</c>), with the element prefix.</summary>
    public static string Item(string pc, PivotValue v, DateSystem system) => v.Kind switch
    {
        PivotKind.Blank => "<" + pc + "m/>",
        PivotKind.Number => "<" + pc + "n v=\"" + Num(v.Number) + "\"/>",
        PivotKind.Text => "<" + pc + "s v=\"" + Esc(v.Text ?? string.Empty) + "\"/>",
        PivotKind.Boolean => "<" + pc + "b v=\"" + (v.Number != 0 ? "1" : "0") + "\"/>",
        PivotKind.Error => "<" + pc + "e v=\"" + Esc(v.Text ?? "#N/A") + "\"/>",
        _ => "<" + pc + "d v=\"" + DateText(v.Number, system) + "\"/>",
    };
}
