using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Encryption;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Vba;

public sealed partial class VbaProject
{
    /// <summary>Finds and reads the VBA project of a file of any format that can hold one. The format is found from the content, never the extension.</summary>
    /// <param name="path">The file: an OOXML package of any kind (including XLSB and Flat OPC), a DOC, XLS or PPT file, an encrypted file (opened with the context's password), or a bare <c>vbaProject.bin</c>.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The project, or null when the file holds none. Damage inside the project is reported to the context as <c>vba.*</c> defects and reading continues.</returns>
    /// <exception cref="OfficeFormatException">The file is damaged beyond repair.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="OfficeEncryptedException">The file is encrypted and the password is missing or wrong.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static VbaProject? Open(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using IByteSource source = ByteSource.FromFile(path);
        return Open(source, context, cancellationToken);
    }

    /// <summary>Finds and reads the VBA project of a seekable stream.</summary>
    /// <param name="stream">The stream; left open.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The project, or null when the content holds none.</returns>
    /// <exception cref="OfficeFormatException">The content is damaged beyond repair.</exception>
    public static VbaProject? Open(Stream stream, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using IByteSource source = ByteSource.FromStream(stream, leaveOpen: true);
        return Open(source, context, cancellationToken);
    }

    /// <summary>Finds and reads the VBA project of a byte source.</summary>
    /// <param name="source">The bytes; synchronous reads are required. The caller disposes it.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The project, or null when the content holds none.</returns>
    /// <exception cref="OfficeFormatException">The content is damaged beyond repair.</exception>
    public static VbaProject? Open(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        cancellationToken.ThrowIfCancellationRequested();
        OfficeFormat format = FormatSniffer.Detect(source, context.Limits);
        if (format == OfficeFormat.EncryptedPackage)
        {
            using IByteSource plain = ByteSource.FromMemory(PackageEncryption.DecryptCompoundFile(source, context));
            return Open(plain, context, cancellationToken);
        }

        if (IsOpcFormat(format))
        {
            using OpcPackage package = OpcPackage.Open(source, context);
            return OoxmlVba.Read(package, context);
        }

        switch (format)
        {
            case OfficeFormat.Doc:
                return ReadStorage(source, "/Macros", context);
            case OfficeFormat.Xls:
                return ReadStorage(source, "/_VBA_PROJECT_CUR", context);
            case OfficeFormat.Unknown:
                return ReadBareProject(source, context);
            case OfficeFormat.Ppt:
                // The project of a PowerPoint file lives in records only the presentations package reads; without it the answer would be a wrong "none".
                return FormatRegistry.Default.TryGetVbaReader(format, out VbaReadHandler? handler)
                    ? handler(source, context, cancellationToken)
                    : throw new FormatNotAvailableException(format, "read a VBA project from");
            default:
                return null;
        }
    }

    /// <summary>Reads the project of an OOXML package already open (the project part is the one with the VBA project content type).</summary>
    /// <param name="package">The package.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <returns>The project, or null when the package holds none.</returns>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static VbaProject? FromPackage(IOpcPackage package, OpenContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        return OoxmlVba.Read(package, context ?? new OpenContext());
    }

    /// <summary>Reads a project from the bytes of a <c>vbaProject.bin</c> (a compound file with a <c>VBA</c> storage at its root).</summary>
    /// <param name="compoundFile">The bytes.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="name">The name to report as <see cref="Container"/>.</param>
    /// <returns>The project.</returns>
    /// <exception cref="OfficeFormatException">The bytes are not a compound file.</exception>
    public static VbaProject FromProjectStorage(ReadOnlyMemory<byte> compoundFile, OpenContext? context = null, string name = "vbaProject.bin")
    {
        context ??= new OpenContext();
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(compoundFile), context);
        return Parse(cfb, string.Empty, name, context);
    }

    /// <summary>Reads a project stored as a storage inside a compound file already open (the project root is the storage <paramref name="root"/>).</summary>
    /// <param name="compoundFile">The compound file.</param>
    /// <param name="root">The storage's path, for example <c>/Macros</c>; <c>""</c> when the file itself is the project root.</param>
    /// <param name="context">Limits and defect sink.</param>
    /// <returns>The project.</returns>
    public static VbaProject FromStorage(CfbPackage compoundFile, string root, OpenContext context)
    {
        ArgumentNullException.ThrowIfNull(compoundFile);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(context);
        return Parse(compoundFile, root, root.Length == 0 ? "vbaProject.bin" : root, context);
    }

    private static VbaProject? ReadStorage(IByteSource source, string root, OpenContext context)
    {
        using CfbPackage cfb = CfbPackage.Open(source, context);
        string prefix = root + "/";
        foreach (CfbStreamPart p in cfb.Streams)
        {
            if (p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Parse(cfb, root, root, context);
            }
        }

        return null;
    }

    private static VbaProject? ReadBareProject(IByteSource source, OpenContext context)
    {
        if (!CfbPackage.TryOpen(source, out CfbPackage? cfb, context) || cfb is null)
        {
            return null;
        }

        using (cfb)
        {
            return cfb.FindPart("/VBA/dir") is null ? null : Parse(cfb, string.Empty, "vbaProject.bin", context);
        }
    }

    internal static bool IsOpcFormat(OfficeFormat f) =>
        f is >= OfficeFormat.Docx and <= OfficeFormat.FlatOpcDocument
            or >= OfficeFormat.Xlsx and <= OfficeFormat.Xlsb
            or >= OfficeFormat.Pptx and <= OfficeFormat.FlatOpcPresentation;
}
