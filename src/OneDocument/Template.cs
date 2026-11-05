using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument;

/// <summary>
/// The happy path for templates: open a template, bind data, save the result. Each call identifies the template from its content,
/// finds the binder registered for that format in <see cref="FormatRegistry.Default"/> (the kind's package registers it), renders in
/// memory and writes the output; the output format is the destination's extension, or an explicit format.
/// </summary>
/// <remarks>
/// Data is anything <see cref="TemplateData.From"/> reads: classes marked <c>[TemplateModel]</c>, JSON, dictionaries, data tables, XML.
/// Missing data renders empty and is returned as a warning; set <see cref="TemplateOptions.Strict"/> to throw instead.
/// </remarks>
public static class Template
{
    /// <summary>Renders a template file to a file.</summary>
    /// <param name="templatePath">The template.</param>
    /// <param name="data">The data.</param>
    /// <param name="outputPath">The output; its extension names the format. An existing file is replaced atomically.</param>
    /// <param name="options">Template options, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the render; the destination stays as it was.</param>
    /// <returns>The warnings found while rendering.</returns>
    /// <exception cref="TemplateException">The template is invalid, or strict mode met missing data.</exception>
    /// <exception cref="FormatNotAvailableException">No binder is registered for the template's format.</exception>
    /// <exception cref="OfficeFormatException">The template is not a recognised format, or is damaged beyond repair.</exception>
    public static IReadOnlyList<Defect> Render(string templatePath, object? data, string outputPath, TemplateOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        using OfficeFile file = Office.Open(templatePath, null, cancellationToken);
        IReadOnlyList<Defect> warnings = RenderCore(file, data, options, cancellationToken);
        file.SaveAs(outputPath, cancellationToken);
        return warnings;
    }

    /// <summary>Renders a template stream to a stream.</summary>
    /// <param name="template">The template; it is read to the end and left open.</param>
    /// <param name="data">The data.</param>
    /// <param name="output">Where the result goes; left open.</param>
    /// <param name="outputFormat">The format to write, or null for the template's own format.</param>
    /// <param name="options">Template options, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>The warnings found while rendering.</returns>
    /// <exception cref="TemplateException">The template is invalid, or strict mode met missing data.</exception>
    /// <exception cref="FormatNotAvailableException">No binder is registered for the template's format, or no writer for the output format.</exception>
    public static IReadOnlyList<Defect> Render(Stream template, object? data, Stream output, OfficeFormat? outputFormat = null, TemplateOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(output);
        using OfficeFile file = Office.Open(ByteSource.FromStream(template, leaveOpen: true), null, cancellationToken);
        IReadOnlyList<Defect> warnings = RenderCore(file, data, options, cancellationToken);
        file.SaveAs(output, outputFormat ?? file.Format, cancellationToken);
        return warnings;
    }

    /// <summary>Renders an opened file in place; the caller saves it. The other binders and the mail-merge paths use the same dispatch.</summary>
    /// <param name="file">The opened template; it becomes the output.</param>
    /// <param name="data">The data.</param>
    /// <param name="options">Template options, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>The warnings found while rendering.</returns>
    /// <exception cref="TemplateException">The template is invalid, or strict mode met missing data.</exception>
    /// <exception cref="FormatNotAvailableException">No binder is registered for the file's format.</exception>
    public static IReadOnlyList<Defect> Render(OfficeFile file, object? data, TemplateOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        return RenderCore(file, data, options, cancellationToken);
    }

    /// <summary>Checks a template without binding data.</summary>
    /// <param name="templatePath">The template.</param>
    /// <param name="options">Template options (delimiters), or null.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Every error and warning with its place; empty when the template is valid.</returns>
    /// <exception cref="FormatNotAvailableException">No binder is registered for the template's format.</exception>
    public static IReadOnlyList<Defect> Validate(string templatePath, TemplateOptions? options = null, CancellationToken cancellationToken = default)
    {
        using OfficeFile file = Office.Open(templatePath, null, cancellationToken);
        return Validate(file, options, cancellationToken);
    }

    /// <summary>Checks an opened template without binding data.</summary>
    /// <param name="file">The opened template; not changed.</param>
    /// <param name="options">Template options (delimiters), or null.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Every error and warning with its place; empty when the template is valid.</returns>
    /// <exception cref="FormatNotAvailableException">No binder is registered for the file's format.</exception>
    public static IReadOnlyList<Defect> Validate(OfficeFile file, TemplateOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        return FormatRegistry.Default.GetTemplateBinder(file.Format).Validate(file, options ?? new TemplateOptions(), cancellationToken);
    }

    private static IReadOnlyList<Defect> RenderCore(OfficeFile file, object? data, TemplateOptions? options, CancellationToken cancellationToken)
    {
        ITemplateBinder binder = FormatRegistry.Default.GetTemplateBinder(file.Format);
        return binder.Render(file, TemplateData.From(data, options?.MaxRows ?? 1_000_000), options ?? new TemplateOptions(), cancellationToken);
    }
}
