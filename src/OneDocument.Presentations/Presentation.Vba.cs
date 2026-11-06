using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Presentation
{
    /// <summary>
    /// Reads the presentation's VBA project as data: modules with their source, references, protection and signature streams. Nothing is run.
    /// </summary>
    /// <returns>The project, or null when the presentation has none (a presentation converted from PPT keeps its project only in the original file: read that with <see cref="VbaProject.Open(string, OpenContext, CancellationToken)"/>).</returns>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public VbaProject? GetVbaProject() => VbaProject.FromPackage(Package, Context);

    /// <summary>
    /// Removes the VBA project from the presentation, with its signature parts and the relationships to them. The presentation then saves as a
    /// macro-free format (<c>SaveAs("x.pptx")</c>); the main part is retyped when it is written.
    /// </summary>
    /// <returns>What was removed. When the presentation had no project, nothing changes and <see cref="VbaRemovalReport.HadProject"/> is false.</returns>
    public VbaRemovalReport RemoveVbaProject() => VbaRemoval.RemoveFromPackage(Package, Format, Context);
}
