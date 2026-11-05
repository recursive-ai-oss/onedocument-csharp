using System;
using System.Buffers;
using System.Globalization;

namespace RecursiveAI.OneOffice.NumberFormats;

public sealed partial class NumberFormat
{
    private const int StackChars = 512;

    /// <summary>Formats a number to text.</summary>
    /// <param name="value">The value (a date or time is its serial number).</param>
    /// <param name="options">Options; the defaults are the 1900 date system and US English separators.</param>
    /// <returns>
    /// The displayed text. When the value cannot be shown (a negative or too-large date serial, NaN or infinity, or a number wider than
    /// <see cref="FormatOptions.MaxWidth"/>) the result is a row of <c>#</c> (eight when no width is given), or <c>#NUM!</c> for NaN and infinity;
    /// use <see cref="TryFormat(double, Span{char}, FormatOptions)"/> to see the status.
    /// </returns>
    public string Format(double value, FormatOptions options = default)
    {
        // A fixed 1 KiB scratch buffer; longer output (very wide numbers) retries with a pooled array of up to 64 Ki chars.
        Span<char> stack = stackalloc char[StackChars];
        FormatResult r = TryFormat(value, stack, options);
        if (r.Status == FormatStatus.DestinationTooSmall)
        {
            for (int size = StackChars * 4; size <= 1 << 16; size *= 4)
            {
                char[] rented = ArrayPool<char>.Shared.Rent(size);
                try
                {
                    r = TryFormat(value, rented, options);
                    if (r.Status != FormatStatus.DestinationTooSmall)
                    {
                        return Finish(r, rented.AsSpan(0, r.Length), options);
                    }
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(rented);
                }
            }

            return new string('#', 8);
        }

        return Finish(r, stack.Slice(0, r.Length), options);
    }

    internal static int GeneralWidth(FormatOptions o) => o.GeneralWidth > 0 ? o.GeneralWidth : 11;

    private static string Finish(FormatResult r, ReadOnlySpan<char> text, FormatOptions options) => r.Status switch
    {
        FormatStatus.Ok => text.ToString(),
        FormatStatus.InvalidNumber => "#NUM!",
        FormatStatus.NotRepresentable => new string('#', options.MaxWidth > 0 ? options.MaxWidth : 8),
        FormatStatus.DoesNotFit => new string('#', Math.Max(1, options.MaxWidth)),
        _ => new string('#', options.MaxWidth > 0 ? options.MaxWidth : 8),
    };

    /// <summary>Formats a number into a span without allocating.</summary>
    /// <param name="value">The value (a date or time is its serial number).</param>
    /// <param name="destination">Where to write the text.</param>
    /// <param name="options">Options.</param>
    /// <returns>
    /// The status, the number of characters written and the layout hints (colour, <c>*</c> fill position). On
    /// <see cref="FormatStatus.DateOutOfRange"/> and <see cref="FormatStatus.InvalidNumber"/> nothing is written; on
    /// <see cref="FormatStatus.DoesNotFit"/> a row of <c>#</c> of the allowed width is written.
    /// </returns>
    public FormatResult TryFormat(double value, Span<char> destination, FormatOptions options = default)
    {
        var w = new Writer(destination);
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return new FormatResult(FormatStatus.InvalidNumber, 0);
        }

        if (value == 0)
        {
            value = 0; // negative zero is zero
        }

        NumberFormatCulture culture = options.Culture ?? NumberFormatCulture.EnglishUnitedStates;
        int idx = SelectSection(value, out bool dropSign, out bool noSection);
        if (noSection)
        {
            return new FormatResult(FormatStatus.NotRepresentable, 0);
        }

        NumberFormatSection sec = _sections[idx];
        bool neg = value < 0 && !dropSign;
        double abs = Math.Abs(value);
        FormatStatus status;
        if (sec.DbNum != 0)
        {
            // Numerals are rewritten after layout, so the plain digits go to a scratch buffer first (at most 256 characters; longer
            // sections are shown with plain digits).
            Span<char> scratch = stackalloc char[DbNumbers.MaxChars];
            var sw = new Writer(scratch);
            status = EmitSection(ref sw, sec, value, abs, neg, culture, options);
            if (status != FormatStatus.Ok)
            {
                return new FormatResult(status, 0, sec.Color);
            }

            if (sw.Overflow)
            {
                status = EmitSection(ref w, sec, value, abs, neg, culture, options);
            }
            else
            {
                DbNumbers.Apply(sw.Written, ref w, sec, culture, options);
            }
        }
        else
        {
            status = EmitSection(ref w, sec, value, abs, neg, culture, options);
            if (status != FormatStatus.Ok)
            {
                return new FormatResult(status, 0, sec.Color);
            }
        }

