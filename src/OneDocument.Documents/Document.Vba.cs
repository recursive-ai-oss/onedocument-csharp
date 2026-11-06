using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    /// <summary>
    /// Reads the document's VBA project as data: modules with their source, references, protection and signature streams. Nothing is run.
    /// </summary>
    /// <returns>The project, or null when the document has none (a document converted from DOC keeps its project only in the original file: read that with <see cref="VbaProject.Open(string, OpenContext, CancellationToken)"/>).</returns>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public VbaProject? GetVbaProject() => VbaProject.FromPackage(Package, Context);

    /// <summary>
    /// Removes the VBA project from the document, with its signature parts, the form-data part and the relationships to them. The document
    /// then saves as a macro-free format (<c>SaveAs("x.docx")</c>); the main part is retyped when it is written.
    /// </summary>
    /// <returns>What was removed. When the document had no project, nothing changes and <see cref="VbaRemovalReport.HadProject"/> is false.</returns>
    public VbaRemovalReport RemoveVbaProject() => VbaRemoval.RemoveFromPackage(Package, Format, Context);
}
