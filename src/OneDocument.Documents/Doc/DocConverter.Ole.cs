using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Doc;

internal sealed partial class DocConverter
{
    // The summary information streams become the package's core, extended and custom properties ([MS-OSHARED] 2.3.3.2); what has no
    // counterpart is one loss item that names it.
    private void ApplyProperties(OpcPackage pkg)
    {
        if (_f.SummaryInformation.Length == 0 && _f.DocumentSummaryInformation.Length == 0)
        {
            return;
        }

        var props = new DocumentProperties(pkg, _f.Context);
        IReadOnlyList<string> lost = OleDocumentProperties.Read(_f.SummaryInformation, _f.DocumentSummaryInformation, props, _f.Context);
        props.Commit();
        OleDocumentProperties.ReportLoss(lost, _loss, "loss.doc-properties");
    }
}
