using System;
using System.Globalization;

namespace RecursiveAI.OneOffice.NumberFormats;

// Number sections: digit placeholders, scaling, percent, scientific, fractions and General.
//
// Excel shows at most 15 significant digits and rounds half away from zero on that decimal text, not on the binary value. So a double
// is first turned into its 15-digit decimal form (digits + decimal point position), and all scaling, rounding and digit placement
// work on those decimal digits. That is why 0.285 shows as 0.29 with "0.00" and 1234567890123456789 shows as 1234567890123460000.
internal static class NumberWriter
{
    // Decimal digits of a non-negative number: d[0..n) are the significant digits without trailing zeros; the value is
    // 0.d0d1d2... * 10^pt. Zero has n = 0 and pt = 0. The buffer must hold at least 24 digits.
    internal static void Extract(double abs, Span<byte> d, out int n, out int pt)
    {
        if (abs == 0)
        {
            n = 0;
            pt = 0;
            return;
        }

        // "E14" gives 15 significant digits, correctly rounded from the exact binary value: d.dddddddddddddd E+ddd (at most 22 chars).
        Span<char> t = stackalloc char[32];
        abs.TryFormat(t, out int len, "E14", CultureInfo.InvariantCulture);
        d[0] = (byte)(t[0] - '0');
        for (int i = 0; i < 14; i++)
        {
            d[1 + i] = (byte)(t[2 + i] - '0');
        }

        int e = 0;
        for (int i = 18; i < len; i++)
        {
            e = (e * 10) + (t[i] - '0');
        }

        if (t[17] == '-')
        {
            e = -e;
        }

        n = 15;
        while (n > 0 && d[n - 1] == 0)
        {
            n--;
        }

        pt = e + 1;
    }

    // Rounds half away from zero so that `keep` digits remain after the decimal point.
    internal static void RoundTo(Span<byte> d, ref int n, ref int pt, int keep)
    {
        int cut = pt + keep;
        if (cut < 0)
        {
            n = 0;
            pt = 0;
            return;
        }

        if (cut >= n)
        {
            return;
        }

        bool up = d[cut] >= 5;
        n = cut;
        if (up)
        {
            int i = cut - 1;
            while (i >= 0 && d[i] == 9)
            {
                d[i] = 0;
                i--;
            }

            if (i >= 0)
            {
                d[i]++;
            }
            else
            {
                d[0] = 1;
                n = 1;
                pt++;
                return;
            }
        }

        while (n > 0 && d[n - 1] == 0)
        {
            n--;
        }

        if (n == 0)
        {
            pt = 0;
        }
    }

