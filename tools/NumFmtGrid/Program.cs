using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.NumberFormats;

// Writes the number-format edge-case grid: a workbook of format codes x values for Excel to answer, and a manifest of our answers.
//
//   NumFmtGrid write <out dir> [skip-codes.txt]     grid-text.xlsx, grid-cells-NN.xlsx, manifest.tsv, readback.md (one format code per line to leave out)
//   NumFmtGrid compare <manifest.tsv> <answers.tsv> [report.tsv]
//        answers.tsv: id <TAB> text-from-TEXT() <TAB> display-text-of-the-formatted-cell (as read back from an application)
//        prints the agreement and writes every difference to report.tsv.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "write")
        {
            Directory.CreateDirectory(args[1]);
            HashSet<string>? skip = args.Length > 2 ? File.ReadAllLines(args[2], Encoding.UTF8).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal) : null;
            return Write(args[1], skip);
        }

        if (args.Length >= 3 && args[0] == "compare")
        {
            return Compare(args[1], args[2], args.Length > 3 ? args[3] : null);
        }

        Console.Error.WriteLine("usage: NumFmtGrid write <dir> | compare <manifest.tsv> <answers.tsv> [report.tsv]");
        return 2;
    }

    private static int Write(string dir, HashSet<string>? skip)
    {
        List<GridCase> cases = Cases.Build();
        var formats = cases.Select(c => c.Code).Distinct(StringComparer.Ordinal).ToList();
        var nf = new Dictionary<string, NumberFormat?>(StringComparer.Ordinal);
        foreach (string f in formats)
        {
            nf[f] = NumberFormat.TryParse(f, out NumberFormat? p, out _) ? p : null;
        }

        var manifest = new StringBuilder("id\tcode\tkind\tvalue\tours\tstatus\n");
        for (int i = 0; i < cases.Count; i++)
        {
            GridCase c = cases[i];
            string ours = string.Empty, status = "ok";
            NumberFormat? f = nf[c.Code];
            if (f is null)
            {
                status = "invalid";
            }
            else if (c.Text is not null)
            {
                ours = f.FormatText(c.Text);
            }
            else
            {
                char[] buf = new char[512];
                FormatResult r = f.TryFormat(c.Value, buf);
                status = r.Status.ToString();
                ours = r.Status == FormatStatus.Ok ? new string(buf, 0, r.Length) : string.Empty;
            }

            manifest.Append(i + 1).Append('\t').Append(Esc(c.Code)).Append('\t').Append(c.Text is null ? "n" : "t").Append('\t')
                .Append(c.Text ?? c.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\t').Append(Esc(ours)).Append('\t').Append(status).Append('\n');
        }

        File.WriteAllText(Path.Combine(dir, "manifest.tsv"), manifest.ToString(), new UTF8Encoding(false));
        // Two workbooks: formulas first (always safe to open), then the cells with custom number formats in styles.xml, so a
        // format Excel dislikes can only spoil the second.
        WriteWorkbook(Path.Combine(dir, "grid-text.xlsx"), cases, formats, text: true);
        // The cell workbooks are small shards of 12 codes each, so one code Excel refuses costs one shard, not the lot.
        List<string> valid = formats.Where(f => nf[f] is not null && !(skip?.Contains(f) ?? false)).ToList();
        for (int s = 0; s * 12 < valid.Count; s++)
        {
            List<string> part = valid.Skip(s * 12).Take(12).ToList();
            WriteWorkbook(Path.Combine(dir, $"grid-cells-{s:D2}.xlsx"), cases, part, text: false);
        }
        File.WriteAllText(Path.Combine(dir, "readback.md"), Readback(cases.Count, formats.Count), new UTF8Encoding(false));
        Console.WriteLine($"{cases.Count} cases, {formats.Count} distinct codes, {nf.Count(x => x.Value is null)} codes our parser rejects");
        return 0;
    }

    private static string Readback(int n, int codes) =>
        "# Reading the number-format grid back\n\n" +
        $"`grid.xlsx` has {n} cases over {codes} format codes (1900 date system, US English settings).\n\n" +
        "Sheet `text`: row r (2..): A id, B format code (text), C value (number, or text for the text cases), D `=TEXT(C,B)`.\n" +
        "Read `D<r>` (`Value2`/`Text`) after a full calculation. A TEXT failure shows as an error value: record its text (`#VALUE!`).\n\n" +
        "Sheet `cells`: row r: A id, B the value in a cell whose number format is the case's code. Read `B<r>.Text` (the displayed text, with a\n" +
        "column wide enough that nothing turns into `#`; when it still shows `#`, record it). Set the column width to 60 before reading.\n\n" +
        "Write `answers.tsv` with one line per id: `id<TAB>TEXT result<TAB>cell display text` (tabs and newlines in results escaped as `\\t`, `\\n`), then run\n" +
        "`NumFmtGrid compare manifest.tsv answers.tsv report.tsv`.\n";

    private static string Esc(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

    private static string X(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (c < 0x20 && c != '\t' && c != '\n' && c != '\r')
                    {
                        break;
                    }

                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static void WriteWorkbook(string path, List<GridCase> cases, List<string> formats, bool text)
    {
        if (text)
        {
            formats = new List<string>();
        }

        var fmtIndex = formats.Select((f, i) => (f, i)).ToDictionary(x => x.f, x => x.i, StringComparer.Ordinal);
        const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
            e.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var s = e.Open();
            byte[] b = new UTF8Encoding(false).GetBytes(content);
            s.Write(b, 0, b.Length);
        }

        Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
        Add("_rels/.rels", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{Rel}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Add("xl/workbook.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\"><sheets><sheet name=\"{(text ? "text" : "cells")}\" sheetId=\"1\" r:id=\"rId1\"/></sheets><calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>");
        Add("xl/_rels/workbook.xml.rels", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{Rel}/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId3\" Type=\"{Rel}/styles\" Target=\"styles.xml\"/></Relationships>");

        var st = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"{Main}\">");
        st.Append("<numFmts count=\"").Append(formats.Count).Append("\">");
        for (int i = 0; i < formats.Count; i++)
        {
            st.Append("<numFmt numFmtId=\"").Append(164 + i).Append("\" formatCode=\"").Append(X(formats[i])).Append("\"/>");
        }

        st.Append("</numFmts><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        st.Append("<cellXfs count=\"").Append(formats.Count + 1).Append("\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>");
        for (int i = 0; i < formats.Count; i++)
        {
            st.Append("<xf numFmtId=\"").Append(164 + i).Append("\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>");
        }

        st.Append("</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
        Add("xl/styles.xml", st.ToString());

        var s1 = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{Main}\"><cols><col min=\"2\" max=\"2\" width=\"40\" customWidth=\"1\"/><col min=\"3\" max=\"4\" width=\"60\" customWidth=\"1\"/></cols><sheetData>");
        s1.Append("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>id</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>code</t></is></c><c r=\"C1\" t=\"inlineStr\"><is><t>value</t></is></c><c r=\"D1\" t=\"inlineStr\"><is><t>TEXT(value, code)</t></is></c></row>");
        var s2 = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{Main}\"><cols><col min=\"2\" max=\"2\" width=\"60\" customWidth=\"1\"/></cols><sheetData>");
        s2.Append("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>id</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>value shown with the code</t></is></c></row>");
        int row = 0;
        for (int i = 0; i < cases.Count; i++)
        {
            GridCase c = cases[i];
            if (!text && !fmtIndex.ContainsKey(c.Code))
            {
                continue;
            }

            int r = (++row) + 1;
            string val = c.Text is null
                ? $"<v>{c.Value.ToString("R", CultureInfo.InvariantCulture)}</v>"
                : $"<is><t xml:space=\"preserve\">{X(c.Text)}</t></is>";
            string t = c.Text is null ? string.Empty : " t=\"inlineStr\"";
            s1.Append($"<row r=\"{r}\"><c r=\"A{r}\"><v>{i + 1}</v></c><c r=\"B{r}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{X(c.Code)}</t></is></c><c r=\"C{r}\"{t}>{val}</c><c r=\"D{r}\"><f>TEXT(C{r},B{r})</f></c></row>");
            s2.Append($"<row r=\"{r}\"><c r=\"A{r}\"><v>{i + 1}</v></c><c r=\"B{r}\" s=\"{(fmtIndex.TryGetValue(c.Code, out int xi) ? xi + 1 : 0)}\"{t}>{val}</c></row>");
        }

        s1.Append("</sheetData></worksheet>");
        s2.Append("</sheetData></worksheet>");
        Add("xl/worksheets/sheet1.xml", text ? s1.ToString() : s2.ToString());
    }

    private static int Compare(string manifestPath, string answersPath, string? reportPath)
    {
        string[] man = File.ReadAllLines(manifestPath, Encoding.UTF8).Skip(1).ToArray();
        var ans = new Dictionary<string, string[]>();
        foreach (string line in File.ReadAllLines(answersPath, Encoding.UTF8))
        {
            string[] p = line.Split('\t');
            if (p.Length >= 3)
            {
                ans[p[0]] = p;
            }
        }

        int total = 0, textOk = 0, cellOk = 0, missing = 0;
        var report = new StringBuilder("id\tcode\tvalue\tours\tTEXT()\tcell\n");
        foreach (string line in man)
        {
            string[] m = line.Split('\t');
            if (!ans.TryGetValue(m[0], out string[]? a))
            {
                missing++;
                continue;
            }

            total++;
            string ours = m[4];
            bool ok1 = a[1] == ours || (m[5] != "Ok" && a[1].StartsWith('#'));
            bool ok2 = a[2] == ours || (m[5] != "Ok" && a[2].StartsWith('#'));
            textOk += ok1 ? 1 : 0;
            cellOk += ok2 ? 1 : 0;
            if (!ok1 || !ok2)
            {
                report.Append(m[0]).Append('\t').Append(m[1]).Append('\t').Append(m[3]).Append('\t').Append(ours).Append('\t').Append(a[1]).Append('\t').Append(a[2]).Append('\n');
            }
        }

        Console.WriteLine($"compared {total} cases ({missing} without an answer): TEXT() agrees on {textOk}, cell display agrees on {cellOk}");
        if (reportPath is not null)
        {
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
        }

        return 0;
    }
}

