using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Encryption;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Vba;

/// <summary>
/// Takes the VBA project out of a file and writes the rest as a new file in the matching macro-free format. The project is data to
/// delete: it is never opened for execution, and nothing else in the file is changed.
/// </summary>
/// <remarks>
/// <para>
/// Macro-enabled OOXML (DOCM, DOTM, XLSM, XLTM, PPTM, POTM, PPSM) loses its project part, its signature parts and the form-data part, the
/// relationships to them and their content types, and the main part becomes the macro-free type (so DOCM becomes DOCX). Add-ins (XLAM,
/// PPAM) and binary workbooks (XLSB) have no macro-free type: they keep their format without a project. Every other part is
/// copied as stored.
/// </para>
/// <para>
/// DOC loses its <c>Macros</c> storage. XLS loses its <c>_VBA_PROJECT_CUR</c> storage and the records that announce a project, with the
/// stream positions that follow them corrected. PPT has its project storage emptied and the macro flag cleared. A signature belongs to
/// the project it signs, so removal takes it too and says so in the report.
/// </para>
/// <para>Encrypted files are decrypted with the context's password and written without encryption; the report says so.</para>
/// </remarks>
public static class VbaRemoval
{
    /// <summary>Removes the VBA project from a file and writes the result to another path.</summary>
    /// <param name="source">The input file.</param>
    /// <param name="destination">The output file; replaced atomically. It may not be the input.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the removal; no output is left.</param>
    /// <returns>The report: what was removed, the output format, and what was left.</returns>
    /// <exception cref="ArgumentException">The destination is the input.</exception>
    /// <exception cref="OfficeFormatException">The input is damaged beyond repair.</exception>
    /// <exception cref="OfficeUnsupportedException">The input is a format that holds no VBA project, or an encrypted legacy file.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="IOException">A file cannot be read or written.</exception>
    public static VbaRemovalReport Remove(string source, string destination, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.Ordinal))
        {
            throw new ArgumentException("The output must be a new file, not the input.", nameof(destination));
        }

        VbaRemovalReport? report = null;
        using (IByteSource input = ByteSource.FromFile(source))
        {
            AtomicFile.Write(destination, stream => report = Remove(input, stream, context, cancellationToken));
        }

        return report!;
    }

    /// <summary>Removes the VBA project from a stream and writes the result to another.</summary>
    /// <param name="source">The input; seekable.</param>
    /// <param name="destination">Where the output goes; it is left open.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>The report.</returns>
    /// <exception cref="OfficeFormatException">The input is damaged beyond repair.</exception>
    /// <exception cref="OfficeUnsupportedException">The input holds no VBA at all by its format, or is an encrypted legacy file.</exception>
    public static VbaRemovalReport Remove(Stream source, Stream destination, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using IByteSource input = ByteSource.FromStream(source, leaveOpen: true);
        return Remove(input, destination, context, cancellationToken);
    }

    /// <summary>Removes the VBA project from a byte source and writes the result to a stream.</summary>
    /// <param name="source">The input; it must support synchronous reads. It is not disposed.</param>
    /// <param name="destination">Where the output goes; it is left open.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>The report.</returns>
    /// <exception cref="OfficeFormatException">The input is damaged beyond repair.</exception>
    /// <exception cref="OfficeUnsupportedException">The input holds no VBA at all by its format, or is an encrypted legacy file.</exception>
    public static VbaRemovalReport Remove(IByteSource source, Stream destination, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        context ??= new OpenContext();
        cancellationToken.ThrowIfCancellationRequested();
        OfficeFormat format = FormatSniffer.Detect(source, context.Limits);
        if (format == OfficeFormat.EncryptedPackage)
        {
            byte[] plain = PackageEncryption.DecryptCompoundFile(source, context);
            using IByteSource inner = ByteSource.FromMemory(plain);
            VbaRemovalReport inside = Remove(inner, destination, context, cancellationToken);
            inside.Note("vba.output-not-encrypted", "The input was encrypted; the output is written without encryption.", DefectLocation.None);
            return inside;
        }

        if (VbaProject.IsOpcFormat(format))
        {
            return OoxmlVba.RemoveFromFile(source, format, destination, context, cancellationToken);
        }

        switch (format)
        {
            case OfficeFormat.Doc:
                return LegacyVba.RemoveFromDoc(source, destination, context, cancellationToken);
            case OfficeFormat.Xls:
                return LegacyVba.RemoveFromXls(source, destination, context, cancellationToken);
            case OfficeFormat.XlsLegacy:
                return LegacyVba.Copy(source, format, destination, cancellationToken); // BIFF2-5 streams hold no VBA storage
            case OfficeFormat.Unknown:
                break;
            default:
                if (FormatRegistry.Default.TryGetVbaRemover(format, out VbaRemoveHandler? handler))
                {
                    return handler(source, destination, context, cancellationToken);
                }

                break;
        }

        throw new FormatNotAvailableException(format, "remove a VBA project from");
    }

    /// <summary>
    /// Removes the VBA project, its signature parts and the form-data part from an OOXML package in place, with the relationships to them.
    /// The main part's content type is left as it is: saving the package in the macro-free format retypes it.
    /// </summary>
    /// <param name="package">The package; it is marked modified when anything was removed.</param>
    /// <param name="format">The package's format, for the report.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <returns>The report; <see cref="VbaRemovalReport.OutputFormat"/> is the macro-free counterpart of <paramref name="format"/>.</returns>
    public static VbaRemovalReport RemoveFromPackage(IOpcPackage package, OfficeFormat format, OpenContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        var report = new VbaRemovalReport(format) { OutputFormat = OoxmlVba.MacroFreeFormat(format) };
        OoxmlVba.RemoveParts(package, report, context ?? new OpenContext());
        return report;
    }
}
