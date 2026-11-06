using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <summary>
    /// Gets what was dropped when this workbook was converted from a legacy binary workbook (BIFF2 to BIFF8): charts, macros, pivot
    /// tables, rich-text runs and the like, one entry per kind with a count. Null for a workbook that was opened from an OOXML file or created in memory.
    /// </summary>
    public LossReport? ImportLoss { get; internal set; }
}
