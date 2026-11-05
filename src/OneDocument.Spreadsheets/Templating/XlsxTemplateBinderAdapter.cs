using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Spreadsheets.Templating;

/// <summary>Plugs the workbook binder into <see cref="Template"/>, so one call renders a workbook template like any other.</summary>
internal sealed class XlsxTemplateBinderAdapter : ITemplateBinder
{
    public static XlsxTemplateBinderAdapter Instance { get; } = new();

    public IReadOnlyList<Defect> Validate(OfficeFile file, TemplateOptions options, CancellationToken cancellationToken) =>
        XlsxTemplateBinder.Validate(As(file), options, cancellationToken);

    public IReadOnlyList<Defect> Render(OfficeFile file, ITemplateData data, TemplateOptions options, CancellationToken cancellationToken) =>
        XlsxTemplateBinder.Render(As(file), data, options, null, cancellationToken).Warnings;

    private static Workbook As(OfficeFile file) =>
        file as Workbook ?? throw new FormatNotAvailableException(file.Format, "render a workbook template from a " + file.Kind);
}
