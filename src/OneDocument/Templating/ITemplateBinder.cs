using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Templating;

/// <summary>
/// The template binder of one kind of document: it reads the template text out of an opened file, hands it to <see cref="TemplateEngine"/>
/// and rebuilds the file from what the engine returns. Each kind package registers its binder for the formats it handles with
/// <see cref="Formats.FormatRegistry.RegisterTemplateBinder"/>; <see cref="Template"/> dispatches on the format the content sniffer found.
/// </summary>
public interface ITemplateBinder
{
    /// <summary>Finds every problem in a template without binding any data.</summary>
    /// <param name="file">The opened template. It is not changed.</param>
    /// <param name="options">The options; the binder supplies its own scope resolver for each story or part it reads.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The defects, each located (part URI and offset inside the unit's text); empty for a valid template.</returns>
    IReadOnlyList<Defect> Validate(OfficeFile file, TemplateOptions options, CancellationToken cancellationToken);

    /// <summary>Renders the template in place: the opened file becomes the output document, which the caller then saves.</summary>
    /// <param name="file">The opened template; it is edited.</param>
    /// <param name="data">The data the expressions read.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>The warnings (missing data, dropped markup); errors throw <see cref="TemplateException"/>.</returns>
    /// <exception cref="TemplateException">The template is invalid, or strict mode met missing data.</exception>
    IReadOnlyList<Defect> Render(OfficeFile file, ITemplateData data, TemplateOptions options, CancellationToken cancellationToken);
}
