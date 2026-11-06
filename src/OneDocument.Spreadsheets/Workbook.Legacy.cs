using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <summary>
    /// Gets what was dropped when this workbook was converted from a legacy binary workbook (BIFF2 to BIFF8) or an Excel 2003 XML file: charts, macros, pivot
    /// tables, rich-text runs and the like, one entry per kind with a count (see <c>docs/xls.md</c> and <c>docs/xml-2003.md</c>). Null for a workbook that was opened from an OOXML file or created in memory.
    /// </summary>
    public LossReport? ImportLoss { get; internal set; }

    /// <summary>
    /// Gets what the last save as a legacy binary workbook (<see cref="OfficeFormat.Xls"/>) could not carry: charts, pictures, tables, formulas BIFF8
    /// cannot express, cells beyond 65,536 rows or 256 columns, and the like, one entry per kind with a count. Null until such a save has run.
    /// </summary>
    public LossReport? ExportLoss { get; private set; }

    // The writer behind SaveAs(.., OfficeFormat.Xls): one BIFF8 Workbook stream in a compound file.
    internal void WriteXls(Stream destination, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        CommitChanges();
        var loss = new LossReport();
        byte[] stream = Biff.XlsWriter.Write(this, loss, cancellationToken);
        using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Create(Limits);
        // The class of an Excel 97-2003 workbook (Excel.Sheet.8).
        cfb.RootClassId = new Guid("00020820-0000-0000-C000-000000000046");
        cfb.AddPart("/Workbook", string.Empty, stream);
        cfb.WriteTo(destination, cancellationToken);
        ExportLoss = loss;
    }
}