internal readonly record struct GridCase(string Code, double Value, string? Text);

internal static class Cases
{
    private static readonly double[] Numbers =
    [
        0, 1, -1, 0.5, -0.5, 12, 123, 1234.5678, -1234.5678, 0.000123, 0.285, 1.005, 2.5, -2.5, 99.995, 0.9999999, 1.0 / 3, 100.0 / 3,
        12200000, 123456789.123, 0.0001, 0.00001, 0.000001, 0.0000001, 0.000012345, 1234567890.5, 1e11, 123456789012345, 12345678901234567, 1e-10, 5.25, 5.3, 0.1 + 0.2, -0.001, 1e300, 0.08, 2.8,
    ];

    private static readonly double[] Dates =
    [
        0, 0.5, 0.75, 1, 1.5, 31, 59, 60, 61, 366, 36526, 43831.75, 45000, 45000.123456, 45000.999994, 45292.5, 2958465, 2958465.4, 2958466, -1, 0.99999999,
        0.000011574, 0.0416666666666667, 1.0 / 1440, 62.0 / 1440 + 16.0 / 86400, 3735.8 / 86400, 1.5, 40.25, 45658,
    ];

    private static readonly string[] Texts = ["abc", "", "Hello World", "12", "a;b"];

    public static List<GridCase> Build()
    {
        var list = new List<GridCase>();
        foreach (string code in NumberCodes)
        {
            foreach (double v in Numbers)
            {
                list.Add(new GridCase(code, v, null));
            }
        }

        foreach (string code in DateCodes)
        {
            foreach (double v in Dates)
            {
                list.Add(new GridCase(code, v, null));
            }
        }

        foreach (string code in TextCodes)
        {
            foreach (string t in Texts)
            {
                list.Add(new GridCase(code, 0, t));
            }

            foreach (double v in new double[] { 5, -5, 0 })
            {
                list.Add(new GridCase(code, v, null));
            }
        }

        foreach (string code in SectionCodes)
        {
            foreach (double v in new double[] { 5, -5, 0, 0.5, 150, -150, 100, 1234.5, -0.4 })
            {
                list.Add(new GridCase(code, v, null));
            }
        }

        // Month, weekday and AM/PM names for every locale in the culture table, read back from the application.
        foreach (int lcid in new[] { 0x409, 0x809, 0x407, 0x40C, 0xC0A, 0x410, 0x416, 0x413, 0x419, 0x411, 0x804, 0x404, 0x412 })
        {
            string tag = "[$-" + lcid.ToString("X", CultureInfo.InvariantCulture) + "]";
            foreach (string code in new[] { "mmmm", "mmm" })
            {
                for (int m = 0; m < 12; m++)
                {
                    list.Add(new GridCase(tag + code, 45672 + (31 * m), null));
                }
            }

            foreach (string code in new[] { "dddd", "ddd" })
            {
                for (int d = 0; d < 7; d++)
                {
                    list.Add(new GridCase(tag + code, 45671 + d, null));
                }
            }

            list.Add(new GridCase(tag + "h:mm AM/PM", 0.25, null));
            list.Add(new GridCase(tag + "h:mm AM/PM", 0.75, null));
            list.Add(new GridCase(tag + "d mmmm yyyy", 45000, null));
            list.Add(new GridCase(tag + "#,##0.00", 1234567.891, null));
        }

        // Built-in IDs written out in their US English form.
        for (int id = 0; id <= 49; id++)
        {
            string? c = NumberFormat.GetBuiltInCode(id);
            if (c is not null && !NumberCodes.Contains(c) && !DateCodes.Contains(c) && !TextCodes.Contains(c))
            {
                foreach (double v in new double[] { 0, 5, -5, 1234.5678, 0.25, 45000.5 })
                {
                    list.Add(new GridCase(c, v, null));
                }
            }
        }

        // Probes added after the first round of answers: each settles one open question.
        void Probe(string[] codes, double[] values)
        {
            foreach (string code in codes)
            {
                foreach (double v in values)
                {
                    list.Add(new GridCase(code, v, null));
                }
            }
        }

        // Which fraction is chosen, and how large a numerator or number is accepted.
        Probe(["# ?/?", "# ??/??", "# ???/???", "?/?", "??/??", "# ?/4", "# ??/10", "0/0"],
            [0.9, 0.7, 0.45, 0.6, 0.95, 0.99, 0.123, 0.0625, 0.333, 0.667, 0.875, 2.9, 0.05, 0.04, 0.02, 0.51, 0.4, 0.14, 3.14159265358979, 0.001, 0.999]);
        // Whether a condition makes the section responsible for the minus sign.
        Probe(["[<=0]\"n\";0", "[<-3]\"a\";0", "[<5]\"s\";0.0", "[<5]0;0.0", "[=-5]\"x\";0", "[<0]0;[>=0]0", "[<0]0.0;0.00", "[<=-3]0;0.0", "[<>-5]0;0.0", "[>0]0;[<0]0", "[<0]0;[<0]0.0;0.00"],
            [-5, -3, -1, -0.2, 0, 1, 5]);
        // The largest numbers each kind of format accepts.
        Probe(["0", "#,##0", "0.00", "General", "0.00E+00", "@", "yyyy", "[h]", "0%", "0.0,,", "# ?/?", "0 0/0"],
            [1e14, 9.99999999999999e14, 1e15, 1e16, 1e20, 1e22, 1e27, 1e28, 1e29, 1e30, 1e50, 1e100, 1e300, -1e20, 99999999999999999.0]);
        // Where General turns to scientific notation.
        Probe(["General", "0.###############"],
            [1e-4, 1.5e-4, 1e-5, 1e-8, 1e-9, 1.2345e-8, 1.2345e-9, 1.2e-10, 0.00012345678, 0.123456789012345, 0.0123456789012345, 0.00123456789012345,
             1234567890123, 123456789012, 12345678901, 1234567890.12345, -0.00001, 1e10, 1e11, 123456.789012345678]);
        // Calendars and eras given by a locale tag.
        Probe(["[$-41E]yyyy", "[$-41E]bbbb", "[$-41E]bb", "[$-41E]d/m/yyyy", "[$-1041E]bbbb", "[$-D07041E]d mmmm yyyy", "[$-404]yyyy", "[$-404]ee", "[$-404]ggg e", "[$-404]e",
              "[$-411]e", "[$-411]ge", "[$-411]gggee", "[$-804]e", "[$-412]e", "[$-412]yyyy", "[$-404]gge\"年\"", "[$-11]yyyy", "[$-F800]d mmmm yyyy", "[$-409]yyyy"],
            [1, 45000, 36526, 28000, 43585, 43586, 15000]);
        // Chinese numerals.
        Probe(["[DBNum1][$-411]0", "[DBNum2][$-411]0", "[DBNum1][$-404]0", "[DBNum2][$-404]0", "[DBNum3][$-804]0", "[DBNum3][$-404]0", "[DBNum1][$-412]0", "[DBNum2][$-412]0",
              "[DBNum3][$-411]0", "[DBNum1][$-804]0.00", "[DBNum2][$-804]0.0", "[DBNum1][$-804]#,##0", "[DBNum1][$-804]h:mm", "[DBNum1][$-804]d", "[DBNum2][$-804]d", "[DBNum1][$-411]d", "[DBNum2][$-804]yyyy"],
            [0, 5, 12, 105, 10, 100, 1000, 10000, 100000000, 1234.5678, -12, 20, 11, 45000.5]);

        return list;
    }

