using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Redaction;

internal static class Redactor
{
    public static OfficeFormat OutputFormat(OfficeFile file) => file.Format switch
    {
        OfficeFormat.Docx or OfficeFormat.Docm or OfficeFormat.Dotx or OfficeFormat.Dotm
            or OfficeFormat.Xlsx or OfficeFormat.Xlsm or OfficeFormat.Xltx or OfficeFormat.Xltm or OfficeFormat.Xlam
            or OfficeFormat.Pptx or OfficeFormat.Pptm or OfficeFormat.Potx or OfficeFormat.Potm or OfficeFormat.Ppsx or OfficeFormat.Ppsm or OfficeFormat.Ppam => file.Format,
        _ => file.Kind switch
        {
            OfficeKind.Document => OfficeFormat.Docx,
            OfficeKind.Workbook => OfficeFormat.Xlsx,
            OfficeKind.Presentation => OfficeFormat.Pptx,
            _ => throw new FormatNotAvailableException(file.Format, "redaction"),
        },
    };

    public static RedactionReport Run(OfficeFile file, RedactionPlan plan, Stream destination, Action<OpcPackage, RedactionPlan, RedactionReport> regions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Options ??= new RedactionOptions();
        OfficeFormat format = OutputFormat(file);
        var report = new RedactionReport { OutputFormat = format, Converted = format != file.Format };
        var ctx = new RedactionContext(plan, report, file.Limits);

        using var staged = new MemoryStream();
        file.SaveAs(staged, format, cancellationToken);
        staged.Position = 0;
        byte[] output;
        using (OpcPackage package = OpcPackage.Open(staged, new OpenContext(file.Limits), leaveOpen: true))
        {
            List<string>? removedByRegions = null;
            if (plan.Regions.Count > 0)
            {
                Dictionary<string, List<string>> before = RegionResidue.Units(package, file.Limits);
                regions(package, plan, report);
                removedByRegions = RegionResidue.Removed(before, RegionResidue.Units(package, file.Limits));
            }

            cancellationToken.ThrowIfCancellationRequested();
            PackageRedactor.Run(package, ctx, string.Empty, 0);
            if (removedByRegions is { Count: > 0 })
            {
                RegionResidue.Report(removedByRegions, RegionResidue.Units(package, file.Limits), report);
            }

            using var written = new MemoryStream();
            package.WriteTo(written, cancellationToken);
            output = written.ToArray();
        }

        // Untouched entries keep their zip comments and extra fields; those sit outside every part.
        if (ZipMetadata.Clear(output) is > 0 and int cleared)
        {
            report.Add("zip-metadata", "/", cleared);
        }

        if (plan.Options.Verify)
        {
            new RedactionVerifier(ctx).Verify(output);
        }

        destination.Write(output);
        return report;
    }

    public static RedactionReport Run(OfficeFile file, RedactionPlan plan, string outputPath, Action<OpcPackage, RedactionPlan, RedactionReport> regions, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        if (file.FilePath is { } source && string.Equals(Path.GetFullPath(source), Path.GetFullPath(outputPath), StringComparison.Ordinal))
        {
            throw new ArgumentException("Redaction writes a new file; the output path is the source file.", nameof(outputPath));
        }

        RedactionReport? report = null;
        AtomicFile.Write(outputPath, stream => report = Run(file, plan, stream, regions, cancellationToken));
        return report!;
    }
}
