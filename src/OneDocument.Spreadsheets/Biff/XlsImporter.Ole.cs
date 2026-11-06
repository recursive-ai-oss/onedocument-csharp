using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsImporter
{
    // The summary information streams become the package's core, extended and custom properties ([MS-OSHARED] 2.3.3.2); what has no
    // counterpart is one loss item that names it.
    private void ApplyProperties()
    {
        if (_summaryInformation.Length == 0 && _documentSummaryInformation.Length == 0)
        {
            return;
        }

        var props = new DocumentProperties(_wb.Package, _context);
        IReadOnlyList<string> lost = OleDocumentProperties.Read(_summaryInformation, _documentSummaryInformation, props, _context);
        props.Commit();
        OleDocumentProperties.ReportLoss(lost, _loss, "loss.document-properties");
    }
}
