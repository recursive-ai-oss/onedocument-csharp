using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Markdown;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>One stretch of formatted text in converted HTML or Markdown.</summary>
internal sealed record RichRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false, bool Code = false, string? Link = null, bool Break = false);

/// <summary>A converted paragraph: its style, its list membership and its runs.</summary>
/// <param name="Style">A heading level 1 to 6 as "h1".."h6", "quote", "code", or null for a body paragraph.</param>
/// <param name="ListLevel">The nesting level of a list item (from 0), or -1 when the paragraph is not in a list.</param>
/// <param name="Ordered">Whether the list is numbered.</param>
/// <param name="ListId">Identifies the list, so each numbered list starts again at 1.</param>
/// <param name="Runs">The text.</param>
internal sealed record RichBlock(string? Style, int ListLevel, bool Ordered, int ListId, List<RichRun> Runs);

/// <summary>
/// Converts the value of an <c>html</c> or <c>markdown</c> tag into paragraphs (Markdown through the parser in <c>Markdown/</c>). Only a small, safe vocabulary survives: paragraphs, headings, bold, italic,
/// underline, strike-through, code, links, line breaks, quotes and nested lists. Everything else (scripts, styles, forms, images, tables, event attributes)
/// is dropped, and the drop is reported, so the HTML never injects markup of its own into the document.
/// </summary>
internal static class RichContent
{
    public static IReadOnlyList<RichBlock> Convert(RenderContext ctx, TemplatePiece piece)
    {
        string text = piece.Insertion?.Value is { Kind: TemplateDataKind.Scalar } v ? System.Convert.ToString(v.Raw, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
        var dropped = new SortedSet<string>(StringComparer.Ordinal);
        int max = (int)Math.Min(int.MaxValue, (ctx.Options.Limits ?? OfficeLimits.Default).MaxPartSize);
        if (text.Length > max)
        {
            dropped.Add("content over the part size limit");
            text = text[..max];
        }

        var list = piece.Kind == TemplatePieceKind.Html ? Html.HtmlRich.Convert(text, new OpenContext(ctx.Options.Limits ?? OfficeLimits.Default), dropped) : MarkdownRich.Convert(text, dropped, (ctx.Options.Limits ?? OfficeLimits.Default).MaxRecursionDepth);
        if (dropped.Count > 0)
        {
            ctx.Warnings.Add(new Defect(DefectKind.Unsupported, "template.markup-dropped", DefectSeverity.Dropped,
                "The " + (piece.Kind == TemplatePieceKind.Html ? "html" : "markdown") + " tag contained things that were left out: " + string.Join(", ", dropped) + ".",
                DefectLocation.InPart(ctx.CurrentPart)));
        }

        return list;
    }
}
