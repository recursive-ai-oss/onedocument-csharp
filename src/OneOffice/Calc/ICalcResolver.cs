using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>The cell a formula is evaluated for: its sheet (null when there is no workbook, as in templates) and its 1-based position.</summary>
/// <param name="Sheet">The sheet name, or null.</param>
/// <param name="Row">1-based row.</param>
/// <param name="Column">1-based column.</param>
public readonly record struct CalcCell(string? Sheet, int Row, int Column);

/// <summary>
/// What the evaluator needs from the world around a formula: cell values, names, tables, external workbooks, spilled ranges and members of
/// template data. Only <see cref="GetCell"/> is required; everything else has a default that says "not available". Templates implement it
/// over data with no grid; a workbook implements it over its cell store. The evaluator never reaches the network or the file system.
/// </summary>
public interface ICalcResolver
{
    /// <summary>Reads one cell.</summary>
    /// <param name="sheet">The sheet name (never null when the formula's <see cref="CalcCell.Sheet"/> is set; may be null without a workbook).</param>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <returns>The cell's value: a number, text, boolean, error or <see cref="CalcValue.Blank"/>. Never an array or reference.</returns>
    CalcValue GetCell(string? sheet, int row, int column);

    /// <summary>Reads a block of cells. The default calls <see cref="GetCell"/> per cell; a resolver with a faster path overrides it.</summary>
    /// <param name="area">The block, with its sheet filled in. A 3-D area is never passed (see <see cref="TryExpandSheets"/>).</param>
    /// <returns>The values, row-major.</returns>
    CalcArray GetRange(in CellArea area)
    {
        long count = (long)area.Rows * area.Columns;
        if (count > int.MaxValue / 2)
        {
            throw new OfficeLimitException(nameof(OfficeLimits.MaxAllocationBytes), int.MaxValue, count);
        }

        var cells = new CalcValue[(int)count];
        int i = 0;
        for (int r = area.Row1; r <= area.Row2; r++)
        {
            for (int c = area.Column1; c <= area.Column2; c++)
            {
                cells[i++] = GetCell(area.Sheet, r, c);
            }
        }

        return new CalcArray(area.Rows, area.Columns, cells);
    }

    /// <summary>Lists the sheets of a 3-D reference, in workbook order.</summary>
    /// <param name="first">First sheet.</param>
    /// <param name="last">Last sheet.</param>
    /// <param name="sheets">The sheet names from first to last inclusive.</param>
    /// <returns>False (the default) when the sheets are not known; the 3-D reference then reads as <c>#REF!</c>.</returns>
    bool TryExpandSheets(string first, string last, out IReadOnlyList<string> sheets)
    {
        sheets = [];
        return false;
    }

    /// <summary>Resolves a defined name.</summary>
    /// <param name="scopeSheet">The sheet qualifier written in the formula, or null for a workbook-level name or the formula's own sheet's name.</param>
    /// <param name="name">The name as written (without the <c>_xlpm.</c> prefix).</param>
    /// <param name="origin">The cell being evaluated.</param>
    /// <returns>A value, a reference, a lambda, or <c>#NAME?</c> (the default).</returns>
    CalcValue ResolveName(string? scopeSheet, string name, in CalcCell origin) => CalcValue.FromError(CalcError.Name);

    /// <summary>Resolves a table reference such as <c>Table1[[#This Row],[Col]]</c>.</summary>
    /// <param name="node">The parsed reference.</param>
    /// <param name="origin">The cell being evaluated (for <c>#This Row</c>).</param>
    /// <returns>Usually a <see cref="CalcReference"/>; <c>#REF!</c> by default.</returns>
    CalcValue ResolveStructured(StructuredReferenceNode node, in CalcCell origin) => CalcValue.FromError(CalcError.Ref);

    /// <summary>Reads a reference into another workbook (<c>[1]Sheet1!A1</c>). Resolution is opt-in: the default denies it.</summary>
    /// <param name="prefix">The workbook and sheet part.</param>
    /// <param name="node">The reference node (a cell, an area or a name).</param>
    /// <returns>The cached value, an array of them, or <c>#REF!</c> (the default).</returns>
    CalcValue ResolveExternal(SheetPrefix prefix, FormulaNode node) => CalcValue.FromError(CalcError.Ref);

    /// <summary>Finds the extent of the dynamic-array result anchored at a cell (<c>A1#</c>).</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="row">The anchor row.</param>
    /// <param name="column">The anchor column.</param>
    /// <param name="area">The spilled block, anchor included.</param>
    /// <returns>False (the default) when the cell holds no spilling formula; the evaluator then yields <c>#REF!</c>.</returns>
    bool TryGetSpill(string? sheet, int row, int column, out CellArea area)
    {
        area = default;
        return false;
    }

    /// <summary>Resolves a dotted member path of the template extension: <c>item.price</c>.</summary>
    /// <param name="segments">The path, root first.</param>
    /// <returns>A scalar, or an array when the path lifts over a collection; <c>#NAME?</c> by default.</returns>
    CalcValue ResolveMember(IReadOnlyList<string> segments) => CalcValue.FromError(CalcError.Name);
}
