using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Redaction;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    /// <inheritdoc />
    protected override void RedactRegions(OpcPackage package, RedactionPlan plan, RedactionReport report) =>
        DocumentRegions.Apply(package, plan, report, Limits);
}
