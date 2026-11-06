using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Doc;

internal sealed partial class DocConverter
{
    private int _oleNo;
    private int _shapeNo = 1024;

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

    // An EMBED or LINK field whose separator is an object placeholder ([MS-DOC] sprmCFOle2) is replaced by the object element Word writes:
    // a picture shape with the preview the field result showed, and the object itself as a related part (an embedded storage written as a
    // compound file) or as an external relationship (a link). The field's XML, already written, is cut back to where the field began.
    private void WriteObject(StringBuilder sb, StoryState st, FieldMark mark, string instr, string rprXml, uint id)
    {
        string name = "_" + id.ToString(CultureInfo.InvariantCulture);
        OleStorageImage? storage = _f.Objects.Find(name);
        if (storage is null)
        {
            Skip("doc.ole-storage-missing", "The field refers to the object storage '" + name + "', which the ObjectPool does not have; the object keeps its preview picture only.", id);
            return;
        }

        if (!ReferenceEquals(mark.Owner, sb) || mark.Begin > sb.Length)
        {
            Skip("doc.ole-object-in-markup", "The object '" + name + "' is in a field that spans paragraphs; it keeps its preview picture only.", id);
            return;
        }

        string slice = sb.ToString(mark.Begin, sb.Length - mark.Begin);
        if (slice.Contains("<w:ins ", StringComparison.Ordinal) || slice.Contains("<w:del ", StringComparison.Ordinal) || slice.Contains("</w:ins>", StringComparison.Ordinal) || slice.Contains("</w:del>", StringComparison.Ordinal)
            || slice.Contains("<w:bookmark", StringComparison.Ordinal) || slice.Contains("<w:comment", StringComparison.Ordinal) || slice.Contains("<w:object", StringComparison.Ordinal))
        {
            Skip("doc.ole-object-in-markup", "The object '" + name + "' sits inside tracked changes, a bookmark or a comment range; it keeps its preview picture only.", id);
            return;
        }

        OleObjectInfo info = storage.Describe();
        string? progId = ProgIdOf(instr) ?? info.ProgId;
        (long cx, long cy) = mark.Preview is { } pv ? (pv.Cx, pv.Cy) : (914400, 914400);
        string rel;
        string type;
        if (info.Kind == OleObjectKind.Linked)
        {
            if (info.LinkTarget is null)
            {
                Skip("doc.ole-link-unreadable", "The linked object '" + name + "' has no readable file moniker; it keeps its preview picture only.", id);
                return;
            }

            rel = st.Part.AddRel(OleEmbedding.ObjectRelationshipType, OleEmbedding.FileUri(info.LinkTarget), external: true);
            type = "Link";
        }
        else
        {
            byte[] bin;
            try
            {
                bin = storage.ToCompoundFile(_f.Context.Limits);
            }
            catch (OfficeException ex)
            {
                _sink.Report(ex.Defect with { Severity = DefectSeverity.Dropped });
                return;
            }

            int n = ++_oleNo;
            string target = "embeddings/oleObject" + n.ToString(CultureInfo.InvariantCulture) + ".bin";
            _media.Add(("/word/" + target, OleEmbedding.ObjectContentType, bin));
            rel = st.Part.AddRel(OleEmbedding.ObjectRelationshipType, target);
            type = "Embed";
        }

        string shape = "_x0000_i" + (++_shapeNo).ToString(CultureInfo.InvariantCulture);
        string width = (cx / 12700.0).ToString("0.##", CultureInfo.InvariantCulture);
        string height = (cy / 12700.0).ToString("0.##", CultureInfo.InvariantCulture);
        sb.Length = mark.Begin;
        sb.Append("<w:r>").Append(rprXml).Append("<w:object w:dxaOrig=\"").Append((cx / EmuPerTwip).ToString(CultureInfo.InvariantCulture)).Append("\" w:dyaOrig=\"")
            .Append((cy / EmuPerTwip).ToString(CultureInfo.InvariantCulture)).Append("\">");
        if (!st.ShapeTypeWritten)
        {
            sb.Append(OleEmbedding.PictureShapeType);
            st.ShapeTypeWritten = true;
        }

        sb.Append("<v:shape id=\"").Append(shape).Append("\" type=\"#_x0000_t75\" style=\"width:").Append(width).Append("pt;height:").Append(height).Append("pt\" o:ole=\"\">");
        if (mark.Preview is { } preview)
        {
            sb.Append("<v:imagedata r:id=\"").Append(preview.Rid).Append("\" o:title=\"\"/>");
        }

        sb.Append("</v:shape><o:OLEObject Type=\"").Append(type).Append('"');
        if (progId is not null)
        {
            sb.Append(" ProgID=\"").Esc(progId).Append('"');
        }

        sb.Append(" ShapeID=\"").Append(shape).Append("\" DrawAspect=\"Content\" ObjectID=\"").Append(name).Append("\" r:id=\"").Append(rel).Append('"');
        if (type == "Link")
        {
            sb.Append(instr.Contains("\\a", StringComparison.Ordinal) ? " UpdateMode=\"Always\"" : " UpdateMode=\"OnCall\"");
        }

        sb.Append("/></w:object></w:r>");
        _oleCarried++;
    }

    private void Skip(string code, string message, uint id) =>
        _sink.Report(new Defect(DefectKind.Reference, code, DefectSeverity.Dropped, message, DefectLocation.InRecord("/ObjectPool/_" + id.ToString(CultureInfo.InvariantCulture))));

    // The field instruction names the programmatic identifier right after the field name: EMBED Excel.Sheet.8 \s, LINK Word.Document.8 "path" ...
    private static string? ProgIdOf(string instr)
    {
        string[] words = instr.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !(words[0].Equals("EMBED", StringComparison.OrdinalIgnoreCase) || words[0].Equals("LINK", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string w = words[1];
        if (w.Length is 0 or > 128 || w[0] is '\\' or '"' || !w.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            return null;
        }

        return w;
    }
}