        if (w.Overflow)
        {
            return new FormatResult(FormatStatus.DestinationTooSmall, 0);
        }

        if (options.MaxWidth > 0 && w.Length > options.MaxWidth && sec.Kind is not (NumberFormatSectionKind.General or NumberFormatSectionKind.Literal or NumberFormatSectionKind.Empty))
        {
            if (options.MaxWidth > destination.Length)
            {
                return new FormatResult(FormatStatus.DestinationTooSmall, 0);
            }

            destination.Slice(0, options.MaxWidth).Fill('#');
            return new FormatResult(FormatStatus.DoesNotFit, options.MaxWidth, sec.Color);
        }

        return new FormatResult(status, w.Length, sec.Color, w.FillPosition, w.FillCharacter);
    }

    private static FormatStatus EmitSection(ref Writer w, NumberFormatSection sec, double value, double abs, bool neg, NumberFormatCulture culture, FormatOptions options)
    {
        switch (sec.Kind)
        {
            case NumberFormatSectionKind.Empty:
                // Observed in Excel: a section that holds only a colour or a condition shows the number as General does.
                if (sec.Color is not null || sec.Condition is not null)
                {
                    NumberWriter.AppendGeneral(ref w, abs, neg, GeneralWidth(options), culture);
                }

                break;
            case NumberFormatSectionKind.Literal:
                EmitLiteral(ref w, sec, value, neg, culture, options);
                break;
            case NumberFormatSectionKind.General:
                EmitGeneralSection(ref w, sec, abs, neg, culture, options);
                break;
            case NumberFormatSectionKind.DateTime:
                return DateWriter.Emit(ref w, sec, value, options, culture);
            default:
                return NumberWriter.Emit(ref w, sec, abs, neg, culture);
        }

        return FormatStatus.Ok;
    }

    /// <summary>Formats a text value (the fourth section, or the section that contains <c>@</c>).</summary>
    /// <param name="text">The cell text.</param>
    /// <returns>The displayed text; the text itself when the format has no text section.</returns>
    public string FormatText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        NumberFormatSection? sec = TextSection();
        if (sec is null)
        {
            return text;
        }

        int need = text.Length + sec.Source.Length + 8;
        char[]? rented = null;
        // At most 1 KiB on the stack; longer text uses a pooled array.
        Span<char> buf = need <= StackChars ? stackalloc char[StackChars] : (rented = ArrayPool<char>.Shared.Rent(need * 2)).AsSpan();
        try
        {
            FormatResult r = TryFormatText(text, buf);
            return r.Status == FormatStatus.Ok ? buf.Slice(0, r.Length).ToString() : text;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Formats a text value into a span without allocating.</summary>
    /// <param name="text">The cell text.</param>
    /// <param name="destination">Where to write.</param>
    /// <returns>The status and length. A format without a text section copies the text unchanged.</returns>
    public FormatResult TryFormatText(ReadOnlySpan<char> text, Span<char> destination)
    {
        var w = new Writer(destination);
        NumberFormatSection? sec = TextSection();
        if (sec is null)
        {
            w.Append(text);
            return w.Overflow ? new FormatResult(FormatStatus.DestinationTooSmall, 0) : new FormatResult(FormatStatus.Ok, w.Length);
        }

        foreach (Element e in sec.Elements)
        {
            switch (e.Kind)
            {
                case EK.Literal:
                    w.Append(e.Text);
                    break;
                case EK.At:
                    w.Append(text);
                    break;
                case EK.Fill:
                    w.SetFill(e.Ch);
                    break;
            }
        }

        return w.Overflow ? new FormatResult(FormatStatus.DestinationTooSmall, 0) : new FormatResult(FormatStatus.Ok, w.Length, sec.Color, w.FillPosition, w.FillCharacter);
    }

    private NumberFormatSection? TextSection()
    {
        if (_sections.Length == 4)
        {
            return _sections[3];
        }

        foreach (NumberFormatSection s in _sections)
        {
            if (s.ContainsText && s.Kind == NumberFormatSectionKind.Literal)
            {
                return s;
            }
        }

        return null;
    }

    // ECMA-376 Part 1 §18.8.31: with one section it is used for all numbers; with two, the first is for positive numbers and zero and
    // the second for negative numbers; with three or four, positive, negative, zero. An explicit condition replaces the default test;
    // a section without one that comes last takes whatever is left.
    private int SelectSection(double v, out bool dropSign, out bool notFound)
    {
        dropSign = false;
        notFound = false;
        int count = _sections.Length == 4 ? 3 : _sections.Length;
        if (count == 1)
        {
            return 0;
        }

        int chosen = count - 1;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            NumberFormatSection s = _sections[i];
            bool match;
            if (s.Condition is { } c)
            {
                match = c.Matches(v);
            }
            else if (i == count - 1)
            {
                match = true;
            }
            else if (i == 0)
            {
                match = count == 2 ? v >= 0 : v > 0;
            }
            else
            {
                match = v < 0;
            }

            if (match)
            {
                chosen = i;
                found = true;
                break;
            }
        }

        if (!found)
        {
            chosen = count - 1;
        }

        // The minus sign is not shown when a section is for negative numbers only. Observed in Excel (1900 grid, TEXT and cells):
        //  - with no conditions anywhere, the second and third sections are the negative ones: no sign;
        //  - a section with a condition drops the sign when the condition holds only below zero ([<0], [<-3], [<=-3]); [<=0] and [<5] keep it;
        //  - a leftover section after conditions drops it when the first condition is <, <= or <> ([<=-3]0;0.0), and keeps it after
        //    >, >= or = ([>100]"big";0.0).
        bool anyCondition = false;
        for (int i = 0; i < count; i++)
        {
            anyCondition |= _sections[i].Condition is not null;
        }

        NumberFormatCondition? chosenCondition = _sections[chosen].Condition;
        if (v >= 0)
        {
            dropSign = false;
        }
        else if (chosenCondition is { } cc)
        {
            dropSign = (cc.Operator == "<" && cc.Operand <= 0) || (cc.Operator is "<=" or "=" && cc.Operand < 0);
        }
        else if (!anyCondition)
        {
            dropSign = chosen >= 1;
        }
        else
        {
            dropSign = chosen >= 1 && _sections[0].Condition is { Operator: "<" or "<=" or "<>" };
        }

        // Excel shows no number at all when every section has a condition and none matches.
        if (!found)
        {
            notFound = true;
        }

        return chosen;
    }

    private static void EmitLiteral(ref Writer w, NumberFormatSection sec, double value, bool neg, NumberFormatCulture culture, FormatOptions options)
    {
        // Observed in Excel: a number put through a format that has only a text section (@, "x"@) is shown the way General shows it,
        // without the format's literal text.
        if (sec.ContainsText)
        {
            NumberWriter.AppendGeneral(ref w, Math.Abs(value), value < 0, GeneralWidth(options), culture);
            return;
        }

        // Observed in Excel: a negative number in a section of plain text still gets its minus sign ([<=0]"n" shows -n).
        if (neg)
        {
            w.Append('-');
        }

        foreach (Element e in sec.Elements)
        {
            switch (e.Kind)
            {
                case EK.Literal:
                    w.Append(e.Text);
                    break;
                case EK.Fill:
                    w.SetFill(e.Ch);
                    break;
            }
        }
    }

    private static void EmitGeneralSection(ref Writer w, NumberFormatSection sec, double abs, bool neg, NumberFormatCulture culture, FormatOptions options)
    {
        // As for digit placeholders, the minus sign comes before everything else in the section.
        if (neg && abs != 0)
        {
            w.Append('-');
        }

        foreach (Element e in sec.Elements)
        {
            switch (e.Kind)
            {
                case EK.Literal:
                    w.Append(e.Text);
                    break;
                case EK.General or EK.At:
                    NumberWriter.AppendGeneral(ref w, abs, neg, GeneralWidth(options), culture, signWritten: true);
                    break;
                case EK.Fill:
                    w.SetFill(e.Ch);
                    break;
            }
        }
    }
}

// A forward-only writer over a caller's span; running out of room sets Overflow instead of throwing.
internal ref struct Writer
{
    private readonly Span<char> _buf;

    public Writer(Span<char> buffer)
    {
        _buf = buffer;
        Length = 0;
        Overflow = false;
        FillPosition = -1;
        FillCharacter = '\0';
    }

    public int Length { get; private set; }

    public bool Overflow { get; private set; }

    public int FillPosition { get; private set; }

    public char FillCharacter { get; private set; }

    public readonly Span<char> Written => _buf.Slice(0, Length);

    public void Append(char c)
    {
        if (Length < _buf.Length)
        {
            _buf[Length++] = c;
        }
        else
        {
            Overflow = true;
        }
    }

    public void Append(scoped ReadOnlySpan<char> s)
    {
        if (s.Length <= _buf.Length - Length)
        {
            s.CopyTo(_buf.Slice(Length));
            Length += s.Length;
        }
        else
        {
            Overflow = true;
        }
    }

    public void Append(string? s) => Append(s.AsSpan());

    public void AppendRepeat(char c, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Append(c);
        }
    }

    public void SetFill(char c)
    {
        FillPosition = Length;
        FillCharacter = c;
    }

    public void Reset() => Length = 0;
}
