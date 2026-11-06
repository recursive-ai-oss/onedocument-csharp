using System.Buffers.Binary;
using System.Globalization;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using System.Linq;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>One record of a buffered group: its type, bytes and offset in the part.</summary>
internal readonly record struct XlsbGroupRecord(int Type, byte[] Data, long Offset);

/// <summary>
/// Worksheet features that map to SpreadsheetML elements: data validation, sheet protection, conditional formatting and filters. Each
/// group of records is translated whole; when a group holds a record this layer does not know, the caller keeps the whole group as raw records.
/// </summary>
internal sealed class XlsbFeatureReader(OpenContext context, string partName, XlsbFormulaContext formulas)
{
    internal static readonly string[] ValidationTypes = ["none", "whole", "decimal", "list", "date", "time", "textLength", "custom"];
    internal static readonly string[] ValidationOperators = ["between", "notBetween", "equal", "notEqual", "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual"];
    internal static readonly string[] ErrorStyles = ["stop", "warning", "information"];
    internal static readonly string[] ImeModes = ["noControl", "on", "off", "disabled", "hiragana", "fullKatakana", "halfKatakana", "fullAlpha", "halfAlpha", "fullHangul", "halfHangul"];

    /// <summary>Reads an UncheckedSqRfX ([MS-XLSB] §2.5.156) as a space-separated list of ranges; null when the data ends early or a range is invalid.</summary>
    internal static string? ReadSqRef(ref XlsbRecordFields f, out int firstRow, out int firstColumn)
    {
        firstRow = firstColumn = 0;
        int count = f.I32();
        if (f.Overrun || count < 0 || count > f.Remaining / 16)
        {
            return null;
        }

        var parts = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int r1 = f.I32();
            int r2 = f.I32();
            int c1 = f.I32();
            int c2 = f.I32();
            if (r1 < 0 || r2 < r1 || r2 > 1048575 || c1 < 0 || c2 < c1 || c2 > 16383)
            {
                return null;
            }

            if (i == 0)
            {
                firstRow = r1;
                firstColumn = c1;
            }

            parts.Add(XlsbAddress.Range(r1, r2, c1, c2));
        }

