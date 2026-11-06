using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <summary>Gets a value indicating whether the package carries a VBA project. It is never read or run.</summary>
    public bool HasVbaProject
    {
        get
        {
            foreach (IPackagePart part in Package.Parts)
            {
                if (string.Equals(part.ContentType, "application/vnd.ms-office.vbaProject", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Reads the workbook's VBA project as data: modules with their source, references, protection and signature streams. Nothing is run.
    /// </summary>
    /// <returns>The project, or null when the workbook has none (a workbook converted from XLS keeps its project only in the original file: read that with <see cref="VbaProject.Open(string, OpenContext, CancellationToken)"/>).</returns>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public VbaProject? GetVbaProject() => VbaProject.FromPackage(Package, OpenContext);

    /// <summary>
    /// Removes the VBA project from the workbook, with its signature parts and the relationships to them. The workbook then saves as a
    /// macro-free format (<c>SaveAs("x.xlsx")</c>); the main part is retyped when it is written.
    /// </summary>
    /// <returns>What was removed. When the workbook had no project, nothing changes and <see cref="VbaRemovalReport.HadProject"/> is false.</returns>
    public VbaRemovalReport RemoveVbaProject() => VbaRemoval.RemoveFromPackage(Package, Format, OpenContext);
}
