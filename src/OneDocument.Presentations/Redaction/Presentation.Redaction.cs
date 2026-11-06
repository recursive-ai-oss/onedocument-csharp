using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Redaction;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Presentation
{
    /// <inheritdoc />
    protected override void RedactRegions(OpcPackage package, RedactionPlan plan, RedactionReport report) =>
        PresentationRegions.Apply(package, plan, report, Limits);
}