        return string.Join(' ', parts);
    }

    /// <summary>Reads a formula stored as <c>cce, rgce, cb, rgcb</c> (a ParsedFormula) and prints it; null when it cannot be read.</summary>
    internal string? ReadFormula(ref XlsbRecordFields f, int originRow, int originColumn, long offset)
    {
        uint cce = f.U32();
        if (f.Overrun || cce > f.Remaining)
        {
            return null;
        }

        byte[] rgce = f.Bytes((int)cce).ToArray();
        uint cb = f.Remaining >= 4 ? f.U32() : 0;
        if (cb > f.Remaining)
        {
            return null;
        }

        byte[] rgcb = f.Bytes((int)cb).ToArray();
        if (cce == 0)
        {
            return string.Empty;
        }

        if (XlsbFormula.TryDecode(rgce, rgcb, formulas, XlsbFormulaKind.Name, originRow, originColumn, out FormulaNode node, out string? problem))
        {
            return FormulaPrinter.Print(node, new FormulaPrintOptions { Form = FormulaForm.File });
        }

        context.Defects.Report(new Defect(DefectKind.Formula, "xlsb.formula-unreadable", DefectSeverity.Dropped,
            "A formula could not be read (" + problem + "); the feature that holds it is kept as binary records.", DefectLocation.InRecord(partName, offset)));
        return null;
    }

    // ------------------------------------------------------------------ data validation

    /// <summary>BrtBeginDVals … BrtEndDVals ([MS-XLSB] §2.4.55, §2.4.356).</summary>
    public bool DataValidations(List<XlsbGroupRecord> group, XmlSink x)
    {
        var body = new XmlSink();
        int count = 0;
        XmlSink head = new();
        foreach (XlsbGroupRecord r in group)
        {
            switch (r.Type)
            {
                case XlsbRecordType.BeginDVals:
                {
                    var f = new XlsbRecordFields(r.Data);
                    int flags = f.U16();
                    uint xLeft = f.U32();
                    uint yTop = f.U32();
                    f.U32();
                    f.U32();
                    if (f.Overrun)
                    {
                        return false;
                    }

                    if ((flags & 1) != 0)
                    {
                        head.Attr("disablePrompts", 1);
                    }

                    if (xLeft != 0)
                    {
                        head.Attr("xWindow", (long)xLeft);
                    }

                    if (yTop != 0)
                    {
                        head.Attr("yWindow", (long)yTop);
                    }

                    break;
                }

                case XlsbRecordType.DVal:
                    if (!Validation(r, body))
                    {
                        return false;
                    }

                    count++;
                    break;
                case XlsbRecordType.EndDVals:
                    break;
                default:
                    return false;
            }
        }

        x.Raw("<dataValidations").Attr("count", count).Raw(head.Memory.Span).Raw(">").Raw(body.Memory.Span).Raw("</dataValidations>");
        return true;
    }

    private bool Validation(XlsbGroupRecord r, XmlSink x)
    {
        var f = new XlsbRecordFields(r.Data);
        uint flags = f.U32();
        string? sqref = ReadSqRef(ref f, out int row, out int column);
        string? errorTitle = f.NullableWideString();
        string? error = f.NullableWideString();
        string? promptTitle = f.NullableWideString();
        string? prompt = f.NullableWideString();
        if (sqref is null || f.Overrun)
        {
            return false;
        }

        string? f1 = ReadFormula(ref f, row, column, r.Offset);
        string? f2 = ReadFormula(ref f, row, column, r.Offset);
        if (f1 is null || f2 is null)
        {
            return false;
        }

        int valType = (int)(flags & 0xF);
        int errStyle = (int)((flags >> 4) & 7);
        int ime = (int)((flags >> 10) & 0xFF);
        int op = (int)((flags >> 20) & 0xF);
        if (op >= ValidationOperators.Length || errStyle >= ErrorStyles.Length || valType >= ValidationTypes.Length)
        {
            return false;
        }

        x.Raw("<dataValidation");
        if (valType != 0)
        {
            x.Attr("type", ValidationTypes[valType]);
        }

        if (errStyle != 0)
        {
            x.Attr("errorStyle", ErrorStyles[errStyle]);
        }

        if (ime != 0 && ime < ImeModes.Length)
        {
            x.Attr("imeMode", ImeModes[ime]);
        }

        if (op != 0 && valType is not (0 or 3 or 7))
        {
            x.Attr("operator", ValidationOperators[op]);
        }

        if ((flags & (1 << 8)) != 0)
        {
            x.Attr("allowBlank", 1);
        }

        if ((flags & (1 << 9)) != 0)
        {
            x.Attr("showDropDown", 1);
        }

        if ((flags & (1 << 18)) != 0)
        {
            x.Attr("showInputMessage", 1);
        }

        if ((flags & (1 << 19)) != 0)
        {
            x.Attr("showErrorMessage", 1);
        }

        if (!string.IsNullOrEmpty(errorTitle))
        {
            x.Attr("errorTitle", errorTitle);
        }

        if (!string.IsNullOrEmpty(error))
        {
            x.Attr("error", error);
        }

        if (!string.IsNullOrEmpty(promptTitle))
        {
            x.Attr("promptTitle", promptTitle);
        }

        if (!string.IsNullOrEmpty(prompt))
        {
            x.Attr("prompt", prompt);
        }

        x.Attr("sqref", sqref).Raw(">");
        if (f1.Length > 0)
        {
            x.Raw("<formula1>").Text(f1).Raw("</formula1>");
        }

        if (f2.Length > 0)
        {
            x.Raw("<formula2>").Text(f2).Raw("</formula2>");
        }

        x.Raw("</dataValidation>");
        return true;
    }

    /// <summary>The conditional formatting group (BrtBeginConditionalFormatting … BrtEndConditionalFormatting).</summary>
    public bool ConditionalFormatting(List<XlsbGroupRecord> group, XmlSink x) => XlsbConditionalFormatting.ToXml(group, this, x);

    // ------------------------------------------------------------------ page breaks and ignored errors

    /// <summary>BrtBeginRwBrk / BrtBeginColBrk with their BrtBrk records ([MS-XLSB] §2.4.197, §2.4.315).</summary>
    public static bool Breaks(List<XlsbGroupRecord> group, XmlSink x)
    {
        bool rows = group[0].Type == XlsbRecordType.BeginRwBrk;
        var body = new XmlSink();
        int count = 0;
        int manual = 0;
        foreach (XlsbGroupRecord r in group.Skip(1))
        {
            if (r.Type == XlsbRecordType.Brk)
            {
                var f = new XlsbRecordFields(r.Data);
                uint id = f.U32();
                uint min = f.U32();
                uint max = f.U32();
                bool man = f.U32() != 0;
                bool pivot = f.U32() != 0;
                if (f.Overrun)
                {
                    return false;
                }

                body.Raw("<brk").Attr("id", (long)id);
                if (min != 0)
                {
                    body.Attr("min", (long)min);
                }

                if (max != 0)
                {
                    body.Attr("max", (long)max);
                }

                if (man)
                {
                    body.Attr("man", 1);
                    manual++;
                }
                else
                {
                    body.Attr("man", 0);
                }

                if (pivot)
                {
                    body.Attr("pt", 1);
                }

                body.Raw("/>");
                count++;
            }
            else if (r.Type is not (XlsbRecordType.EndRwBrk or XlsbRecordType.EndColBrk))
            {
                return false;
            }
        }

        string tag = rows ? "rowBreaks" : "colBreaks";
        x.Raw("<").Raw(tag).Attr("count", count).Attr("manualBreakCount", manual).Raw(">").Raw(body.Memory.Span).Raw("</").Raw(tag).Raw(">");
        return true;
    }

    internal static readonly string[] IgnoredErrorFlags =
    [
        "evalError", "emptyCellReference", "numberStoredAsText", "formulaRange", "formula", "twoDigitTextYear", "unlockedFormula", "listDataValidation", "calculatedColumn",
    ];

    /// <summary>BrtBeginCellIgnoreECs … BrtEndCellIgnoreECs ([MS-XLSB] §2.4.16, §2.4.322).</summary>
    public static bool IgnoredErrors(List<XlsbGroupRecord> group, XmlSink x)
    {
        var body = new XmlSink();
        foreach (XlsbGroupRecord r in group.Skip(1))
        {
            if (r.Type == XlsbRecordType.CellIgnoreEC)
            {
                var f = new XlsbRecordFields(r.Data);
                uint flags = f.U32();
                string? sqref = ReadSqRef(ref f, out _, out _);
                if (sqref is null || f.Overrun)
                {
                    return false;
                }

                body.Raw("<ignoredError").Attr("sqref", sqref);
                for (int i = 0; i < IgnoredErrorFlags.Length; i++)
                {
                    if ((flags & (1u << i)) != 0)
                    {
                        body.Attr(IgnoredErrorFlags[i], 1);
                    }
                }

                body.Raw("/>");
            }
            else if (r.Type != XlsbRecordType.EndCellIgnoreECs)
            {
                return false;
            }
        }

        x.Raw("<ignoredErrors>").Raw(body.Memory.Span).Raw("</ignoredErrors>");
        return true;
    }

    // ------------------------------------------------------------------ sheet protection

    private static readonly string[] ProtectionFlags =
    [
        "sheet", "objects", "scenarios", "formatCells", "formatColumns", "formatRows", "insertColumns", "insertRows", "insertHyperlinks", "deleteColumns",
        "deleteRows", "selectLockedCells", "sort", "autoFilter", "pivotTables", "selectUnlockedCells",
    ];

    /// <summary>BrtSheetProtection ([MS-XLSB] §2.4.794): the file stores what is allowed, the markup what is locked, so every flag but the first is inverted.</summary>
    public static bool Protection(XlsbGroupRecord r, XmlSink x)
    {
        var f = new XlsbRecordFields(r.Data);
        int password = f.U16();
        var flags = new int[ProtectionFlags.Length];
        for (int i = 0; i < flags.Length; i++)
        {
            flags[i] = f.I32();
        }

        if (f.Overrun)
        {
            return false;
        }

        x.Raw("<sheetProtection");
        if (password != 0)
        {
            x.Attr("password", password.ToString("X4", CultureInfo.InvariantCulture));
        }

        for (int i = 0; i < flags.Length; i++)
        {
            bool on = i == 0 ? flags[i] != 0 : flags[i] == 0;
            x.Attr(ProtectionFlags[i], on ? 1 : 0);
        }

        x.Raw("/>");
        return true;
    }

    internal static string[] ProtectionNames => ProtectionFlags;

    internal static int ReadInt(ReadOnlySpan<byte> d, int at) => d.Length >= at + 4 ? BinaryPrimitives.ReadInt32LittleEndian(d[at..]) : 0;
}

