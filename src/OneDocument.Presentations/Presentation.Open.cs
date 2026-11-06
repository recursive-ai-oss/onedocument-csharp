using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Ppt.Binary;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Presentation
{
    // ECMA-376 Part 1 §13.3.6 and [MS-PPTX]: the presentation part's content type tells the presentation formats apart.
    private const string Main = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
    private const string MainPptm = "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml";
    private const string MainPotx = "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml";
    private const string MainPotm = "application/vnd.ms-powerpoint.template.macroEnabled.main+xml";
    private const string MainPpsx = "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml";
    private const string MainPpsm = "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml";
    private const string MainPpam = "application/vnd.ms-powerpoint.addin.macroEnabled.main+xml";

    /// <inheritdoc />
    public override OfficeKind Kind => OfficeKind.Presentation;

    /// <summary>
    /// Opens a presentation from a file. The format is found from the content, never the extension: PPTX, PPTM, POTX, POTM, PPSX, PPSM,
    /// PPAM and Flat OPC are accepted.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The presentation, which the caller disposes. Only the package structure is read; every part is read when first used, so a damaged presentation part raises at that first use, not here. Opening changes nothing.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format (a document, a legacy binary file…), named in the exception.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static Presentation Open(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenSource(ByteSource.FromFile(path), context, Path.GetFullPath(path), cancellationToken);
    }

    /// <summary>Opens a presentation from a seekable stream.</summary>
    /// <param name="stream">The stream; parts are read from it lazily, so it must stay readable until the presentation is disposed.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the presentation is disposed.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The presentation, which the caller disposes. It has no <see cref="OfficeFile.FilePath"/>, so use <c>SaveAs</c>.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format, named in the exception.</exception>
    public static Presentation Open(Stream stream, OpenContext? context = null, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenSource(ByteSource.FromStream(stream, leaveOpen), context, null, cancellationToken);
    }

    /// <summary>Opens a presentation from a byte source, which the presentation then owns and disposes.</summary>
    /// <param name="source">The bytes; it must support synchronous reads (<see cref="IByteSource.CanReadSync"/>).</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The presentation, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format, named in the exception.</exception>
    public static Presentation Open(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default) =>
        OpenSource(source, context, null, cancellationToken);

    /// <summary>
    /// Creates a minimal valid presentation: one slide master with a theme and three layouts (Title Slide, Title and Content, Blank),
    /// 16:9 slides and no slides. The bytes written for it are the same on every run.
    /// </summary>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <returns>The presentation, which the caller disposes. It saves as PPTX and has no <see cref="OfficeFile.FilePath"/>.</returns>
    public static Presentation Create(OpenContext? context = null)
    {
        context ??= new OpenContext();
        OpcPackage package = OpcPackage.Create(context.Limits);
        try
        {
            NewPresentation.Fill(package);
            return FromPackage(package, context, ownsPackage: true);
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Saves to the file the presentation was opened from, atomically: the new bytes are written to a temporary file beside it and
    /// renamed over it, so a failed or interrupted save leaves the original as it was. Edited parts are written, the others are copied as stored.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save; the original stays as it was.</param>
    /// <exception cref="InvalidOperationException">The presentation was not opened from a path.</exception>
    /// <exception cref="OfficeException">The package cannot be written.</exception>
    public override void Save(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (FilePath is null)
        {
            base.Save(cancellationToken);
            return;
        }

        CommitChanges();
        if (Package is OpcPackage { FilePath: not null } opc)
        {
            opc.Save(cancellationToken);
            return;
        }

        base.Save(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks><c>.pptx</c>, <c>.pptm</c>, <c>.potx</c>, <c>.potm</c>, <c>.ppsx</c>, <c>.ppsm</c> and <c>.ppam</c> name those formats and <c>.xml</c> names Flat OPC.</remarks>
    protected override OfficeFormat ResolveOutputFormat(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase)
            ? OfficeFormat.FlatOpcPresentation
            : base.ResolveOutputFormat(path);
    }

    /// <inheritdoc />
    /// <remarks>The text of the presentation as it is now, edits included: slides in order, each followed by its notes, as <see cref="Office.ExtractText(string, OpenContext?, CancellationToken)"/> gives for the same file.</remarks>
    protected override void ExtractTextCore(TextWriter output, CancellationToken cancellationToken)
    {
        // Write the current package out and read it back through the streaming extractor, so the model and Office.ExtractText agree on every file.
        CommitChanges();
        if (!FormatRegistry.Default.TryGetTextExtractor(Format, out TextExtractionHandler? extractor))
        {
            foreach (Slide slide in Slides)
            {
                output.WriteLine(slide.Text);
            }

            return;
        }

        using var buffer = new MemoryStream();
        Package.WriteTo(buffer, cancellationToken);
        using IByteSource source = ByteSource.FromMemory(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
        extractor(source, Format, Context, output, cancellationToken);
    }

    /// <inheritdoc />
    protected override void WriteCore(OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        if (IsPresentationFormat(format))
        {
            Write(format, destination, cancellationToken);
        }
        else if (format == OfficeFormat.Html)
        {
            WriteHtml(destination, null, cancellationToken);
        }
        else
        {
            base.WriteCore(format, destination, cancellationToken);
        }
    }

    // The writer behind SaveAs(path|stream, format).
    internal void Write(OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        CommitChanges();
        if (format == OfficeFormat.FlatOpcPresentation)
        {
            if (Package is not OpcPackage flat)
            {
                throw new FormatNotAvailableException(format, "write this package as");
            }

            flat.WriteFlatOpcTo(destination, cancellationToken);
            return;
        }

        string target = MainContentTypeOf(format);
        bool macroFree = format is OfficeFormat.Pptx or OfficeFormat.Potx or OfficeFormat.Ppsx;
        if (HasVbaProject && macroFree)
        {
            throw new OfficeUnsupportedException(
                "Saving a presentation that has a VBA project as " + format + " (the macro-free format would silently drop the project; use Pptm, Potm or Ppsm)",
                DefectLocation.InPart(MainPartName));
        }

        IPackagePart main = Package.FindPart(MainPartName)!;
        if (Package is not OpcPackage opc)
        {
            Package.WriteTo(destination, cancellationToken);
            return;
        }

        // A copy in another presentation format differs only in the main part's content type; set it for the write and put it back, so
        // saving a copy never changes the presentation.
        string original = main.ContentType;
        bool retype = !string.Equals(original, target, StringComparison.OrdinalIgnoreCase);
        try
        {
            if (retype)
            {
                opc.SetPartContentType(MainPartName, target);
            }

            opc.WriteZipTo(destination, cancellationToken);
        }
        finally
        {
            if (retype)
            {
                opc.SetPartContentType(MainPartName, original);
            }
        }
    }

    private static string MainContentTypeOf(OfficeFormat format) => format switch
    {
        OfficeFormat.Pptx => Main,
        OfficeFormat.Pptm => MainPptm,
        OfficeFormat.Potx => MainPotx,
        OfficeFormat.Potm => MainPotm,
        OfficeFormat.Ppsx => MainPpsx,
        OfficeFormat.Ppsm => MainPpsm,
        OfficeFormat.Ppam => MainPpam,
        _ => throw new FormatNotAvailableException(format, "write a presentation as"),
    };

    private static OfficeFormat FormatOf(IOpcPackage package, string mainPartName)
    {
        if (package is OpcPackage { IsFlatOpc: true })
        {
            return OfficeFormat.FlatOpcPresentation;
        }

        string type = package.FindPart(mainPartName)?.ContentType ?? string.Empty;
        foreach ((string contentType, OfficeFormat format) in KnownMainTypes)
        {
            if (string.Equals(type, contentType, StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }

        return OfficeFormat.Pptx;
    }

    private static readonly (string ContentType, OfficeFormat Format)[] KnownMainTypes =
    [
        (MainPptm, OfficeFormat.Pptm), (MainPotx, OfficeFormat.Potx), (MainPotm, OfficeFormat.Potm), (MainPpsx, OfficeFormat.Ppsx),
        (MainPpsm, OfficeFormat.Ppsm), (MainPpam, OfficeFormat.Ppam),
    ];

    internal static bool IsPresentationFormat(OfficeFormat format) =>
        format is OfficeFormat.Pptx or OfficeFormat.Pptm or OfficeFormat.Potx or OfficeFormat.Potm or OfficeFormat.Ppsx or OfficeFormat.Ppsm
            or OfficeFormat.Ppam or OfficeFormat.FlatOpcPresentation;

    // The reader registered for the presentation formats; also what Presentation.Open runs.
    internal static Presentation OpenSource(IByteSource source, OpenContext? context, string? filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            OfficeFormat sniffed = FormatSniffer.Detect(source, context.Limits);
            if (sniffed == OfficeFormat.Unknown)
            {
                throw new OfficeFormatException(DefectKind.Structure, "format.unrecognised", "The content is not a format OneDocument knows.");
            }

            if (sniffed == OfficeFormat.Ppt)
            {
                return OpenLegacy(source, context, cancellationToken);
            }

            if (!IsPresentationFormat(sniffed))
            {
                throw new FormatNotAvailableException(sniffed, "open as a presentation:");
            }

            // The package owns the source from here on, and disposes it if the open fails.
            OpcPackage package = filePath is null ? OpcPackage.Open(source, context) : OpcPackage.Open(source, filePath, context);
            try
            {
                return FromPackage(package, context, ownsPackage: true);
            }
            catch
            {
                package.Dispose();
                throw;
            }
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// What did not carry over when the presentation was converted from a legacy binary file (<c>.ppt</c>): every dropped or approximated feature with a count.
    /// Null for presentations opened from PresentationML. The same items are in <see cref="OfficeFile.Defects"/> with the record offset in the source.
    /// </summary>
    public LossReport? ConversionLoss { get; private set; }

    /// <summary>The binary format the presentation was converted from (<see cref="OfficeFormat.Ppt"/>), or null when it was opened as PresentationML.</summary>
    public OfficeFormat? ConvertedFrom { get; private set; }

    // PowerPoint 97-2003 files are read at record level and converted to PresentationML, which the hub then holds as typed views. The result
    // has no file path: saving writes a new PresentationML file, never over the binary original.
    private static Presentation OpenLegacy(IByteSource source, OpenContext context, CancellationToken cancellationToken)
    {
        try
        {
            PptFile file = PptFile.Open(source, context, cancellationToken);
            var loss = new LossReport();
            OpcPackage package = PptConverter.Convert(file, loss, cancellationToken);
            try
            {
                Presentation presentation = FromPackage(package, context, ownsPackage: true);
                presentation.ConversionLoss = loss;
                presentation.ConvertedFrom = OfficeFormat.Ppt;
                return presentation;
            }
            catch
            {
                package.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException or IOException))
        {
            // Records are arbitrary bytes: whatever a damaged file provokes, the caller sees the typed exception.
            throw new OfficeFormatException(DefectKind.Record, "ppt.malformed", "The PowerPoint file could not be read: " + ex.Message, DefectLocation.None, ex);
        }
        finally
        {
            source.Dispose();
        }
    }
}
