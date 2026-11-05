using RecursiveAI.OneOffice.Properties;

namespace RecursiveAI.OneOffice.Documents;

public sealed partial class Document
{
    private DocumentProperties? _properties;

    /// <summary>
    /// Gets the document's core, extended and custom properties. Nothing is read until one is asked for; a part nobody edits is
    /// left as stored, and <see cref="CommitChanges"/> writes the edited ones. Dates are set by the caller: the library never reads the clock.
    /// </summary>
    public DocumentProperties Properties => Init(ref _properties, () => new DocumentProperties(Package, Context));
}