/// <summary>The XML-to-binary half of <see cref="XlsbFeatureReader"/>.</summary>
internal sealed class XlsbFeatureWriter(XlsbRecordWriter w, XlsbRecordData d, OpenContext context, string partName, XlsbFormulaContext cx, ILossSink loss)
{
    private FormulaNode? Parse(string text, string what)
    {
        var options = new FormulaParserOptions { Limits = context.Limits };
        if (FormulaParser.TryParse(text, out FormulaNode? node, out FormulaSyntaxError error, options))
        {
            return node;
        }

        loss.Report("xlsb.formula-unparsed", "formula that could not be parsed", 1,
            "The formula of " + what + " in " + partName + " could not be parsed (" + error.Code + "); the feature is not written.");
        return null;
    }

    /// <summary>Encodes a formula (reference offsets are relative to the given cell); false when it cannot be parsed.</summary>
    internal bool TryEncode(string text, int originRow, int originColumn, string what, out byte[] rgce, out byte[] rgcb)
    {
        rgce = [];
        rgcb = [];
        FormulaNode? node = Parse(text, what);
        if (node is null)
        {
            return false;
        }

        if (!XlsbFormula.TryEncode(node, cx, XlsbFormulaKind.Name, originRow, originColumn, out rgce, out rgcb, out string? problem))
        {
            loss.Report("xlsb.formula-approximated", "formula written approximately", 1, "A formula could not be encoded exactly: " + problem);
        }

        return true;
    }