    private static readonly string[] NumberCodes =
    [
        "General", "0", "0.0", "0.00", "0.000", "00", "000", "00000", "#", "##", "#.#", "#.##", "#.00", ".00", "0.#", "0.0#", "#.0#", "#.000", "####.#", "???.???", "0.0?", "?",
        "#,##0", "#,##0.00", "#,###", "#,###.00", "0,000", "#,", "#,##0,", "#,##0.0,,", "0.0,,", "0.0,,\"M\"", "#.0,", "0,",
        "0%", "0.0%", "0.00%", "#%", "0%%", "0.0%%",
        "0.00E+00", "0.00E-00", "0.0E+0", "#0.0E+0", "##0.0E+0", "0E+00", "0.00e+00", "#.##E+0", "00.00E+00", "0.0000E+000",
        "# ?/?", "# ??/??", "# ???/???", "?/?", "??/??", "# ?/8", "# ??/16", "# ?/100", "0 0/0", "#/#",
        "000-0000", "(###) ###-####", "00000", "0\" units\"", "\"x\"0", "\\x0", "0\\ \\x", "$0.00", "\"$\"#,##0.00", "$#,##0.00_);($#,##0.00)", "#,##0.00\\ \"€\"", "[$€-407]#,##0.00", "[$USD] 0",
        "[Red]0.00", "[Blue]0", "[Color10]0.0", "0.00_)", "_(* #,##0_);_(* \\(#,##0\\);_(* \"-\"_);_(@_)", "* 0", "0*-", "General\" units\"", "\"x\"General", "@", "0.00@",
        "[DBNum1]0", "[DBNum2]0", "[DBNum3]0", "[DBNum1][$-804]0", "[DBNum1][$-411]0", "[$-411][DBNum3]0",
    ];

