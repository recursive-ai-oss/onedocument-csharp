using System.Text;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsImporter
{
    // Pictures and shapes: filled in by the drawing converter; without one the shapes are counted in the loss report.
    private void WriteDrawing(SheetCtx sc, List<Relationship> rels, Func<string> relId)
    {
        _ = sc;
        _ = rels;
        _ = relId;
        _ = Encoding.UTF8;
        _drawingConverted = false;
    }
}
