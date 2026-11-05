using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

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

    /// <summary>
    /// Narrows a whole-row or whole-column area to the part that can hold data, so <c>SUM(A:A)</c> does not read a million empty cells. The evaluator
    /// calls it only for an area that spans a whole sheet axis, and the returned area keeps the original's first row and column.
    /// </summary>
    /// <param name="area">The area, with its sheet filled in.</param>
    /// <returns>The area to read: the same one (the default) or a smaller one with the same top-left corner.</returns>
    CellArea ClipArea(in CellArea area) => area;

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

    /// <summary>Answers <c>WEBSERVICE(url)</c>. The evaluator itself never makes a request; a resolver that wants to supports it here.</summary>
    /// <param name="url">The address, 1 to 2048 characters.</param>
    /// <returns>The response text, or <c>#VALUE!</c> (the default, Excel's answer when the service cannot be reached).</returns>
    CalcValue ResolveWebService(string url) => CalcValue.FromError(CalcError.Value);

    /// <summary>Answers <c>FILTERXML(xml, xpath)</c>. The default denies it, like <see cref="ResolveWebService"/>.</summary>
    /// <param name="xml">The XML text.</param>
    /// <param name="xpath">The XPath expression.</param>
    /// <returns>A value or an array of them, or <c>#VALUE!</c> (the default).</returns>
    CalcValue ResolveFilterXml(string xml, string xpath) => CalcValue.FromError(CalcError.Value);

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

    /// <summary>The clock behind <c>TODAY</c> and <c>NOW</c>, so a workbook or a test decides what time it is.</summary>
    /// <returns>Null (the default) to use <see cref="EvaluatorOptions.Clock"/>, and the system clock when that is null too.</returns>
    TimeProvider? Clock => null;

    /// <summary>Resolves a dotted member path of the template extension: <c>item.price</c>.</summary>
    /// <param name="segments">The path, root first.</param>
    /// <returns>A scalar, or an array when the path lifts over a collection; <c>#NAME?</c> by default.</returns>
    CalcValue ResolveMember(IReadOnlyList<string> segments) => CalcValue.FromError(CalcError.Name);

    /// <summary>Tells whether a cell holds a formula (<c>ISFORMULA</c>).</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <returns>False by default.</returns>
    bool IsFormula(string? sheet, int row, int column) => false;

    /// <summary>Gives the formula text of a cell (<c>FORMULATEXT</c>), with the leading <c>=</c>.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <returns>The text, or null (the default) when the cell holds no formula; the function then yields <c>#N/A</c>.</returns>
    string? GetFormulaText(string? sheet, int row, int column) => null;

    /// <summary>Tells how a row is hidden, for <c>SUBTOTAL</c> and <c>AGGREGATE</c>.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="row">1-based row.</param>
    /// <returns><see cref="RowVisibility.Visible"/> by default.</returns>
    RowVisibility GetRowVisibility(string? sheet, int row) => RowVisibility.Visible;

    /// <summary>Tells whether a cell's formula is itself a <c>SUBTOTAL</c> or <c>AGGREGATE</c> call, which both functions skip inside ranges.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <returns>False by default.</returns>
    bool IsSubtotalCell(string? sheet, int row, int column) => false;

    /// <summary>Describes a cell for <c>CELL</c>.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <returns>General format, no prefix, locked, width 8.43 by default.</returns>
    CalcCellInfo GetCellInfo(string? sheet, int row, int column) => CalcCellInfo.Default;

    /// <summary>Gives the position of a sheet in the workbook (<c>SHEET</c>).</summary>
    /// <param name="sheet">The sheet name; null for the formula's own sheet.</param>
    /// <returns>1-based index, or 0 (the default) when unknown; the function then yields <c>#N/A</c>.</returns>
    int GetSheetIndex(string? sheet) => 0;

    /// <summary>The number of sheets in the workbook (<c>SHEETS</c> with no argument).</summary>
    /// <returns>1 by default.</returns>
    int GetSheetCount() => 1;

    /// <summary>Gives the text <c>CELL("filename")</c> returns: <c>directory\[book]sheet</c>.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <returns>Empty (the default) for a workbook that has not been saved.</returns>
    string GetFileName(string? sheet) => string.Empty;

    /// <summary>Resolves <c>GETPIVOTDATA</c> against the pivot table that holds a cell: the value the table shows for a data field and field/item pairs.</summary>
    /// <param name="dataField">The data field's caption or its source field's name.</param>
    /// <param name="sheet">The sheet of the cell that identifies the pivot table.</param>
    /// <param name="row">1-based row of that cell.</param>
    /// <param name="column">1-based column of that cell.</param>
    /// <param name="items">The field name and item value pairs, in the order written.</param>
    /// <returns>The value the table shows, or <c>#REF!</c> (the default) when there is no such pivot table or the combination is not shown.</returns>
    CalcValue GetPivotData(string dataField, string? sheet, int row, int column, IReadOnlyList<KeyValuePair<string, CalcValue>> items) => CalcValue.FromError(CalcError.Ref);

    /// <summary>Gives the text <c>INFO</c> returns for a type such as <c>"osversion"</c>.</summary>
    /// <param name="type">The lower-case type name.</param>
    /// <returns>The text or number, or null (the default) for the built-in answer.</returns>
    CalcValue? GetInfo(string type) => null;
}

/// <summary>How a row is hidden.</summary>
public enum RowVisibility : byte
{
    /// <summary>Shown.</summary>
    Visible = 0,

    /// <summary>Hidden by the user (hide row, grouping).</summary>
    Hidden = 1,

    /// <summary>Hidden by a filter.</summary>
    Filtered = 2,
}

/// <summary>Facts about a cell that <c>CELL</c> reports.</summary>
/// <param name="NumberFormat">The number format code.</param>
/// <param name="Prefix">The label prefix character: <c>'</c> left, <c>"</c> right, <c>^</c> centre, <c>\</c> fill, or a NUL character for none.</param>
/// <param name="Locked">True when the cell is locked.</param>
/// <param name="ColumnWidth">The column width in characters.</param>
/// <param name="Color">True when the format shows negatives in colour.</param>
/// <param name="Parentheses">True when the format puts positives or all numbers in parentheses.</param>
public readonly record struct CalcCellInfo(string NumberFormat, char Prefix, bool Locked, double ColumnWidth, bool Color, bool Parentheses)
{
    /// <summary>General format, no prefix, locked, width 8.43.</summary>
    public static CalcCellInfo Default => new("General", '\0', true, 8.43, false, false);
}
