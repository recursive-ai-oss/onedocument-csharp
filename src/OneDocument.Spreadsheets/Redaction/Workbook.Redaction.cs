using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Redaction;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <inheritdoc />
    protected override void RedactRegions(OpcPackage package, RedactionPlan plan, RedactionReport report) =>
        SpreadsheetRegions.Apply(package, plan, report, Limits);
}
