using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// The number-format service. <c>numfmt</c> takes the input as a format code: it must parse or give a defect whose code starts with
/// <c>numfmt.</c>; a code that parses is used to format a spread of values in every date system and several cultures, and nothing may
/// throw. <c>numfmtfmt</c> takes eight bytes of double, one byte of options and then the code, and checks that the string, span and
/// exact-size span results agree and that a span one character short reports <c>DestinationTooSmall</c>.
/// </summary>
internal static class NumFmtTarget
{
    private static readonly double[] Probe =
    [
        0, 1, -1, 0.5, -0.5, 1234.5678, -1234.5678, 0.000123, 1e-300, 1e300, 45000.123456, 2958465.9999, -0.4, 99.995, 5e-324, double.MaxValue,
    ];

    public static void RunParse(ReadOnlySpan<byte> data)
    {
        string code = Encoding.UTF8.GetString(data);
        if (!NumberFormat.TryParse(code, out NumberFormat? f, out NumberFormatDefect? d))
        {
            if (d is null || !d.Code.StartsWith("numfmt.", StringComparison.Ordinal) || d.Position < 0 || d.Position > code.Length)
            {
                throw new InvalidOperationException("a rejected format code must carry a located numfmt defect");
            }

            return;
        }

        _ = f.Category;
        _ = f.GetCellFormatCode();
        NumberFormatCulture[] cultures = [.. NumberFormatCulture.Supported];
        char[] buf = new char[256];
        for (int i = 0; i < Probe.Length; i++)
        {
            var o = new FormatOptions((DateSystem)(i & 1), cultures[i % cultures.Length], i % 2 == 0 ? 11 : 30, i % 5 == 0 ? 12 : 0);
            string a = f.Format(Probe[i], o);
            string b = f.Format(Probe[i], o);
            if (a != b)
            {
                throw new InvalidOperationException("formatting is not deterministic");
            }

            _ = f.TryFormat(Probe[i], buf, o);
        }

        _ = f.FormatText(code);
    }

    public static void RunFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9)
        {
            return;
        }

        double v = BitConverter.ToDouble(data[..8]);
        byte opt = data[8];
        string code = Encoding.UTF8.GetString(data[9..]);
        if (!NumberFormat.TryParse(code, out NumberFormat? f, out _))
        {
            return;
        }

        NumberFormatCulture culture = NumberFormatCulture.Supported[(opt >> 1) % NumberFormatCulture.Supported.Count];
        var o = new FormatOptions((opt & 1) == 0 ? DateSystem.Date1900 : DateSystem.Date1904, culture);
        char[] big = new char[1 << 16];
        FormatResult r = f.TryFormat(v, big, o);
        string s = f.Format(v, o);
        if (r.Status == FormatStatus.Ok)
        {
            if (new string(big, 0, r.Length) != s)
            {
                throw new InvalidOperationException("Format and TryFormat disagree");
            }

            char[] exact = new char[r.Length];
            FormatResult e = f.TryFormat(v, exact, o);
            if (e.Status != FormatStatus.Ok || e.Length != r.Length || new string(exact) != s)
            {
                throw new InvalidOperationException("an exactly sized span must succeed with the same text");
            }

            if (r.Length > 0)
            {
                char[] shortBuf = new char[r.Length - 1];
                if (f.TryFormat(v, shortBuf, o).Status != FormatStatus.DestinationTooSmall)
                {
                    throw new InvalidOperationException("a span one character short must report DestinationTooSmall");
                }
            }
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        string[] codes =
        [
            "General", "0.00", "#,##0.00", "0.00E+00", "# ?/?", "# ??/16", "0%", "[Red]0.0;[Blue]-0.0;\"zero\";@", "[>100]0;[<=100]0.0", "yyyy-mm-dd hh:mm:ss",
            "h:mm:ss AM/PM", "[h]:mm:ss.000", "dddd, mmmm dd, yyyy", "[$-407]d. mmmm yyyy", "[$€-407]#,##0.00", "[DBNum1]0", "_(* #,##0_);_(* \\(#,##0\\);_(* \"-\"_);_(@_)",
            "0.0,,\"M\"", "000-0000", "[$-F800]", "[$-411]ggge\"年\"m\"月\"d\"日\"", ";;;", "0;-0;;@", "\"x\"General",
        ];
        int i = 0;
        foreach (string c in codes)
        {
            byte[] b = Encoding.UTF8.GetBytes(c);
            File.WriteAllBytes(Path.Combine(dir, $"parse-{i}"), b);
            var f = new List<byte>(BitConverter.GetBytes(i % 3 == 0 ? 45000.5 : i % 3 == 1 ? -1234.5678 : 0.285));
            f.Add((byte)(i * 2));
            f.AddRange(b);
            File.WriteAllBytes(Path.Combine(dir, $"format-{i}"), f.ToArray());
            i++;
        }

        return 0;
    }
}
