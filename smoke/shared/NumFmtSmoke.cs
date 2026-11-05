using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Smoke;

/// <summary>Number formats under NativeAOT and WASM: output must not depend on the host's culture data.</summary>
internal static class NumFmtSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        void Check(string code, double v, string expected, FormatOptions o = default)
        {
            string actual = NumberFormat.Parse(code).Format(v, o);
            if (actual != expected)
            {
                failures.Add($"numfmt {code} {v}: '{actual}' != '{expected}'");
            }
        }

        Check("#,##0.00", 1234567.891, "1,234,567.89");
        Check("0.00%", 0.12345, "12.35%");
        Check("yyyy-mm-dd hh:mm:ss", 45000.75, "2023-03-15 18:00:00");
        Check("[$-407]dddd, d. mmmm yyyy", 45000, "Mittwoch, 15. M\u00e4rz 2023");
        Check("#,##0.00", 1234.5, "1.234,50", new FormatOptions(Culture: NumberFormatCulture.Get(0x0407)));
        Check("# ?/?", 5.25, "5 1/4");
        Check("General", 1.0 / 3, "0.333333333");
        Check("0.00E+00", 12200000, "1.22E+07");
        Check("[DBNum1][$-804]0", 123, "\u4e00\u4e8c\u4e09");
        Check("[$-419]mmmm", 45000, "\u041c\u0430\u0440\u0442");
        Check("# ?/?", 5.3, "5 2/7");
        if (NumberFormat.TryParse("0.0.0", out _, out NumberFormatDefect? d) || d.Code != "numfmt.multiple-decimal-points")
        {
            failures.Add("numfmt defect");
        }

        return failures;
    }
}
