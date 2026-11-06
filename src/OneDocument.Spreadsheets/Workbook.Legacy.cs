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

    /// <summary>
    /// Saves a copy as a password-protected legacy binary workbook (<see cref="OfficeFormat.Xls"/>), atomically. The workbook stream is
    /// encrypted with RC4 CryptoAPI and a 128-bit key, as Excel does, and the document properties stay readable (Excel's "properties not
    /// encrypted" setting). The bytes are the same for the same workbook and password: the salt comes from the content, never from a clock or a
    /// random source. RC4 is weak (see <c>docs/encryption.md</c>); use it only where a legacy reader needs it.
    /// </summary>
    /// <param name="path">The target path.</param>
    /// <param name="format">The format; only <see cref="OfficeFormat.Xls"/> can be written with a password here.</param>
    /// <param name="password">The password; at most 255 UTF-16 code units are used. It is not stored anywhere in the workbook.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <exception cref="OfficeUnsupportedException"><paramref name="format"/> is not <see cref="OfficeFormat.Xls"/>.</exception>
    public void SaveAs(string path, OfficeFormat format, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(password);
        ThrowIfDisposed();
        RequireXls(format);
        RecursiveAI.OneDocument.IO.AtomicFile.Write(path, stream => WriteXls(stream, password, cancellationToken));
    }

    /// <summary>Writes a copy to a stream as a password-protected legacy binary workbook; see <see cref="SaveAs(string, OfficeFormat, string, CancellationToken)"/>.</summary>
    /// <param name="destination">The stream; it is left open.</param>
    /// <param name="format">The format; only <see cref="OfficeFormat.Xls"/>.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="OfficeUnsupportedException"><paramref name="format"/> is not <see cref="OfficeFormat.Xls"/>.</exception>
    public void SaveAs(Stream destination, OfficeFormat format, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(password);
        ThrowIfDisposed();
        RequireXls(format);
        WriteXls(destination, password, cancellationToken);
    }

    private static void RequireXls(OfficeFormat format)
    {
        if (format != OfficeFormat.Xls)
        {
            // Agile encryption of OOXML and ODF waits for the cryptography package; nothing weaker is written in its place.
            throw new OfficeUnsupportedException("writing a password-protected " + format + " file");
        }
    }

    // The writer behind SaveAs(.., OfficeFormat.Xls): one BIFF8 Workbook stream in a compound file, encrypted when a password is given.
    internal void WriteXls(Stream destination, CancellationToken cancellationToken, string? password = null) => WriteXls(destination, password, cancellationToken);

    private void WriteXls(Stream destination, string? password, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        CommitChanges();
        var loss = new LossReport();
        byte[] stream = Biff.XlsWriter.Write(this, loss, cancellationToken);
        if (password is not null)
        {
            // The seed for the salt and the verifier is a digest of the content, so one workbook and password always give the same file.
            byte[] digest = System.Security.Cryptography.SHA256.HashData(stream);
            stream = Biff.XlsEncryption.Encrypt(stream, password, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(digest));
        }

        using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Create(Limits);
        // The class of an Excel 97-2003 workbook (Excel.Sheet.8).
        cfb.RootClassId = new Guid("00020820-0000-0000-C000-000000000046");
        cfb.AddPart("/Workbook", string.Empty, stream);
        cfb.WriteTo(destination, cancellationToken);
        ExportLoss = loss;
    }
}