    private static readonly string[] DateCodes =
    [
        "yy", "yyyy", "y", "yyy", "m", "mm", "mmm", "mmmm", "mmmmm", "d", "dd", "ddd", "dddd", "m/d/yyyy", "mm/dd/yy", "d-mmm-yy", "d-mmm", "mmm-yy", "dddd, mmmm dd, yyyy", "yyyy-mm-dd", "yyyy-mm-dd hh:mm:ss",
        "h", "hh", "h:mm", "hh:mm", "h:mm:ss", "hh:mm:ss", "h AM/PM", "h:mm AM/PM", "h:mm:ss AM/PM", "h:mm:ss A/P", "h:mm am/pm", "h:mm a/p", "h:mm:ss.0", "h:mm:ss.00", "h:mm:ss.000", "mm:ss", "mm:ss.0", "m:ss", "ss", "s", "s.00",
        "[h]:mm", "[h]:mm:ss", "[hh]:mm", "[m]", "[mm]:ss", "[ss]", "[s].00", "[h]", "[h] \"h\" m \"m\"", "d \"days\" h:mm", "m/d/yy h:mm", "mm-dd-yy", "yyyy/m/d", "hh\"h\"mm\"m\"", "mmmm d, yyyy", "dd.mm.yyyy",
        "[$-407]d. mmmm yyyy", "[$-407]dddd", "[$-40C]d mmmm yyyy", "[$-40C]dddd", "[$-C0A]dddd d \"de\" mmmm", "[$-411]yyyy\"年\"m\"月\"d\"日\"", "[$-411]dddd", "[$-411]ggge\"年\"", "[$-411]gge/m/d", "[$-404]e/m/d", "[$-804]yyyy\"年\"m\"月\"d\"日\"", "[$-412]yyyy-mm-dd", "[$-419]d mmmm yyyy",
        "[$-409]mmmm", "[$-F800]", "[$-F400]", "[$-F800]dddd", "[DBNum1]yyyy", "[DBNum1][$-804]yyyy\"年\"m\"月\"d\"日\"", "[DBNum2][$-804]m\"月\"d\"日\"", "[DBNum3][$-411]yyyy/m/d",
        "AM/PM h:mm", "上午/下午 hh\"時\"mm\"分\"", "[$-411]AM/PM h:mm", "bbbb", "[$-41E]d mmmm bbbb",
    ];

    private static readonly string[] TextCodes =
    [
        "@", "\"x\"@", "@\"y\"", "@@", "0;@", "0;0;0;@", "0;0;0;\"t:\"@", "0;0;0;\"none\"", "0.00;-0.00;;@", "General;General;General;@", ";;;", ";;;@", "@_)", "*-@",
    ];

    private static readonly string[] SectionCodes =
    [
        "0;0", "0;-0", "0;(0)", "0.0;(0.0);\"zero\"", "0;;", ";0", ";;0", "0;-0;;@", "[Red]0;[Blue]0;[Green]0", "[>100]\"big\";0.0", "[>100]\"big\";[<=100]0.0;0.00", "[<=100]0.0;[>100]\"big\"", "[=0]\"zero\";0.0", "[<0]\"neg\";0.0", "[<0]\"neg\";\"nonneg\"",
        "[>100]0;[>10]0.0;0.00", "[<>0]0.0;\"zero\"", "[>=100]0;[>=10]0.0;0.00", "[Red][<=100];[Blue][>100]", "[<=100][Red]0;[Blue]0", "[<0]-0;0", "[>0]\"p\";[<0]\"n\";\"z\"", "[>1000]#,##0,\"K\";0",
    ];
}
