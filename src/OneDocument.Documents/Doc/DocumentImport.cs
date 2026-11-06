using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Doc;
using RecursiveAI.OneDocument.Doc.Binary;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    private OfficeFormat? _sourceFormat;

    /// <summary>
    /// Gets the format the document was read from. It is <see cref="OfficeFile.Format"/> for DOCX and the other package formats, and
    /// <see cref="OfficeFormat.Doc"/> for a Word 97-2003 or Word 6/95 file, which is converted into the same model when it is opened.
    /// </summary>
    public OfficeFormat SourceFormat => _sourceFormat ?? Format;

    /// <summary>
    /// Gets what the conversion from a binary Word file or RTF could not carry over, or <c>null</c> for a document that was not converted.
    /// Entries have <c>loss.doc-*</c> codes for Word binary files and <c>loss.rtf-*</c> codes for RTF (see <c>docs/doc.md</c> and
    /// <c>docs/rtf.md</c>); the located detail of each repair is in <see cref="OfficeFile.Defects"/>. Reading RTF goes through the DOCX
    /// model, so a document opened from RTF is a WordprocessingML package in memory.
    /// </summary>
    public LossReport? ImportLoss { get; private set; }

    // Reads a Word binary file and converts it into the model. The file is read in full and the source released: the result is
    // an ordinary document that saves as DOCX (it has no path of its own, so Save() asks for SaveAs).
    internal static Document OpenBinary(IByteSource source, OpenContext context)
    {
        var loss = new LossReport();
        OpcPackage package = OpcPackage.Create(context.Limits);
        try
        {
            DocFile file = DocGuard.Run(() => DocFile.Open(source, context));
            source.Dispose();
            DocGuard.Run(() => new DocConverter(file, loss).Build(package));
            Document doc = FromPackage(package, context, ownsPackage: true);
            doc._sourceFormat = OfficeFormat.Doc;
            doc.ImportLoss = loss;
            return doc;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }
}