    /// <summary>Writes <c>cce, rgce, cb, rgcb</c> for a formula; false when it cannot be parsed.</summary>
    internal bool WriteFormula(string text, int originRow, int originColumn, string what, XlsbRecordData target)
    {
        if (text.Length == 0)
        {
            target.U32(0);
            target.U32(0);
            return true;
        }

        if (!TryEncode(text, originRow, originColumn, what, out byte[] rgce, out byte[] rgcb))
        {
            return false;
        }

        target.U32((uint)rgce.Length);
        target.Bytes(rgce);
        target.U32((uint)rgcb.Length);
        target.Bytes(rgcb);
        return true;
    }

    /// <summary>Writes an UncheckedSqRfX from a space-separated reference list; false when no range is valid.</summary>
    internal static bool WriteSqRef(string sqref, XlsbRecordData target, out int firstRow, out int firstColumn)
    {
        firstRow = firstColumn = 0;
        var ranges = new List<(int, int, int, int)>();
        foreach (string part in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (XlsbAddress.TryParseRange(part, out int r1, out int r2, out int c1, out int c2))
            {
                ranges.Add((Math.Min(r1, r2), Math.Max(r1, r2), Math.Min(c1, c2), Math.Max(c1, c2)));
            }
        }

        if (ranges.Count == 0)
        {
            return false;
        }

        firstRow = ranges[0].Item1;
        firstColumn = ranges[0].Item3;
        target.I32(ranges.Count);
        foreach ((int r1, int r2, int c1, int c2) in ranges)
        {
            target.I32(r1);
            target.I32(r2);
            target.I32(c1);
            target.I32(c2);
        }

        return true;
    }

    private static int IndexOf(string[] names, string value, int fallback)
    {
        int i = Array.IndexOf(names, value);
        return i < 0 ? fallback : i;
    }

