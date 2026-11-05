using System;
using System.Collections.Generic;
using System.Globalization;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>Information functions: the <c>IS*</c> tests, <c>N</c>, <c>T</c>, <c>TYPE</c>, <c>ERROR.TYPE</c>, <c>CELL</c>, <c>INFO</c>, <c>SHEET</c>, <c>SHEETS</c>, formula inspection.</summary>
internal static class InformationFunctions
{
    private static readonly ArgKind[] V = [ArgKind.Value];
    private static readonly ArgKind[] Any = [ArgKind.Any];

    public static void Register(FunctionRegistry r)
    {
        Test(r, "ISBLANK", v => v.IsBlank);
        Test(r, "ISNUMBER", v => v.Kind == ValueKind.Number);
        Test(r, "ISTEXT", v => v.Kind == ValueKind.Text);
        Test(r, "ISNONTEXT", v => v.Kind != ValueKind.Text);
        Test(r, "ISLOGICAL", v => v.Kind == ValueKind.Boolean);
        Test(r, "ISERR", v => v.IsError && v.Error != CalcError.NA);
        Test(r, "ISNA", v => v.IsError && v.Error == CalcError.NA);
        r.Add(FunctionDefinition.Create("ISEVEN", 1, 1, V, (_, a) => Parity(a[0], 0)));
        r.Add(FunctionDefinition.Create("ISODD", 1, 1, V, (_, a) => Parity(a[0], 1)));
        r.Add(FunctionDefinition.Create("ISREF", 1, 1, Any, (_, a) => CalcValue.FromBoolean(a[0].Kind == ValueKind.Reference)));
        r.Add(FunctionDefinition.Create("ISFORMULA", 1, 1, Any, IsFormula));
        r.Add(FunctionDefinition.Create("FORMULATEXT", 1, 1, Any, FormulaText));
        r.Add(FunctionDefinition.Create("N", 1, 1, V, N));
        r.Add(FunctionDefinition.Create("T", 1, 1, V, (_, a) => a[0].Kind == ValueKind.Text ? a[0] : CalcValue.EmptyText));
        r.Add(FunctionDefinition.Create("NA", 0, 0, V, (_, _) => Fx.Err(CalcError.NA)));
        r.Add(FunctionDefinition.Create("TYPE", 1, 1, Any, TypeOf));
        r.Add(FunctionDefinition.Create("ERROR.TYPE", 1, 1, V, ErrorType, FunctionTraits.HandlesErrors));
        r.Add(FunctionDefinition.Create("SHEET", 0, 1, Any, Sheet));
        r.Add(FunctionDefinition.Create("SHEETS", 0, 1, Any, Sheets));
        r.Add(FunctionDefinition.Create("CELL", 1, 2, [ArgKind.Value, ArgKind.Any], Cell, FunctionTraits.UsesOrigin));
        r.Add(FunctionDefinition.Create("INFO", 1, 1, V, Info));
    }

    // The IS* tests see errors instead of propagating them.
    private static void Test(FunctionRegistry r, string name, Func<CalcValue, bool> test) =>
        r.Add(FunctionDefinition.Create(name, 1, 1, V, (_, a) => CalcValue.FromBoolean(test(a[0])), FunctionTraits.HandlesErrors));

    // ISEVEN and ISODD truncate the number; text that is not a number and booleans give #VALUE!.
    private static CalcValue Parity(in CalcValue v, int want)
    {
        if (v.Kind == ValueKind.Boolean)
        {
            return Fx.Err(CalcError.Value);
        }

        if (!Fx.TryNum(v, out double d, out CalcValue e))
        {
            return e;
        }

        return CalcValue.FromBoolean(Math.Abs(Math.Truncate(d) % 2) == want);
    }

    private static CalcValue N(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcValue v = a[0];
        return v.Kind switch
        {
            ValueKind.Number => v,
            ValueKind.Boolean => CalcValue.FromNumber(v.Boolean ? 1 : 0),
            _ => CalcValue.FromNumber(0),
        };
    }

    private static CalcValue TypeOf(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcValue v = a[0];
        if (v.Kind == ValueKind.Reference)
        {
            CalcReference rf = v.Reference!;
            if (!rf.IsSingleCell)
            {
                return CalcValue.FromNumber(64);
            }

            v = ev.Dereference(v);
        }

        return v.Kind switch
        {
            ValueKind.Number or ValueKind.Blank => CalcValue.FromNumber(1),
            ValueKind.Text => CalcValue.FromNumber(2),
            ValueKind.Boolean => CalcValue.FromNumber(4),
            ValueKind.Error => CalcValue.FromNumber(16),
            ValueKind.Array => CalcValue.FromNumber(64),
            _ => Fx.Err(CalcError.Calc),
        };
    }

