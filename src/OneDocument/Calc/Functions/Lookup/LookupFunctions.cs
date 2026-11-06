using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>
/// Lookup and reference functions, written from Microsoft's function reference pages. Behaviour the pages leave open is marked
/// <c>// open:</c> and is settled by the Excel grids in <c>testdata/calc/lookup-grid</c>.
/// </summary>
internal static class LookupFunctions
{
    private static readonly ArgKind[] V = [ArgKind.Value];
    private static readonly ArgKind[] A = [ArgKind.Array];
    private static readonly ArgKind[] Any = [ArgKind.Any];

    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("MATCH", 2, 3, [ArgKind.Value, ArgKind.Array, ArgKind.Value], Match));
        r.Add(FunctionDefinition.Create("XMATCH", 2, 4, [ArgKind.Value, ArgKind.Array, ArgKind.Value, ArgKind.Value], XMatch));
        r.Add(FunctionDefinition.Create("XLOOKUP", 3, 6, [ArgKind.Value, ArgKind.Array, ArgKind.Any, ArgKind.Any, ArgKind.Value, ArgKind.Value], XLookup));
        r.Add(FunctionDefinition.Create("LOOKUP", 2, 3, [ArgKind.Value, ArgKind.Array, ArgKind.Array], Lookup));
        r.Add(FunctionDefinition.Create("VLOOKUP", 3, 4, [ArgKind.Value, ArgKind.Array, ArgKind.Value, ArgKind.Value], (c, a) => TableLookup(c, a, false)));
        r.Add(FunctionDefinition.Create("HLOOKUP", 3, 4, [ArgKind.Value, ArgKind.Array, ArgKind.Value, ArgKind.Value], (c, a) => TableLookup(c, a, true)));
        r.Add(FunctionDefinition.Create("CHOOSECOLS", 2, int.MaxValue, A, (c, a) => Choose(c, a, false), repeatLast: true));
        r.Add(FunctionDefinition.Create("CHOOSEROWS", 2, int.MaxValue, A, (c, a) => Choose(c, a, true), repeatLast: true));
        r.Add(FunctionDefinition.Create("OFFSET", 3, 5, [ArgKind.Any, ArgKind.Value, ArgKind.Value, ArgKind.Value, ArgKind.Value], Offset, FunctionTraits.Volatile));
        r.Add(FunctionDefinition.Create("INDIRECT", 1, 2, V, Indirect, FunctionTraits.Volatile | FunctionTraits.UsesOrigin));
        r.Add(FunctionDefinition.Create("ROW", 0, 1, Any, (c, a) => RowColumn(c, a, true), FunctionTraits.UsesOrigin));
        r.Add(FunctionDefinition.Create("COLUMN", 0, 1, Any, (c, a) => RowColumn(c, a, false), FunctionTraits.UsesOrigin));
        r.Add(FunctionDefinition.Create("AREAS", 1, 1, Any, Areas));
        r.Add(FunctionDefinition.Create("ADDRESS", 2, 5, V, Address));
        r.Add(FunctionDefinition.Create("TRANSPOSE", 1, 1, A, Transpose));
        r.Add(FunctionDefinition.Create("HYPERLINK", 1, 2, V, Hyperlink));
        r.Add(FunctionDefinition.Create("GETPIVOTDATA", 2, int.MaxValue, Any, GetPivotData, repeatLast: true));
        r.Add(FunctionDefinition.Create("TRIMRANGE", 1, 3, [ArgKind.Any, ArgKind.Value, ArgKind.Value], TrimRange));
    }

    private static CalcValue Err(CalcError e) => CalcValue.FromError(e);

    private static CalcValue Num(int n) => CalcValue.FromNumber(n);

    // Microsoft's GETPIVOTDATA page: data_field, then any cell of the pivot table, then field/item pairs. The pivot table itself is the resolver's
    // (it reads what the table shows); without one, or when the combination is not shown, the result is #REF!.
    private static CalcValue GetPivotData(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        CalcValue name = ev.ToScalar(args[0]);
        if (name.IsError)
        {
            return name;
        }

        if (args[1].Reference is not { } reference || args.Length % 2 != 0)
        {
            return Err(CalcError.Ref);
        }

        CellArea first = reference.First;
        var items = new List<KeyValuePair<string, CalcValue>>();
        for (int i = 2; i + 1 < args.Length; i += 2)
        {
            CalcValue field = ev.ToScalar(args[i]);
            CalcValue item = ev.ToScalar(args[i + 1]);
            if (field.IsError)
            {
                return field;
            }

            if (item.IsError)
            {
                return item;
            }

            items.Add(new KeyValuePair<string, CalcValue>(CalcConvert.ToText(field), item));
        }

        return ev.Resolver.GetPivotData(CalcConvert.ToText(name), first.Sheet ?? ev.Origin.Sheet, first.Row1, first.Column1, items);
    }

    // ---- MATCH ------------------------------------------------------------------------------------------------------------

    // Microsoft's MATCH page: match_type 1 (default) finds the largest value <= lookup_value in ascending data, 0 the first exact
    // match (wildcards in text), -1 the smallest value >= lookup_value in descending data. The array must be one row or one column.
    private static CalcValue Match(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[1], out CalcArray arr, out CalcValue error))
        {
            return error;
        }

        int type = 1;
        if (args.Length > 2)
        {
            // open: an omitted match_type between commas counts as 0.
            if (args[2].IsMissing)
            {
                type = 0;
            }
            else
            {
                CalcError e = CalcConvert.ToNumber(args[2], out double d);
                if (e != CalcError.None)
                {
                    return Err(e);
                }

                type = Math.Sign(Math.Truncate(d));
            }
        }

        if (!Vec.TryOf(arr, out Vec v))
        {
            return Err(CalcError.NA);
        }

        int i = type == 0 ? LookupSupport.FindExact(v, args[0], true, false) : LookupSupport.Binary(v, args[0], type > 0, false);
        return i < 0 ? Err(CalcError.NA) : Num(i + 1);
    }

    // ---- XMATCH / XLOOKUP ---------------------------------------------------------------------------------------------------

    private static bool ModeArgs(ReadOnlySpan<CalcValue> args, int matchIndex, out int mm, out int sm, out CalcValue error)
    {
        mm = 0;
        sm = 1;
        error = default;
        if (args.Length > matchIndex && !args[matchIndex].IsMissing)
        {
            if (!LookupSupport.TryInt(args[matchIndex], out mm, out error))
            {
                return false;
            }
        }

        if (args.Length > matchIndex + 1 && !args[matchIndex + 1].IsMissing)
        {
            if (!LookupSupport.TryInt(args[matchIndex + 1], out sm, out error))
            {
                return false;
            }
        }

        if (mm is < -1 or > 2 || sm is not (1 or -1 or 2 or -2))
        {
            error = Err(CalcError.Value);
            return false;
        }

        return true;
    }

    /// <summary>The XMATCH search: returns the 0-based position or -1.</summary>
    internal static int XSearch(in Vec v, in CalcValue target, int mm, int sm)
    {
        CalcValue t = LookupSupport.Normalise(target);
        if (sm is 2 or -2)
        {
            bool asc = sm == 2;
            int best = LookupSupport.Binary(v, t, asc, false);
            if (mm == 2)
            {
                // Wildcards need a linear scan; a binary search cannot use them.
                return LookupSupport.FindExact(v, t, true, false);
            }

            bool equal = best >= 0 && LookupSupport.CompareSameClass(v[best], t) == 0;
            if (equal)
            {
                return best;
            }

            if (mm == 0)
            {
                return -1;
            }

            // In ascending data the last element below the target is `best`; the next larger is best + 1. Descending is the mirror.
            bool wantsLower = mm == -1;
            if (asc == wantsLower)
            {
                return best;
            }

            int next = best + 1;
            return next < v.Length && LookupSupport.Class(v[next]) == LookupSupport.Class(t) ? next : -1;
        }

        bool reverse = sm == -1;
        if (mm is 0 or 2)
        {
            return LookupSupport.FindExact(v, t, mm == 2, reverse);
        }

        int found = -1;
        int n = v.Length;
        int cls = LookupSupport.Class(t);
        for (int k = 0; k < n; k++)
        {
            int i = reverse ? n - 1 - k : k;
            CalcValue x = v[i];
            if (LookupSupport.Class(x) != cls)
            {
                continue;
            }

            int c = LookupSupport.CompareSameClass(x, t);
            if (c == 0)
            {
                return i;
            }

            if ((mm == -1 && c < 0 && (found < 0 || LookupSupport.CompareSameClass(x, v[found]) > 0)) ||
                (mm == 1 && c > 0 && (found < 0 || LookupSupport.CompareSameClass(x, v[found]) < 0)))
            {
                found = i;
            }
        }

        return found;
    }

    private static CalcValue XMatch(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[1], out CalcArray arr, out CalcValue error))
        {
            return error;
        }

        if (!ModeArgs(args, 2, out int mm, out int sm, out error))
        {
            return error;
        }

        if (!Vec.TryOf(arr, out Vec v))
        {
            return Err(CalcError.Value);
        }

        int i = XSearch(v, args[0], mm, sm);
        return i < 0 ? Err(CalcError.NA) : Num(i + 1);
    }

    // Microsoft's XLOOKUP page: lookup_array is one row or column; return_array has the same length in that direction and the
    // result is the matching row or column of it. Not found gives if_not_found, or #N/A. Returns a reference when return_array is one.
    private static CalcValue XLookup(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[1], out CalcArray la, out CalcValue error))
        {
            return error;
        }

        if (!ModeArgs(args, 4, out int mm, out int sm, out error))
        {
            return error;
        }

        if (!Vec.TryOf(la, out Vec v))
        {
            return Err(CalcError.Value);
        }

        CalcValue rv = args[2];
        if (rv.IsError)
        {
            return rv;
        }

        bool lookupIsRow = la.Rows == 1 && la.Columns > 1;
        CalcReference? rref = rv.Reference;
        CellArea? area = rref is { AreaCount: 1 } && !rref.IsSpill ? rref.First : null;
        int retRows;
        int retCols;
        CalcArray? ra = null;
        if (area is { } ar)
        {
            retRows = ar.Rows;
            retCols = ar.Columns;
        }
        else
        {
            if (!LookupSupport.TryArray(ev, rv, out ra, out error))
            {
                return error;
            }

            retRows = ra.Rows;
            retCols = ra.Columns;
        }

        if (lookupIsRow ? retCols != v.Length : retRows != v.Length)
        {
            return Err(CalcError.Value);
        }

        int i = XSearch(v, args[0], mm, sm);
        if (i < 0)
        {
            if (args.Length > 3 && !args[3].IsMissing)
            {
                return args[3];
            }

            return Err(CalcError.NA);
        }

        if (area is { } a2)
        {
            CellArea slice = lookupIsRow
                ? a2 with { Column1 = a2.Column1 + i, Column2 = a2.Column1 + i }
                : a2 with { Row1 = a2.Row1 + i, Row2 = a2.Row1 + i };
            return CalcValue.FromReference(new CalcReference([slice]));
        }

        return Slice(ev, ra!, lookupIsRow, i);
    }

    private static CalcValue Slice(Evaluator ev, CalcArray a, bool column, int index)
    {
        int n = column ? a.Rows : a.Columns;
        if (n == 1)
        {
            return column ? a[0, index] : a[index, 0];
        }

        CalcValue[] cells = ev.NewCells(column ? n : 1, column ? 1 : n);
        ev.ChargeSteps(cells.Length);
        for (int k = 0; k < n; k++)
        {
            cells[k] = column ? a[k, index] : a[index, k];
        }

        return CalcValue.FromArray(new CalcArray(column ? n : 1, column ? 1 : n, cells));
    }

    // ---- LOOKUP, VLOOKUP, HLOOKUP -----------------------------------------------------------------------------------------

    // Microsoft's LOOKUP page: vector form searches lookup_vector (ascending) and answers from the same position of result_vector;
    // array form searches the first row of a wide array (or the first column otherwise) and answers from its last row (column).
    private static CalcValue Lookup(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[1], out CalcArray arr, out CalcValue error))
        {
            return error;
        }

        if (args.Length == 3)
        {
            if (!LookupSupport.TryArray(ev, args[2], out CalcArray res, out error))
            {
                return error;
            }

            if (!Vec.TryOf(arr, out Vec lv) || !Vec.TryOf(res, out Vec rv))
            {
                return Err(CalcError.NA);
            }

            int i = LookupSupport.Binary(lv, args[0], true, false);
            return i < 0 || i >= rv.Length ? Err(CalcError.NA) : rv[i];
        }

        bool wide = arr.Columns > arr.Rows;
        Vec search = wide ? new Vec(arr, true, 0) : new Vec(arr, false, 0);
        int k = LookupSupport.Binary(search, args[0], true, false);
        if (k < 0)
        {
            return Err(CalcError.NA);
        }

        return wide ? arr[arr.Rows - 1, k] : arr[k, arr.Columns - 1];
    }

    private static CalcValue TableLookup(Evaluator ev, ReadOnlySpan<CalcValue> args, bool horizontal)
    {
        if (!LookupSupport.TryArray(ev, args[1], out CalcArray table, out CalcValue error))
        {
            return error;
        }

        if (!LookupSupport.TryInt(args[2], out int index, out error))
        {
            return error;
        }

        bool approx = true;
        if (args.Length > 3)
        {
            // open: an omitted range_lookup between commas counts as FALSE.
            if (args[3].IsMissing)
            {
                approx = false;
            }
            else
            {
                CalcError e = CalcConvert.ToBoolean(args[3], out approx);
                if (e != CalcError.None)
                {
                    return Err(e);
                }
            }
        }

        int span = horizontal ? table.Rows : table.Columns;
        Vec search = horizontal ? new Vec(table, true, 0) : new Vec(table, false, 0);
        int i = approx ? LookupSupport.Binary(search, args[0], true, false) : LookupSupport.FindExact(search, args[0], true, false);
        if (i < 0)
        {
            return Err(CalcError.NA);
        }

        // Observed in Excel 16.0.20527: the lookup runs first, so a value that is not found is #N/A whatever the index is; the index is
        // checked only for a row that was found.
        if (index < 1)
        {
            return Err(CalcError.Value);
        }

        if (index > span)
        {
            return Err(CalcError.Ref);
        }

        return horizontal ? table[index - 1, i] : table[i, index - 1];
    }

    // ---- CHOOSECOLS, CHOOSEROWS -------------------------------------------------------------------------------------------

    private static CalcValue Choose(Evaluator ev, ReadOnlySpan<CalcValue> args, bool rows)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error))
        {
            return error;
        }

        int extent = rows ? a.Rows : a.Columns;
        var picks = new List<int>();
        for (int k = 1; k < args.Length; k++)
        {
            if (!LookupSupport.TryArray(ev, args[k], out CalcArray idx, out error))
            {
                return error;
            }

            for (int r = 0; r < idx.Rows; r++)
            {
                for (int c = 0; c < idx.Columns; c++)
                {
                    CalcValue x = idx[r, c];
                    if (x.IsError)
                    {
                        return x;
                    }

                    if (!LookupSupport.TryInt(x, out int n, out error))
                    {
                        return error;
                    }

                    // Negative numbers count from the end; zero and out-of-range numbers are #VALUE!.
                    if (n == 0 || Math.Abs(n) > extent)
                    {
                        return Err(CalcError.Value);
                    }

                    picks.Add(n > 0 ? n - 1 : extent + n);
                }
            }
        }

        int outRows = rows ? picks.Count : a.Rows;
        int outCols = rows ? a.Columns : picks.Count;
        CalcValue[] cells = ev.NewCells(outRows, outCols);
        ev.ChargeSteps(cells.Length);
        for (int r = 0; r < outRows; r++)
        {
            for (int c = 0; c < outCols; c++)
            {
                cells[(r * outCols) + c] = rows ? a[picks[r], c] : a[r, picks[c]];
            }
        }

        return CalcValue.FromArray(new CalcArray(outRows, outCols, cells));
    }

    // ---- OFFSET, INDIRECT -----------------------------------------------------------------------------------------------

    // Microsoft's OFFSET page: a reference moved by rows and columns and optionally resized; height and width must be positive
    // (a zero is #REF!); a result off the sheet is #REF!.
    private static CalcValue Offset(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        CalcValue src = args[0];
        if (src.IsError)
        {
            return src;
        }

        if (src.Reference is not { AreaCount: 1 } reference)
        {
            return Err(CalcError.Value);
        }

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].IsError)
            {
                return args[i];
            }
        }

        if (!LookupSupport.TryInt(args[1], out int dr, out CalcValue error) || !LookupSupport.TryInt(args[2], out int dc, out error))
        {
            return error;
        }

        CellArea area = reference.First;
        if (area.LastSheet is not null)
        {
            return Err(CalcError.Value);
        }

        int height = area.Rows;
        int width = area.Columns;
        if (args.Length > 3 && !args[3].IsMissing)
        {
            if (!LookupSupport.TryInt(args[3], out height, out error))
            {
                return error;
            }
        }

        if (args.Length > 4 && !args[4].IsMissing)
        {
            if (!LookupSupport.TryInt(args[4], out width, out error))
            {
                return error;
            }
        }

        if (height == 0 || width == 0)
        {
            return Err(CalcError.Ref);
        }

        // A negative height or width extends up or left from the moved anchor.
        long r1 = (long)area.Row1 + dr;
        long c1 = (long)area.Column1 + dc;
        long r2 = height > 0 ? r1 + height - 1 : r1;
        long c2 = width > 0 ? c1 + width - 1 : c1;
        if (height < 0)
        {
            r1 = r2 + height + 1;
        }

        if (width < 0)
        {
            c1 = c2 + width + 1;
        }

        if (r1 < 1 || c1 < 1 || r2 > CellArea.MaxRow || c2 > CellArea.MaxColumn)
        {
            return Err(CalcError.Ref);
        }

        return CalcValue.FromReference(new CalcReference([new CellArea(area.Sheet, null, (int)r1, (int)c1, (int)r2, (int)c2)]));
    }

    // Microsoft's INDIRECT page: text naming a cell, a range or a defined name, in A1 or (a1 = FALSE) R1C1 notation; anything else is #REF!.
    private static CalcValue Indirect(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        string text = CalcConvert.ToText(args[0]);
        bool a1 = true;
        if (args.Length > 1 && !args[1].IsMissing)
        {
            CalcError e = CalcConvert.ToBoolean(args[1], out a1);
            if (e != CalcError.None)
            {
                return Err(e);
            }
        }
        else if (args.Length > 1)
        {
            a1 = false;
        }

        CalcCell origin = ev.Origin;
        var options = new FormulaParserOptions
        {
            FileForm = false,
            Syntax = a1 ? FormulaSyntax.A1 : FormulaSyntax.R1C1,
            OriginRow = origin.Row,
            OriginColumn = origin.Column,
            Limits = ev.Limits,
        };
        if (text.Length == 0 || !FormulaParser.TryParse(text, out FormulaNode? node, out _, options) || node is null)
        {
            return Err(CalcError.Ref);
        }

        switch (node.Kind)
        {
            case NodeKind.Cell:
            case NodeKind.Area:
                CalcValue v = ev.Evaluate(node);
                return v.Kind == ValueKind.Reference || v.IsError ? v : Err(CalcError.Ref);
            case NodeKind.Name:
                var name = (NameNode)node;
                if (name.Prefix?.Workbook is not null)
                {
                    return Err(CalcError.Ref);
                }

                CalcValue named = ev.Resolver.ResolveName(name.Prefix?.Sheet, name.BareName, origin);
                return named.Kind == ValueKind.Reference ? named : Err(CalcError.Ref);
            case NodeKind.Structured:
                CalcValue st = ev.Evaluate(node);
                return st.Kind == ValueKind.Reference ? st : Err(CalcError.Ref);
            default:
                return Err(CalcError.Ref);
        }
    }

    // ---- ROW, COLUMN, AREAS, ADDRESS ----------------------------------------------------------------------------------------

    // Microsoft's ROW/COLUMN pages: of the cell holding the formula when there is no argument; otherwise the first row (column) of
    // the reference, and for a tall (wide) reference the array of all its row (column) numbers.
    private static CalcValue RowColumn(Evaluator ev, ReadOnlySpan<CalcValue> args, bool row)
    {
        if (args.Length == 0 || args[0].IsMissing)
        {
            return Num(row ? ev.Origin.Row : ev.Origin.Column);
        }

        CalcValue v = args[0];
        if (v.IsError)
        {
            return v;
        }

        if (v.Reference is not { } r)
        {
            return Err(CalcError.Value); // open: ROW of an array or constant.
        }

        CellArea a = r.IsSpill ? ResolveSpill(ev, r) : r.First;
        int first = row ? a.Row1 : a.Column1;
        int count = row ? a.Rows : a.Columns;
        if (count == 1)
        {
            return Num(first);
        }

        CalcValue[] cells = ev.NewCells(row ? count : 1, row ? 1 : count);
        ev.ChargeSteps(cells.Length);
        for (int i = 0; i < count; i++)
        {
            cells[i] = Num(first + i);
        }

        return CalcValue.FromArray(new CalcArray(row ? count : 1, row ? 1 : count, cells));
    }

    private static CellArea ResolveSpill(Evaluator ev, CalcReference r)
    {
        CellArea a = r.First;
        return ev.Resolver.TryGetSpill(a.Sheet ?? ev.Origin.Sheet, a.Row1, a.Column1, out CellArea sp) ? sp : a;
    }

    private static CalcValue Areas(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        _ = ev;
        if (args[0].IsError)
        {
            return args[0];
        }

        return args[0].Reference is { } r ? Num(r.AreaCount) : Err(CalcError.Value);
    }

    // Microsoft's ADDRESS page: row_num, column_num, abs_num 1-4 (default 1), a1 (default TRUE), sheet_text.
    private static CalcValue Address(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        _ = ev;
        if (!LookupSupport.TryInt(args[0], out int row, out CalcValue error) || !LookupSupport.TryInt(args[1], out int col, out error))
        {
            return error;
        }

        int abs = 1;
        if (args.Length > 2 && !args[2].IsMissing && !LookupSupport.TryInt(args[2], out abs, out error))
        {
            return error;
        }

        bool a1 = true;
        if (args.Length > 3 && !args[3].IsMissing)
        {
            CalcError e = CalcConvert.ToBoolean(args[3], out a1);
            if (e != CalcError.None)
            {
                return Err(e);
            }
        }
        else if (args.Length > 3)
        {
            a1 = false;
        }

        if (row < 1 || row > CellArea.MaxRow || col < 1 || col > CellArea.MaxColumn || abs is < 1 or > 4)
        {
            return Err(CalcError.Value);
        }

        bool absRow = abs is 1 or 2;
        bool absCol = abs is 1 or 3;
        string cell = a1
            ? (absCol ? "$" : string.Empty) + RefSyntax.ColumnName(col) + (absRow ? "$" : string.Empty) + row.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "R" + (absRow ? row.ToString(System.Globalization.CultureInfo.InvariantCulture) : "[" + row.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]") +
              "C" + (absCol ? col.ToString(System.Globalization.CultureInfo.InvariantCulture) : "[" + col.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");
        if (args.Length > 4 && !args[4].IsMissing)
        {
            string sheet = CalcConvert.ToText(args[4]);
            if (sheet.Length > 0)
            {
                bool quote = SheetPrefix.NeedsQuotes(sheet);
                return CalcValue.FromText((quote ? "'" + sheet.Replace("'", "''", StringComparison.Ordinal) + "'" : sheet) + "!" + cell);
            }
        }

        return CalcValue.FromText(cell);
    }

    // ---- TRANSPOSE, HYPERLINK, TRIMRANGE ------------------------------------------------------------------------------------

    private static CalcValue Transpose(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error))
        {
            return error;
        }

        if (a.Rows == 1 && a.Columns == 1)
        {
            return a[0, 0];
        }

        CalcValue[] cells = ev.NewCells(a.Columns, a.Rows);
        ev.ChargeSteps(cells.Length);
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                cells[(c * a.Rows) + r] = a[r, c];
            }
        }

        return CalcValue.FromArray(new CalcArray(a.Columns, a.Rows, cells));
    }

    // Microsoft's HYPERLINK page: the cell shows friendly_name, or the link when there is none. Opening the link is the host's job.
    private static CalcValue Hyperlink(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        _ = ev;
        if (args.Length > 1 && !args[1].IsMissing)
        {
            return args[1];
        }

        return CalcValue.FromText(CalcConvert.ToText(args[0]));
    }

    // Microsoft's TRIMRANGE page: removes empty rows and columns from the edges of a range. trim_rows / trim_cols: 0 none,
    // 1 leading, 2 trailing, 3 both (default).
    private static CalcValue TrimRange(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        CalcValue src = args[0];
        if (src.IsError)
        {
            return src;
        }

        int rowMode = 3;
        int colMode = 3;
        if (args.Length > 1 && !args[1].IsMissing && !LookupSupport.TryInt(args[1], out rowMode, out CalcValue error))
        {
            return error;
        }

        if (args.Length > 2 && !args[2].IsMissing && !LookupSupport.TryInt(args[2], out colMode, out error))
        {
            return error;
        }

        if (rowMode is < 0 or > 3 || colMode is < 0 or > 3)
        {
            return Err(CalcError.Value);
        }

        CalcReference? reference = src.Reference;
        CalcArray data;
        CellArea area = default;
        if (reference is not null)
        {
            if (reference.AreaCount != 1)
            {
                return Err(CalcError.Value);
            }

            area = reference.First;
            if (!ev.TryReadAreas(reference, out CalcArray[] blocks, out CalcError re))
            {
                return Err(re);
            }

            data = blocks[0];
        }
        else if (!LookupSupport.TryArray(ev, src, out data, out error))
        {
            return error;
        }

        int top = 0;
        int bottom = data.Rows - 1;
        int left = 0;
        int right = data.Columns - 1;
        if ((rowMode & 1) != 0)
        {
            while (top <= bottom && RowEmpty(data, top, left, right))
            {
                top++;
            }
        }

        if ((rowMode & 2) != 0)
        {
            while (bottom >= top && RowEmpty(data, bottom, left, right))
            {
                bottom--;
            }
        }

        if ((colMode & 1) != 0)
        {
            while (left <= right && ColumnEmpty(data, left, top, bottom))
            {
                left++;
            }
        }

        if ((colMode & 2) != 0)
        {
            while (right >= left && ColumnEmpty(data, right, top, bottom))
            {
                right--;
            }
        }

        if (top > bottom || left > right)
        {
            return Err(CalcError.Ref); // open: a range with nothing in it.
        }

        if (reference is not null)
        {
            return CalcValue.FromReference(new CalcReference([new CellArea(area.Sheet, area.LastSheet, area.Row1 + top, area.Column1 + left, area.Row1 + bottom, area.Column1 + right)]));
        }

        int rows = bottom - top + 1;
        int cols = right - left + 1;
        CalcValue[] cells = ev.NewCells(rows, cols);
        ev.ChargeSteps(cells.Length);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                cells[(r * cols) + c] = data[top + r, left + c];
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    private static bool RowEmpty(CalcArray a, int row, int c1, int c2)
    {
        for (int c = c1; c <= c2; c++)
        {
            if (!a[row, c].IsBlank)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ColumnEmpty(CalcArray a, int col, int r1, int r2)
    {
        for (int r = r1; r <= r2; r++)
        {
            if (!a[r, col].IsBlank)
            {
                return false;
            }
        }

        return true;
    }
}
