using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Inspection;

/// <summary>
/// Reports the hidden data and personal information in a file, category by category (<see cref="InspectionCategory"/>), with counts and
/// locations. Nothing is changed and nothing is executed or fetched. OOXML files are inspected as they are; legacy binary, RTF and 2003 XML
/// files are converted to OOXML through the hub first, and the report says so.
/// </summary>
public static class Inspector
{
    /// <summary>Inspects a file.</summary>
    /// <param name="path">The path.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the inspection.</param>
    /// <returns>The report.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format or is damaged beyond repair.</exception>
    /// <exception cref="OfficeException">Any other typed failure of the input (limits, encryption).</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static InspectionReport Inspect(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Inspect(stream, context, cancellationToken);
    }

    /// <summary>Inspects a file held in a seekable stream. The stream stays open.</summary>
    /// <param name="stream">The stream, positioned anywhere; it is read from the start.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the inspection.</param>
    /// <returns>The report.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format or is damaged beyond repair.</exception>
    public static InspectionReport Inspect(Stream stream, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        context ??= new OpenContext();
        using PreparedPackage prepared = PreparedPackage.Open(stream, context, cancellationToken);
        return Run(prepared.Package, prepared.Format, prepared.SourceFormat, prepared.Kind, context, prepared.Notes, prepared.LegacyMacros);
    }

    /// <summary>Inspects an OOXML package that is already open (for example the package of a <c>Document</c>, <c>Workbook</c> or <c>Presentation</c>).</summary>
    /// <param name="package">The package; changes not yet committed to it by a hub model are not seen.</param>
    /// <param name="format">The package's format.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <returns>The report.</returns>
    public static InspectionReport Inspect(IOpcPackage package, OfficeFormat format, OpenContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        return Run(package, format, format, KindOf(format), context ?? new OpenContext(), [], null);
    }

    internal static OfficeKind KindOf(OfficeFormat format) => format switch
    {
        >= OfficeFormat.Docx and <= OfficeFormat.FlatOpcDocument => OfficeKind.Document,
        >= OfficeFormat.Xlsx and <= OfficeFormat.Xlsb => OfficeKind.Workbook,
        >= OfficeFormat.Pptx and <= OfficeFormat.FlatOpcPresentation => OfficeKind.Presentation,
        _ => OfficeKind.Unknown,
    };

    internal static string? MainPartOf(IOpcPackage package)
    {
        foreach (Relationship r in package.GetRelationships(null))
        {
            if (r.Type.EndsWith("/officeDocument", StringComparison.OrdinalIgnoreCase) && OpcPackage.TryResolveTarget(null, r, out PartUri main))
            {
                return main.Value;
            }
        }

        return null;
    }

    internal static InspectionReport Run(IOpcPackage package, OfficeFormat format, OfficeFormat sourceFormat, OfficeKind kind, OpenContext context, IReadOnlyList<string> notes, VbaProject? legacyMacros)
    {
        var session = new InspectionSession(package, format, kind, context, MainPartOf(package));
        Guard(session, "package", () => PackageInspection.Run(session, null));
        foreach (IInspectionProvider provider in FormatRegistry.Default.GetInspectionProviders(kind))
        {
            Guard(session, kind.ToString(), () => provider.Inspect(session));
        }

        List<string> unreachable = session.UnreachableParts();
        if (unreachable.Count > 0)
        {
            session.Note(unreachable.Count + " part(s) are not reachable through any relationship and are never read by Office: " + string.Join(", ", unreachable.Take(5)) + (unreachable.Count > 5 ? ", ..." : string.Empty) + ". Sanitising removes them.");
        }

        if (legacyMacros is not null)
        {
            session.Add(InspectionCategory.Macros, legacyMacros.Container, "VBA project in the original file (not carried into the converted document)", Math.Max(1, legacyMacros.Modules.Count), legacyMacros.Name);
        }

        return Build(session, format, sourceFormat, kind, notes);
    }

    // A provider that meets markup it cannot handle must not take the whole report down: what it found so far stays and the report says
    // that the inspection of that part of the file is incomplete.
    private static void Guard(InspectionSession session, string what, Action inspect)
    {
        try
        {
            inspect();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException or FormatException or OverflowException)
        {
            session.Note("The inspection of the " + what + " parts stopped early (" + ex.GetType().Name + "); the report may be incomplete.");
        }
    }

    internal static InspectionReport Build(InspectionSession session, OfficeFormat format, OfficeFormat sourceFormat, OfficeKind kind, IEnumerable<string> notes)
    {
        List<InspectionFinding> findings = [.. session.Findings.OrderBy(f => f.Category)];
        List<string> allNotes = [.. notes, .. session.Notes];
        return new InspectionReport(format, sourceFormat, kind, findings, allNotes, [.. session.Tips]);
    }
}

