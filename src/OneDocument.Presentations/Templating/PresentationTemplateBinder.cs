using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Presentations.Templating;

/// <summary>Plugs the presentation binder into <see cref="Template"/>, so one call renders a presentation template like any other.</summary>
internal sealed class PresentationTemplateBinder : ITemplateBinder
{
    public static PresentationTemplateBinder Instance { get; } = new();

    public IReadOnlyList<Defect> Validate(OfficeFile file, TemplateOptions options, CancellationToken cancellationToken) =>
        PresentationTemplate.Validate(As(file), options, cancellationToken);

    public IReadOnlyList<Defect> Render(OfficeFile file, ITemplateData data, TemplateOptions options, CancellationToken cancellationToken) =>
        PresentationTemplate.Render(As(file), data, options, cancellationToken).Defects;

    private static Presentation As(OfficeFile file) =>
        file as Presentation ?? throw new FormatNotAvailableException(file.Format, "render a presentation template from a " + file.Kind);
}