    // Microsoft's ERROR.TYPE reference lists #NULL! to #GETTING_DATA as 1-8 and the newer errors after them.
    private static CalcValue ErrorType(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!a[0].IsError)
        {
            return Fx.Err(CalcError.NA);
        }

        int n = a[0].Error switch
        {
            CalcError.Null => 1,
            CalcError.Div0 => 2,
            CalcError.Value => 3,
            CalcError.Ref => 4,
            CalcError.Name => 5,
            CalcError.Num => 6,
            CalcError.NA => 7,
            CalcError.GettingData => 8,
            CalcError.Spill => 9,
            CalcError.Connect => 10,
            CalcError.Blocked => 11,
            CalcError.Unknown => 12,
            CalcError.Field => 13,
            CalcError.Calc => 14,
            _ => 15,
        };
        return CalcValue.FromNumber(n);
    }

    // ---- formula inspection through the resolver --------------------------------------------------------------------------------

    private static CalcValue PerCell(Evaluator ev, in CalcValue arg, Func<string?, int, int, CalcValue> f)
    {
        if (arg.Kind != ValueKind.Reference)
        {
            return Fx.Err(CalcError.Value);
        }

        CalcReference rf = arg.Reference!;
        if (rf.AreaCount != 1 || rf.IsSpill)
        {
            return Fx.Err(CalcError.Value);
        }

        CellArea a = rf.First;
        string? sheet = a.Sheet ?? ev.Origin.Sheet;
        if (a.IsSingleCell)
        {
            return f(sheet, a.Row1, a.Column1);
        }

        CalcValue[] cells = ev.NewCells(a.Rows, a.Columns);
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                cells[(r * a.Columns) + c] = f(sheet, a.Row1 + r, a.Column1 + c);
            }
        }

        return CalcValue.FromArray(new CalcArray(a.Rows, a.Columns, cells));
    }

    private static CalcValue IsFormula(Evaluator ev, ReadOnlySpan<CalcValue> a) =>
        a[0].IsError ? a[0] : PerCell(ev, a[0], (s, r, c) => CalcValue.FromBoolean(ev.Resolver.IsFormula(s, r, c)));

    private static CalcValue FormulaText(Evaluator ev, ReadOnlySpan<CalcValue> a) =>
        a[0].IsError ? a[0] : PerCell(ev, a[0], (s, r, c) => ev.Resolver.GetFormulaText(s, r, c) is { } t ? CalcValue.FromText(t) : Fx.Err(CalcError.NA));

    private static CalcValue Sheet(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string? name;
        if (a.Length == 0 || a[0].IsMissing)
        {
            name = ev.Origin.Sheet;
        }
        else if (a[0].Kind == ValueKind.Reference)
        {
            name = a[0].Reference!.First.Sheet ?? ev.Origin.Sheet;
        }
        else if (a[0].Kind == ValueKind.Text)
        {
            name = a[0].Text;
        }
        else
        {
            return a[0].IsError ? a[0] : Fx.Err(CalcError.NA);
        }

        int i = ev.Resolver.GetSheetIndex(name);
        return i > 0 ? CalcValue.FromNumber(i) : Fx.Err(CalcError.NA);
    }

    private static CalcValue Sheets(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (a.Length == 0 || a[0].IsMissing)
        {
            return CalcValue.FromNumber(ev.Resolver.GetSheetCount());
        }

        if (a[0].Kind != ValueKind.Reference)
        {
            return a[0].IsError ? a[0] : Fx.Err(CalcError.Value);
        }

        CellArea first = a[0].Reference!.First;
        if (first.LastSheet is not null && first.Sheet is not null && ev.Resolver.TryExpandSheets(first.Sheet, first.LastSheet, out IReadOnlyList<string> sheets))
        {
            return CalcValue.FromNumber(sheets.Count);
        }

        return CalcValue.FromNumber(1);
    }

    // ---- CELL and INFO -------------------------------------------------------------------------------------------------------------

    private static CalcValue Cell(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string kind = CalcConvert.ToText(a[0]).ToLowerInvariant();
        string? sheet = ev.Origin.Sheet;
        int row = ev.Origin.Row;
        int col = ev.Origin.Column;
        CalcArray? block = null;
        if (a.Length > 1 && !a[1].IsMissing)
        {
            if (a[1].Kind != ValueKind.Reference)
            {
                return a[1].IsError ? a[1] : Fx.Err(CalcError.Value);
            }

            CalcReference rf = a[1].Reference!;
            CellArea area = rf.First;
            sheet = area.Sheet ?? sheet;
            row = area.Row1;
            col = area.Column1;
            if (kind == "contents" || kind == "type")
            {
                if (ev.TryReadAreas(new CalcReference([area with { Row2 = area.Row1, Column2 = area.Column1 }]), out CalcArray[] blocks, out CalcError re))
                {
                    block = blocks[0];
                }
                else
                {
                    return Fx.Err(re);
                }
            }
        }

        CalcValue Content() => block is not null ? block[0, 0] : ev.Resolver.GetCell(sheet, row, col);
        CalcCellInfo info = ev.Resolver.GetCellInfo(sheet, row, col);
        switch (kind)
        {
            case "address":
                return CalcValue.FromText("$" + RefSyntax.ColumnName(col) + "$" + row.ToString(CultureInfo.InvariantCulture));
            case "col":
                return CalcValue.FromNumber(col);
            case "row":
                return CalcValue.FromNumber(row);
            case "color":
                return CalcValue.FromNumber(info.Color ? 1 : 0);
            case "parentheses":
                return CalcValue.FromNumber(info.Parentheses ? 1 : 0);
            case "protect":
                return CalcValue.FromNumber(info.Locked ? 1 : 0);
            case "prefix":
                return CalcValue.FromText(info.Prefix == '\0' ? string.Empty : info.Prefix.ToString());
            case "width":
                return CalcValue.FromNumber(Math.Floor(info.ColumnWidth));
            case "format":
                return CalcValue.FromText(FormatCode(info.NumberFormat));
            case "filename":
                return CalcValue.FromText(ev.Resolver.GetFileName(sheet));
            case "contents":
                return Content();
            case "type":
                {
                    CalcValue v = Content();
                    return CalcValue.FromText(v.IsBlank ? "b" : v.Kind == ValueKind.Text ? "l" : "v");
                }

            default:
                return Fx.Err(CalcError.Value);
        }
    }

    /// <summary>Maps a number format code to the short code <c>CELL("format")</c> reports (Microsoft's CELL reference table).</summary>
    internal static string FormatCode(string format)
    {
        string f = format;
        int semi = f.IndexOf(';', StringComparison.Ordinal);
        string first = semi >= 0 ? f[..semi] : f;
        bool negative = semi >= 0;
        string neg = negative ? f[(semi + 1)..] : string.Empty;
        bool red = neg.Contains("[Red]", StringComparison.OrdinalIgnoreCase);
        bool paren = neg.Contains('(', StringComparison.Ordinal);
        string plain = first.Replace("_)", string.Empty, StringComparison.Ordinal).Replace("_(", string.Empty, StringComparison.Ordinal);
        if (plain.Equals("General", StringComparison.OrdinalIgnoreCase) || plain == "@")
        {
            return "G";
        }

        string[] dates =
        [
            "m/d/yy", "d-mmm-yy", "d-mmm", "mmm-yy", "m/d/yy h:mm", "mm/dd", "h:mm:ss AM/PM", "h:mm AM/PM", "h:mm:ss", "h:mm",
        ];
        string[] codes = ["D4", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9"];
        for (int i = 0; i < dates.Length; i++)
        {
            if (string.Equals(plain, dates[i], StringComparison.OrdinalIgnoreCase))
            {
                return codes[i];
            }
        }

        if (plain.Contains("yy", StringComparison.OrdinalIgnoreCase) || plain.Contains("mm", StringComparison.OrdinalIgnoreCase) && !plain.Contains('0', StringComparison.Ordinal))
        {
            return "D4";
        }

        int dot = plain.IndexOf('.', StringComparison.Ordinal);
        int decimals = 0;
        if (dot >= 0)
        {
            for (int i = dot + 1; i < plain.Length && plain[i] == '0'; i++)
            {
                decimals++;
            }
        }

        string d = decimals.ToString(CultureInfo.InvariantCulture);
        if (plain.Contains("E+", StringComparison.OrdinalIgnoreCase))
        {
            return "S" + d;
        }

        if (plain.Contains('%', StringComparison.Ordinal))
        {
            return "P" + d;
        }

        if (plain.Contains('$', StringComparison.Ordinal))
        {
            return "C" + d + (red && paren ? "-" : string.Empty);
        }

        if (plain.Contains("#,##0", StringComparison.Ordinal))
        {
            return "," + d;
        }

        return "F" + d;
    }

    // INFO answers describe the application; a resolver may override any of them. The defaults are the values of Excel for Windows.
    private static CalcValue Info(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string type = CalcConvert.ToText(a[0]).ToLowerInvariant();
        if (ev.Resolver.GetInfo(type) is { } given)
        {
            return given;
        }

        return type switch
        {
            "directory" => CalcValue.FromText(string.Empty),
            "numfile" => CalcValue.FromNumber(1),
            "origin" => CalcValue.FromText("$A:$A$1"),
            "osversion" => CalcValue.FromText("Windows (32-bit) NT 10.00"),
            "recalc" => CalcValue.FromText("Automatic"),
            "release" => CalcValue.FromText("16.0"),
            "system" => CalcValue.FromText("pcdos"),
            _ => type is "memavail" or "memused" or "totmem" or "cpu" ? Fx.Err(CalcError.NA) : Fx.Err(CalcError.Value),
        };
    }
}