/// <summary>
/// Removes hidden data and personal information from a file, writing a new file. Each selected category is removed and recorded; the
/// result is inspected again and every category claimed as removed must be empty in it, or nothing is written.
/// </summary>
public static class Sanitizer
{
    /// <summary>Sanitises a file into another file.</summary>
    /// <param name="input">The file to read; it is not changed unless <paramref name="output"/> is the same path.</param>
    /// <param name="output">The file to write, replaced atomically. Legacy and RTF inputs are written as OOXML and a macro-enabled file whose project is removed in its macro-free format; <see cref="SanitizeReport.OutputFormat"/> says which, so name the file to match.</param>
    /// <param name="options">The categories to remove; null for <see cref="SanitizeOptions.Default"/>.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the operation; nothing is written.</param>
    /// <returns>What was removed, what was kept and why, and the inspection of the result.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format or is damaged beyond repair.</exception>
    /// <exception cref="SanitizeVerificationException">The result still holds something in a category that was reported as removed; nothing was written.</exception>
    /// <exception cref="IOException">A file cannot be read or written.</exception>
    public static SanitizeReport Sanitize(string input, string output, SanitizeOptions? options = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        using var ms = new MemoryStream();
        SanitizeReport report;
        using (FileStream stream = new(input, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            report = Sanitize(stream, ms, options, context, cancellationToken);
        }

        ms.Position = 0;
        AtomicFile.Write(output, s => ms.CopyTo(s));
        return report;
    }

    /// <summary>Sanitises a file held in a seekable stream into another stream.</summary>
    /// <param name="input">The input, read from the start; it stays open.</param>
    /// <param name="output">Receives the new file, only when verification passed.</param>
    /// <param name="options">The categories to remove; null for <see cref="SanitizeOptions.Default"/>.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The report.</returns>
    /// <exception cref="SanitizeVerificationException">The result still holds something in a category that was reported as removed; nothing was written.</exception>
    public static SanitizeReport Sanitize(Stream input, Stream output, SanitizeOptions? options = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        options ??= SanitizeOptions.Default;
        context ??= new OpenContext();
        using PreparedPackage prepared = PreparedPackage.Open(input, context, cancellationToken);
        OpcPackage package = prepared.Package;
        OfficeFormat format = prepared.Format;
        OfficeKind kind = prepared.Kind;
        string? main = Inspector.MainPartOf(package);
        List<string> notes = [.. prepared.Notes];

        InspectionReport before = Inspector.Run(package, format, prepared.SourceFormat, kind, context, notes, prepared.LegacyMacros);
        cancellationToken.ThrowIfCancellationRequested();

        var z = new SanitizeSession(package, format, kind, context, main, options.Categories);
        List<string> unreachable;
        try
        {
            foreach (IInspectionProvider provider in FormatRegistry.Default.GetInspectionProviders(kind))
            {
                provider.Sanitize(z);
                z.Flush();
            }

            PackageInspection.Run(z, z);
            unreachable = z.Finish();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException or FormatException or OverflowException)
        {
            // Half-edited markup is never written: the file stays as it was.
            throw new OfficeFormatException(DefectKind.Structure, "sanitize.failed", "Sanitising stopped on markup it could not edit (" + ex.GetType().Name + ": " + ex.Message + "); nothing was written.", DefectLocation.None, ex);
        }

        if (unreachable.Count > 0)
        {
            notes.Add(unreachable.Count + " part(s) that no relationship reached were removed: " + string.Join(", ", unreachable.Take(10)) + (unreachable.Count > 10 ? ", ..." : string.Empty) + ".");
        }

        OfficeFormat outputFormat = format;
        if (z.RemovedItems.Any(r => r.Category == InspectionCategory.Macros))
        {
            outputFormat = OoxmlVba.MacroFreeFormat(format);
            OoxmlVba.RetypeMain(package, null);
            if (outputFormat != format)
            {
                notes.Add("The macro project was removed, so the file is written as " + outputFormat + ".");
            }
            else
            {
                notes.Add("The macro project was removed; this format has no macro-free counterpart, so it stays " + format + ".");
            }
        }

        if (prepared.LegacyMacros is not null)
        {
            notes.Add("The original file's macro project is not carried into the converted document.");
            if (options.Includes(InspectionCategory.Macros))
            {
                z.Removed(InspectionCategory.Macros, prepared.LegacyMacros.Container, "VBA project of the original file (dropped by the conversion)", Math.Max(1, prepared.LegacyMacros.Modules.Count));
            }
        }

        if (prepared.SourceFormat != prepared.Format)
        {
            outputFormat = prepared.Format;
            notes.Add("The input was converted to " + prepared.Format + " through the document model before sanitising.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (InspectionCategory c in InspectionCategories.All)
        {
            if (options.Includes(c) && InspectionCategories.IsReportOnly(c))
            {
                foreach (InspectionFinding f in before.FindingsOf(c))
                {
                    z.NotRemoved(c, "sanitize.report-only", f.Location, "Sanitising never removes " + InspectionCategories.Name(c) + "; it is reported only.");
                }
            }
        }

        using var written = new MemoryStream();
        package.WriteTo(written, cancellationToken);
        written.Position = 0;
        InspectionReport after;
        using (var check = ByteSource.FromMemory(written.GetBuffer().AsMemory(0, (int)written.Length)))
        {
            using OpcPackage again = OpcPackage.Open(check, new OpenContext(context.Limits, null, context.Password));
            OfficeFormat afterFormat = FormatSniffer.Detect(ByteSource.FromMemory(written.GetBuffer().AsMemory(0, (int)written.Length)), context.Limits);
            after = Inspector.Run(again, afterFormat == OfficeFormat.Unknown ? outputFormat : afterFormat, afterFormat, kind, context, [], null);
        }

        var status = new Dictionary<InspectionCategory, SanitizeStatus>();
        foreach (InspectionCategory c in InspectionCategories.All)
        {
            if (!options.Includes(c))
            {
                status[c] = SanitizeStatus.NotSelected;
            }
            else if (InspectionCategories.IsReportOnly(c))
            {
                status[c] = before.CountOf(c) == 0 ? SanitizeStatus.NothingFound : SanitizeStatus.ReportOnly;
            }
            else if (z.NotRemovedItems.Any(n => n.Category == c))
            {
                status[c] = SanitizeStatus.PartlyRemoved;

                // A category with kept items is still verified: whatever the written file still holds in it must be one of the kept items.
                // Macros are left out because the VBA remover reports its own locations.
                if (c != InspectionCategory.Macros)
                {
                    List<InspectionFinding> unexplained = [.. after.FindingsOf(c).Where(f => !z.NotRemovedItems.Any(n => n.Category == c && Explains(n.Location, f.Location)))];
                    if (unexplained.Count > 0)
                    {
                        throw new SanitizeVerificationException(c, unexplained);
                    }
                }
            }
            else
            {
                status[c] = before.CountOf(c) == 0 && !z.RemovedItems.Any(r => r.Category == c) ? SanitizeStatus.NothingFound : SanitizeStatus.Removed;
                if (after.CountOf(c) > 0)
                {
                    throw new SanitizeVerificationException(c, after.FindingsOf(c));
                }
            }
        }

        written.Position = 0;
        written.CopyTo(output);
        return new SanitizeReport(prepared.SourceFormat, outputFormat, before, after, [.. z.RemovedItems], [.. z.NotRemovedItems], notes, status);
    }

    // A kept item explains a finding when one location names the other (a part name inside "slide 2 (/ppt/slides/slide2.xml)", a sheet
    // inside "Sheet1!rows").
    private static bool Explains(string kept, string found) =>
        kept.Length > 0 && found.Length > 0
        && (found.Contains(kept, StringComparison.OrdinalIgnoreCase) || kept.Contains(found, StringComparison.OrdinalIgnoreCase));
}

/// <summary>An OOXML package ready to inspect: opened as it is, or produced by converting a legacy file through the hub.</summary>
internal sealed class PreparedPackage : IDisposable
{
    private PreparedPackage(OpcPackage package, OfficeFormat sourceFormat, OfficeFormat format, OfficeKind kind, List<string> notes, VbaProject? legacyMacros)
    {
        Package = package;
        SourceFormat = sourceFormat;
        Format = format;
        Kind = kind;
        Notes = notes;
        LegacyMacros = legacyMacros;
    }

    internal OpcPackage Package { get; }

    internal OfficeFormat SourceFormat { get; }

    internal OfficeFormat Format { get; }

    internal OfficeKind Kind { get; }

    internal List<string> Notes { get; }

    internal VbaProject? LegacyMacros { get; }

    internal static PreparedPackage Open(Stream stream, OpenContext context, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        OfficeFormat source;
        using (IByteSource sniff = ByteSource.FromStream(stream, leaveOpen: true))
        {
            source = FormatSniffer.Detect(sniff, context.Limits);
        }

        if (source == OfficeFormat.Unknown)
        {
            throw new OfficeFormatException(DefectKind.Structure, "format.unrecognised", "The content is not a format OneDocument knows.");
        }

        stream.Position = 0;
        OfficeKind kind = Inspector.KindOf(source);
        if (kind != OfficeKind.Unknown)
        {
            // OOXML is read as a package; the stream is copied so the package owns its bytes and the caller's stream stays free.
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            OpcPackage package = OpcPackage.Open(ByteSource.FromMemory(copy.GetBuffer().AsMemory(0, (int)copy.Length)), context);
            return new PreparedPackage(package, source, source, kind, [], null);
        }

        // Everything else goes through the hub and comes out as the OOXML format of its kind.
        VbaProject? macros = null;
        if (source is OfficeFormat.Doc or OfficeFormat.Xls or OfficeFormat.Ppt)
        {
            try
            {
                macros = VbaProject.Open(stream, context, cancellationToken);
            }
            catch (OfficeException)
            {
                macros = null;
            }

            stream.Position = 0;
        }

        IByteSource bytes = ByteSource.FromStream(stream, leaveOpen: true);
        using OfficeFile file = Office.Open(bytes, context, cancellationToken);
        OfficeFormat target = file.Kind switch
        {
            OfficeKind.Document => OfficeFormat.Docx,
            OfficeKind.Workbook => OfficeFormat.Xlsx,
            OfficeKind.Presentation => OfficeFormat.Pptx,
            _ => throw new FormatNotAvailableException(source, "inspect"),
        };
        var converted = new MemoryStream();
        file.SaveAs(converted, target, cancellationToken);
        OpcPackage pkg = OpcPackage.Open(ByteSource.FromMemory(converted.GetBuffer().AsMemory(0, (int)converted.Length)), context);
        return new PreparedPackage(pkg, source, target, file.Kind, ["The file was converted from " + source + " to " + target + " through the document model; the findings describe the converted document."], macros);
    }

    public void Dispose() => Package.Dispose();
}
