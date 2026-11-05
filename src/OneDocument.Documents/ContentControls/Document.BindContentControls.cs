using System.Text;
using RecursiveAI.OneDocument.Documents.ContentControls;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    /// <summary>
    /// Binds data into the custom XML parts and the content controls mapped to them. The data is read the way the template engine
    /// reads it (<c>[TemplateModel]</c> classes, <c>JsonDocument</c>, dictionaries, <c>DataTable</c>), or, when it is a string, a
    /// <c>byte[]</c> or a <c>ReadOnlyMemory&lt;byte&gt;</c>, as XML. Each control's <c>w:dataBinding</c> path is read as a path into
    /// the data (the root element's name is not matched; names are matched ignoring case; <c>[n]</c> takes the n-th item of a list), the
    /// value is written into the bound custom XML node (created when missing), and the control shows it. A repeating section gets one
    /// item per element of the bound list: its first item is the pattern, copied or removed as needed, with the bound paths inside
    /// re-indexed. Controls with no value in the data keep their content. Nothing outside the custom XML parts and the controls changes.
    /// </summary>
    /// <param name="data">The data; null binds nothing but still refreshes the controls from their stores.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What was written and what could not be.</returns>
    /// <exception cref="OfficeUnsupportedException">The data is an object the template engine cannot read without reflection.</exception>
    public ContentBindingReport BindContentControls(object? data, CancellationToken cancellationToken = default)
    {
        ITemplateData? root = data switch
        {
            null => null,
            string xml => TemplateData.FromXml(Encoding.UTF8.GetBytes(xml), Limits),
            byte[] bytes => TemplateData.FromXml(bytes, Limits),
            ReadOnlyMemory<byte> memory => TemplateData.FromXml(memory, Limits),
            _ => TemplateData.From(data),
        };
        return new ContentBinder(this, root, cancellationToken).Run(pushControls: false);
    }

    /// <summary>
    /// Refreshes every bound content control from its custom XML part, expanding or trimming repeating sections to the number of
    /// nodes they are bound to. The parts are not changed.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What was written and what could not be.</returns>
    public ContentBindingReport RefreshContentControls(CancellationToken cancellationToken = default) =>
        new ContentBinder(this, null, cancellationToken).Run(pushControls: false);

    /// <summary>
    /// The reverse of <see cref="RefreshContentControls"/>: writes what the bound controls show into their custom XML nodes (created
    /// when missing). A control that shows its placeholder writes nothing; a drop-down list writes its item's value, a check box
    /// <c>1</c> or <c>0</c> (<c>true</c>/<c>false</c> when the node already used those), a date control its full date as
    /// <c>yyyy-MM-ddTHH:mm:ssZ</c>. Controls are not changed.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What was written and what could not be.</returns>
    public ContentBindingReport PushContentControls(CancellationToken cancellationToken = default) =>
        new ContentBinder(this, null, cancellationToken).Run(pushControls: true);
}

