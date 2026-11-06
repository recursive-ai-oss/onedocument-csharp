using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <summary>
    /// Gets what a conversion on open could not carry over, or null when the workbook was opened from a native OOXML package.
    /// An Excel 2003 XML file opens as a workbook by conversion; this lists every feature it dropped or flattened.
    /// </summary>
    public LossReport? ImportLoss { get; internal set; }
}
