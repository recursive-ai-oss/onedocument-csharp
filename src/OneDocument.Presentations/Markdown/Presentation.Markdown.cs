using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Presentations.Markdown;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Presentation
{
    /// <summary>
    /// Writes the presentation as GitHub Flavored Markdown: a second-level heading for each slide, its text in reading order (body placeholders as bullet lists by outline level,
    /// tables as pipe tables) and its speaker notes under a third-level heading. <see cref="ExportLoss"/> lists what Markdown cannot hold.
    /// </summary>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The Markdown.</returns>
    public string ToMarkdown(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        WriteMarkdown(buffer, cancellationToken);
        return new UTF8Encoding(false).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes the presentation as Markdown in UTF-8 without a byte-order mark. See <see cref="ToMarkdown"/>.</summary>
    /// <param name="destination">The stream; it is left open.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    public void WriteMarkdown(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfDisposed();
        CommitChanges();
        var loss = new LossReport();
        MarkdownPresentation.Write(this, destination, loss, cancellationToken);
        ExportLoss = loss;
    }

}
