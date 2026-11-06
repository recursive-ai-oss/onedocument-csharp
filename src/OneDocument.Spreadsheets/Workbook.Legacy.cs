using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <summary>
    /// Gets what was dropped when this workbook was converted from a legacy binary workbook (BIFF2 to BIFF8) or an Excel 2003 XML file: charts, macros, pivot
    /// tables, rich-text runs and the like, one entry per kind with a count (see <c>docs/xls.md</c> and <c>docs/xml-2003.md</c>). Null for a workbook that was opened from an OOXML file or created in memory.
    /// </summary>
    public LossReport? ImportLoss { get; internal set; }
}