    /// <summary>The <c>dataValidations</c> element as BrtBeginDVals, a BrtDVal per rule and BrtEndDVals.</summary>
    public void DataValidations(MarkupNode c)
    {
        var rules = new List<byte[]>();
        foreach (MarkupNode v in c.Elements("dataValidation"))
        {
            var rec = new XlsbRecordData();
            int type = IndexOf(XlsbFeatureReader.ValidationTypes, v.Str("type", "none"), 0);
            int style = IndexOf(XlsbFeatureReader.ErrorStyles, v.Str("errorStyle", "stop"), 0);
            int op = IndexOf(XlsbFeatureReader.ValidationOperators, v.Str("operator", "between"), 0);
            int ime = IndexOf(XlsbFeatureReader.ImeModes, v.Str("imeMode", "noControl"), 0);
            uint flags = (uint)type | ((uint)style << 4) | (v.Bool("allowBlank") ? 1u << 8 : 0) | (v.Bool("showDropDown") ? 1u << 9 : 0)
                | ((uint)ime << 10) | (v.Bool("showInputMessage") ? 1u << 18 : 0) | (v.Bool("showErrorMessage") ? 1u << 19 : 0) | ((uint)op << 20);
            rec.U32(flags);
            if (!WriteSqRef(v.Str("sqref"), rec, out int row, out int col))
            {
                loss.Report("xlsb.validation-dropped", "data validation without a valid range", 1, "A data validation has no valid cell range and is not written.");
                continue;
            }

            rec.NullableWideString(v.GetAttribute("errorTitle"));
            rec.NullableWideString(v.GetAttribute("error"));
            rec.NullableWideString(v.GetAttribute("promptTitle"));
            rec.NullableWideString(v.GetAttribute("prompt"));
            string what = "a data validation on " + v.Str("sqref");
            if (!WriteFormula(v.Element("formula1").IsNull ? string.Empty : v.Element("formula1").InnerText, row, col, what, rec)
                || !WriteFormula(v.Element("formula2").IsNull ? string.Empty : v.Element("formula2").InnerText, row, col, what, rec))
            {
                continue;
            }

            rules.Add(rec.Span.ToArray());
        }

        if (rules.Count == 0)
        {
            return;
        }

        d.U16(c.Bool("disablePrompts") ? 1 : 0);
        d.U32((uint)c.Long("xWindow"));
        d.U32((uint)c.Long("yWindow"));
        d.U32(0);
        d.U32((uint)rules.Count);
        w.Write(XlsbRecordType.BeginDVals, d);
        foreach (byte[] rule in rules)
        {
            w.Write(XlsbRecordType.DVal, rule);
        }

        w.Write(XlsbRecordType.EndDVals);
    }

    /// <summary>The <c>sheetProtection</c> element as BrtSheetProtection.</summary>
    public void Protection(MarkupNode c)
    {
        string pw = c.Str("password");
        d.U16(pw.Length > 0 && int.TryParse(pw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int verifier) ? verifier & 0xFFFF : 0);
        string[] names = XlsbFeatureReader.ProtectionNames;
        for (int i = 0; i < names.Length; i++)
        {
            // Defaults of the markup: everything but the select flags and the sheet/object/scenario switches is locked.
            bool locked = c.Bool(names[i], i is 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 12 or 13 or 14);
            d.I32(i == 0 ? (locked ? 1 : 0) : (locked ? 0 : 1));
        }

        w.Write(XlsbRecordType.SheetProtection, d);
    }

    /// <summary>The <c>rowBreaks</c> or <c>colBreaks</c> element as BrtBeginRwBrk / BrtBeginColBrk with a BrtBrk per break.</summary>
    public void Breaks(MarkupNode c, bool rows)
    {
        var list = c.Elements("brk").ToList();
        if (list.Count == 0)
        {
            return;
        }

        d.U32((uint)list.Count);
        d.U32((uint)list.Count);
        w.Write(rows ? XlsbRecordType.BeginRwBrk : XlsbRecordType.BeginColBrk, d);
        foreach (MarkupNode b in list)
        {
            d.U32((uint)b.Int("id"));
            d.U32((uint)b.Int("min"));
            d.U32((uint)b.Int("max", rows ? 16383 : 1048575));
            d.U32(b.Bool("man", true) ? 1u : 0);
            d.U32(b.Bool("pt") ? 1u : 0);
            w.Write(XlsbRecordType.Brk, d);
        }

        w.Write(rows ? XlsbRecordType.EndRwBrk : XlsbRecordType.EndColBrk);
    }

    /// <summary>The <c>ignoredErrors</c> element as BrtBeginCellIgnoreECs with a BrtCellIgnoreEC per entry.</summary>
    public void IgnoredErrors(MarkupNode c)
    {
        var list = c.Elements("ignoredError").ToList();
        if (list.Count == 0)
        {
            return;
        }

        w.Write(XlsbRecordType.BeginCellIgnoreECs);
        foreach (MarkupNode e in list)
        {
            uint flags = 0;
            for (int i = 0; i < XlsbFeatureReader.IgnoredErrorFlags.Length; i++)
            {
                if (e.Bool(XlsbFeatureReader.IgnoredErrorFlags[i]))
                {
                    flags |= 1u << i;
                }
            }

            d.U32(flags);
            if (WriteSqRef(e.Str("sqref"), d, out _, out _))
            {
                w.Write(XlsbRecordType.CellIgnoreEC, d);
            }
            else
            {
                d.Clear();
            }
        }

        w.Write(XlsbRecordType.EndCellIgnoreECs);
    }
}
