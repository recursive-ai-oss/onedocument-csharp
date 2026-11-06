using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Ppt.Binary;

public static partial class PptConverter
{
    // The summary information streams become the package's core, extended and custom properties ([MS-OSHARED] 2.3.3.2); what has no
    // counterpart is one loss item that names it. Written after the package relationships are set, which replaces them.
    private static void ApplyProperties(PptPresentation p, OpcPackage package, PptConversion conv)
    {
        if (p.File.SummaryInformation.IsEmpty && p.File.DocumentSummaryInformation.IsEmpty)
        {
            return;
        }

        var props = new DocumentProperties(package, p.File.Context);
        IReadOnlyList<string> lost = OleDocumentProperties.Read(p.File.SummaryInformation, p.File.DocumentSummaryInformation, props, p.File.Context);
        props.Commit();
        conv.Loss.Offset = -1;
        OleDocumentProperties.ReportLoss(lost, conv.Loss, "ppt.document-properties-not-carried");
    }
}