    internal static FormatStatus Emit(ref Writer w, NumberFormatSection sec, double abs, bool neg, NumberFormatCulture culture)
    {
        if (sec.Kind == NumberFormatSectionKind.Fraction)
        {
            return EmitFraction(ref w, sec, abs, neg, culture);
        }

        // 24 digits: 15 significant plus room for a carry; bounded by Extract.
        Span<byte> d = stackalloc byte[24];
        Extract(abs, d, out int n, out int pt);
        int exp = 0;
        if (n > 0)
        {
            pt += (2 * sec.PercentCount) - (3 * sec.ScaleCommas);
        }

        if (sec.Kind == NumberFormatSectionKind.Scientific)
        {
            int p = Math.Max(1, sec.IntegerPlaceholders);
            if (n > 0)
            {
                int e10 = pt - 1;
                exp = FloorDiv(e10, p) * p;
                pt = e10 - exp + 1;
            }

            RoundTo(d, ref n, ref pt, sec.FractionPlaceholders);
            if (pt > p)
            {
                exp += p;
                pt -= p;
            }
        }
        else
        {
            RoundTo(d, ref n, ref pt, sec.FractionPlaceholders);
        }

        // Observed in Excel: a negative number that rounds to zero shows no minus sign.
        if (neg && n > 0)
        {
            w.Append('-');
        }

        EmitDigits(ref w, sec, d, n, pt, exp, culture);
        return FormatStatus.Ok;
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

    private static void EmitDigits(ref Writer w, NumberFormatSection sec, scoped ReadOnlySpan<byte> d, int n, int pt, int exp, NumberFormatCulture culture)
    {
        int bigP = sec.IntegerPlaceholders;
        int len = pt > 0 ? pt : 0;
        int seen = 0;
        int frac = 0;
        bool afterPoint = false;
        int sig = n > pt ? n - pt : 0;
        Element[] els = sec.Elements;
        for (int i = 0; i < els.Length; i++)
        {
            Element e = els[i];
            switch (e.Kind)
            {
                case EK.Literal:
                    w.Append(e.Text);
                    break;
                case EK.Fill:
                    w.SetFill(e.Ch);
                    break;
                case EK.Percent:
                    w.Append('%');
                    break;
                case EK.Zero or EK.Hash or EK.Q:
                    if (!afterPoint)
                    {
                        int q = bigP - 1 - seen;
                        seen++;
                        if (seen == 1 && len > bigP)
                        {
                            for (int x = len - 1; x >= bigP; x--)
                            {
                                IntDigit(ref w, sec, d, n, pt, x, EK.Zero, culture);
                            }
                        }

                        // Observed in Excel: a zero in scientific notation shows every integer placeholder as 0.
                        IntDigit(ref w, sec, d, n, pt, q, n == 0 && sec.Kind == NumberFormatSectionKind.Scientific ? EK.Zero : e.Kind, culture);
                    }
                    else
                    {
                        frac++;
                        if (frac <= sig)
                        {
                            int idx = pt + frac - 1;
                            w.Append((char)('0' + (idx >= 0 && idx < n ? d[idx] : 0)));
                        }
                        else if (e.Kind == EK.Zero)
                        {
                            w.Append('0');
                        }
                        else if (e.Kind == EK.Q)
                        {
                            w.Append(' ');
                        }
                    }

                    break;
                case EK.Point:
                    if (seen == 0 && bigP == 0 && len > 0)
                    {
                        // No integer placeholder: the integer digits go right before the decimal point.
                        for (int x = len - 1; x >= 0; x--)
                        {
                            IntDigit(ref w, sec, d, n, pt, x, EK.Zero, culture);
                        }
                    }

                    afterPoint = true;
                    w.Append(culture.DecimalSeparator);
                    break;
                case EK.Exp:
                    w.Append(e.Ch);
                    if (exp < 0)
                    {
                        w.Append('-');
                    }
                    else if (e.N == 1)
                    {
                        w.Append('+');
                    }

                    AppendPadded(ref w, Math.Abs(exp), sec.ExponentDigits);
                    break;
            }
        }
    }

    private static void IntDigit(ref Writer w, NumberFormatSection sec, scoped ReadOnlySpan<byte> d, int n, int pt, int q, EK kind, NumberFormatCulture culture)
    {
        int idx = pt - 1 - q;
        bool shown;
        if (idx >= 0)
        {
            w.Append((char)('0' + (idx < n ? d[idx] : 0)));
            shown = true;
        }
        else if (kind == EK.Zero)
        {
            w.Append('0');
            shown = true;
        }
        else
        {
            if (kind == EK.Q)
            {
                w.Append(' ');
            }

            shown = false;
        }

        if (shown && sec.UsesGrouping && q > 0 && q % 3 == 0)
        {
            w.Append(culture.GroupSeparator);
        }
    }

    // A whole number held in a double, as Excel shows it: 15 significant digits, zeros after them.
    internal static void AppendWholeNumber(ref Writer w, double value, int minDigits)
    {
        if (value < 1e15)
        {
            AppendPadded(ref w, (long)value, minDigits);
            return;
        }

        // Fixed small buffer: Extract fills at most 24 digits.
        Span<byte> digits = stackalloc byte[24];
        Extract(value, digits, out int n, out int pt);
        for (int i = 0; i < pt; i++)
        {
            w.Append((char)('0' + (i < n ? digits[i] : 0)));
        }
    }

    internal static void AppendPadded(ref Writer w, long value, int minDigits)
    {
        // Fixed small buffer: a long or double prints in at most 24 characters.
        Span<char> t = stackalloc char[24];
        value.TryFormat(t, out int len, default, CultureInfo.InvariantCulture);
        for (int i = len; i < minDigits; i++)
        {
            w.Append('0');
        }

        w.Append(t.Slice(0, len));
    }

    // Excel stores fraction numerators as 32-bit signed integers: a larger one cannot be shown.
    private const long MaxNumerator = int.MaxValue;

    // Observed in Excel: the fraction is the last continued-fraction convergent whose denominator fits the placeholders (and whose
    // numerator fits 32 bits), not the closest fraction. The expansion runs on the binary double, so 5.3 gives 5 2/7 while 0.1+0.2
    // gives 1/3. When not even the first step fits, the number is rounded to a whole.
    private static bool Convergent(double x, long maxDen, out long num, out long den)
    {
        num = 0;
        den = 1;
        double fl = Math.Floor(x);
        if (fl > MaxNumerator)
        {
            return false;
        }

        long pPrev = 1, qPrev = 0, p = (long)fl, q = 1;
        double r = x - fl;
        double firstRemainder = r;
        bool advanced = false;
        while (r > 1e-12)
        {
            double inv = 1.0 / r;
            double a = Math.Floor(inv);
            r = inv - a;
            if (a > 1e9)
            {
                break;
            }

            long p2 = ((long)a * p) + pPrev;
            long q2 = ((long)a * q) + qPrev;
            if (q2 > maxDen || p2 > MaxNumerator)
            {
                break;
            }

            pPrev = p;
            qPrev = q;
            p = p2;
            q = q2;
            advanced = true;
        }

        if (!advanced && firstRemainder > 1e-12)
        {
            double rounded = Math.Round(x, MidpointRounding.AwayFromZero);
            if (rounded > MaxNumerator)
            {
                return false;
            }

            p = (long)rounded;
            q = 1;
        }

        num = p;
        den = q;
        return true;
    }

    private static FormatStatus EmitFraction(ref Writer w, NumberFormatSection sec, double abs, bool neg, NumberFormatCulture culture)
    {
        int wholePh = sec.NumeratorStart;
        int numPh = sec.SlashIndex - sec.NumeratorStart;
        bool hasWhole = false;
        Element[] els = sec.Elements;
        for (int i = 0; i < wholePh; i++)
        {
            if (els[i].Kind is EK.Zero or EK.Hash or EK.Q)
            {
                hasWhole = true;
            }
        }

        double wholeD = 0;
        if (hasWhole)
        {
            wholeD = Math.Floor(abs);
        }

        long num;
        long den = 1;
        if (sec.FixedDenominator > 0)
        {
            den = sec.FixedDenominator;
            // The product is taken to 15 significant digits before rounding, as every number Excel shows is: 0.285 over 100 is 29.
            double product = (hasWhole ? abs - wholeD : abs) * den;
            if (product > MaxNumerator)
            {
                return FormatStatus.NotRepresentable;
            }

            decimal t = Math.Round((decimal)product, MidpointRounding.AwayFromZero);
            num = (long)t;
        }
        else
        {
            long maxDen = 1;
            for (int k = 0; k < sec.DenominatorPlaceholders; k++)
            {
                maxDen *= 10;
            }

            if (!Convergent(hasWhole ? abs - wholeD : abs, maxDen - 1, out num, out den))
            {
                return FormatStatus.NotRepresentable;
            }
        }

        if (hasWhole && num >= den)
        {
            wholeD += num / den;
            num %= den;
        }

        bool numHasZero = false;
        for (int i = sec.NumeratorStart; i < sec.SlashIndex; i++)
        {
            numHasZero |= els[i].Kind == EK.Zero;
        }

        bool blank = hasWhole && num == 0 && !numHasZero;

        // Observed in Excel: without a whole part a fraction that is zero shows no minus sign; with one it keeps it.
        if (neg && (hasWhole || num != 0))
        {
            w.Append('-');
        }

        // Whole part: the same placement as an integer section. At most 24 digits: Extract keeps 15 significant ones.
        Span<byte> wd = stackalloc byte[24];
        Extract(wholeD, wd, out int wn, out int wpt);
        // Fixed small buffer: a long or double prints in at most 24 characters.
        Span<char> tmp = stackalloc char[24];
        bool whole0 = wholeD == 0;
        int seen = 0;
        int i2 = 0;
        for (; i2 < wholePh; i2++)
        {
            Element e = els[i2];
            if (e.Kind == EK.Literal)
            {
                w.Append(e.Text);
            }
            else if (e.Kind == EK.Fill)
            {
                w.SetFill(e.Ch);
            }
            else if (e.Kind is EK.Zero or EK.Hash or EK.Q)
            {
                int q = sec.IntegerPlaceholders - 1 - seen;
                seen++;
                if (seen == 1 && wpt > sec.IntegerPlaceholders)
                {
                    for (int x = wpt - 1; x >= sec.IntegerPlaceholders; x--)
                    {
                        IntDigit(ref w, sec, wd, wn, wpt, x, EK.Zero, culture);
                    }
                }

                // A value of exactly zero still shows its 0.
                IntDigit(ref w, sec, wd, wn, wpt, q, q == 0 && whole0 && num == 0 ? EK.Zero : e.Kind, culture);
            }
        }

        int denPh = sec.DenominatorPlaceholders;
        if (blank)
        {
            w.AppendRepeat(' ', numPh + 1 + (sec.FixedDenominator > 0 ? sec.FixedDenominator.ToString(CultureInfo.InvariantCulture).Length : denPh));
        }
        else
        {
            AppendRightAligned(ref w, num, els, sec.NumeratorStart, numPh);
            w.Append('/');
            if (sec.FixedDenominator > 0)
            {
                AppendPadded(ref w, den, 0);
            }
            else
            {
                den.TryFormat(tmp, out int dl, default, CultureInfo.InvariantCulture);
                w.Append(tmp.Slice(0, dl));
                // Placeholders the denominator does not fill show a space so that fractions line up.
                for (int k = dl; k < denPh; k++)
                {
                    w.Append(' ');
                }
            }
        }

        for (int i = sec.SlashIndex + 1; i < els.Length; i++)
        {
            Element e = els[i];
            if (e.Kind == EK.Literal)
            {
                w.Append(e.Text);
            }
            else if (e.Kind == EK.Fill)
            {
                w.SetFill(e.Ch);
            }
        }

        return FormatStatus.Ok;
    }

    private static void AppendRightAligned(ref Writer w, long value, Element[] els, int start, int count)
    {
        // Fixed small buffer: a long or double prints in at most 24 characters.
        Span<char> t = stackalloc char[24];
        value.TryFormat(t, out int len, default, CultureInfo.InvariantCulture);
        for (int k = 0; k < count - len; k++)
        {
            EK kind = els[start + k].Kind;
            if (kind == EK.Q)
            {
                w.Append(' ');
            }
            else if (kind == EK.Zero)
            {
                w.Append('0');
            }
        }

        w.Append(t.Slice(0, len));
    }

    // Excel's General: up to `width` characters (11 by default; the sign is not counted), at most 15 significant digits, scientific
    // when the integer part is wider than that or the number is below 0.0001.
    internal static void AppendGeneral(ref Writer w, double abs, bool neg, int width, NumberFormatCulture culture, bool signWritten = false)
    {
        if (width < 3)
        {
            width = 3;
        }

        if (abs == 0)
        {
            w.Append('0');
            return;
        }

        // Fixed small buffer: a long or double prints in at most 24 characters.
        Span<byte> d = stackalloc byte[24];
        Extract(abs, d, out int n, out int pt);
        int e10 = pt - 1;
        if (neg && !signWritten)
        {
            w.Append('-');
        }

        // Observed in Excel (TEXT and cells alike): below one, the plain form wins unless the scientific form keeps more of the digits.
        // 0.00001 and 0.000000001 stay plain (one digit either way), 0.000123457 stays plain (six digits either way), and
        // 0.000000012345 turns to 1.2345E-08 (the plain form would keep two of its five digits).
        bool tinyUsesExponent = false;
        if (e10 < 0)
        {
            int expDigits = e10 <= -100 ? 5 : 4;
            int plainKept = Math.Min(n, Math.Max(0, width - 2 - (-e10 - 1)));
            int sciKept = Math.Min(n, Math.Max(1, width - expDigits - 1));
            tinyUsesExponent = plainKept < sciKept;
        }

        if (e10 + 1 > Math.Min(width, 15) || tinyUsesExponent)
        {
            int expLen = Math.Abs(e10) >= 100 ? 5 : 4;
            int m = Math.Max(1, Math.Min(15, width - 1 - expLen));
            int p1 = 1;
            RoundTo(d, ref n, ref p1, m - 1);
            if (p1 > 1)
            {
                e10++;
            }

            w.Append((char)('0' + d[0]));
            if (n > 1)
            {
                w.Append(culture.DecimalSeparator);
                for (int i = 1; i < n; i++)
                {
                    w.Append((char)('0' + d[i]));
                }
            }

            w.Append('E');
            w.Append(e10 < 0 ? '-' : '+');
            AppendPadded(ref w, Math.Abs(e10), 2);
            return;
        }

        int intDigits = e10 >= 0 ? e10 + 1 : 1;
        int keep = Math.Max(0, width - intDigits - 1);
        RoundTo(d, ref n, ref pt, keep);
        if (pt <= 0)
        {
            w.Append('0');
        }
        else
        {
            for (int i = 0; i < pt; i++)
            {
                w.Append((char)('0' + (i < n ? d[i] : 0)));
            }
        }

        if (n > pt)
        {
            w.Append(culture.DecimalSeparator);
            for (int i = pt; i < n; i++)
            {
                w.Append(i < 0 ? '0' : (char)('0' + d[i]));
            }
        }
    }
}
