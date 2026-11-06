using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsImporter
{
    /// <summary>One Obj record of a sheet: its type (<c>ot</c>), identifier, and the embedding storage it names, if any.</summary>
    private readonly record struct ObjInfo(int Type, int Id, string? Storage);

    /// <summary>An embedded object ready to be written: its storage, what the storage says about it, the preview picture and the cell anchor of its shape.</summary>
    private sealed record OleOut(OleStorageImage Storage, OleObjectInfo Info, Blip? Preview, int[] Anchor, int ObjIndex);

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

    // Pairs the embedded-object Obj records of the sheet with their shapes and storages, once per sheet. The shapes with client data and the
    // Obj records after the drawing are in the same order ([MS-XLS] 2.4.181); when their counts differ the pairing is not trusted and the objects
    // stay pictures.
    private List<OleOut> PrepareOle()
    {
        SheetFeatureData f = _sheetFeatures;
        if (f.Ole is not null)
        {
            return f.Ole;
        }

        f.Ole = [];
        int named = f.Objects.Count(o => o.Storage is not null);
        if (named == 0)
        {
            return f.Ole;
        }

        byte[] stream = f.Drawing.SelectMany(x => x).ToArray();
        var pictures = new List<(int Blip, int[] Anchor, int Obj)>();
        int clientShapes = 0;
        CollectPictures(stream, 0, stream.Length, pictures, 0, ref clientShapes);
        if (clientShapes != f.Objects.Count)
        {
            Defect("biff.object-count-mismatch", DefectSeverity.Repaired, "The drawing has " + clientShapes.ToString(CultureInfo.InvariantCulture) + " shapes with object records and the sheet has " + f.Objects.Count.ToString(CultureInfo.InvariantCulture)
                + " Obj records; the embedded objects are not matched to their shapes and keep their pictures.", 0);
            Lose("loss.ole-objects", "embedded objects", named, "The objects could not be matched to their shapes; they keep their preview pictures and the object data is not carried.");
            return f.Ole;
        }

        List<Blip?> blips = Blips();
        foreach ((int blipIndex, int[] anchor, int obj) in pictures)
        {
            if (obj < 0 || f.Objects[obj].Storage is not { } name)
            {
                continue;
            }

            OleStorageImage? storage = _objects.Find(name);
            if (storage is null)
            {
                Defect("biff.embedding-storage-missing", DefectSeverity.Dropped, "The Obj record names the embedding storage '" + name + "', which the file does not have; the object keeps its picture only.", 0, DefectKind.Reference);
                continue;
            }

            OleObjectInfo info = storage.Describe();
            if (info.Kind == OleObjectKind.Linked)
            {
                continue; // a linked object has no data to carry; it keeps its picture
            }

            Blip? preview = blipIndex >= 1 && blipIndex <= blips.Count ? blips[blipIndex - 1] : null;
            f.Ole.Add(new OleOut(storage, info, preview, anchor, obj));
            f.OleTaken.Add(obj);
        }

        long notCarried = named - f.Ole.Count;
        if (notCarried > 0)
        {
            Lose("loss.ole-objects", "embedded and linked objects", notCarried, "Linked objects, and objects whose storage or shape is missing, keep their preview pictures only; the object data is not carried.");
        }

        return f.Ole;
    }

    // Writes one picture shape per object into the legacy drawing, writes the objects as parts, and returns the sheet's oleObjects element.
    // ECMA-376 Part 1 18.3.1.60 (oleObject): shapeId names the shape in the legacy drawing, and r:id the embedded part.
    private string WriteOleObjectShapes(StringBuilder vml, List<Relationship> vmlRels, List<Relationship> sheetRels, Func<string> relId, int drawingNumber, int noteCount, List<OleOut> objects)
    {
        if (objects.Count == 0)
        {
            return string.Empty;
        }

        vml.Append(OleEmbedding.PictureShapeType);
        var sb = new StringBuilder("<oleObjects>");
        int written = 0;
        for (int k = 0; k < objects.Count; k++)
        {
            OleOut o = objects[k];
            byte[] bin;
            try
            {
                bin = o.Storage.ToCompoundFile(_context.Limits);
            }
            catch (OfficeException ex)
            {
                _context.Defects.Report(ex.Defect with { Severity = DefectSeverity.Dropped });
                Lose("loss.ole-objects", "embedded objects", 1, "The object '" + o.Storage.Name + "' could not be written as a compound file; it keeps its picture only.");
                continue;
            }

            int number = ++_oleCounter;
            string partName = "/xl/embeddings/oleObject" + I(number) + ".bin";
            _wb.Package.AddPart(partName, OleEmbedding.ObjectContentType, bin);
            string rid = relId();
            sheetRels.Add(new Relationship(rid, OleEmbedding.ObjectRelationshipType, "../embeddings/oleObject" + I(number) + ".bin"));

            string? imageRid = null;
            if (o.Preview is { } blip)
            {
                string media = "/xl/media/image" + I(++_imageCounter) + "." + blip.Extension;
                _wb.Package.AddPart(media, blip.ContentType, blip.Data);
                imageRid = "rId" + I(vmlRels.Count + 1);
                vmlRels.Add(new Relationship(imageRid, RelBase + "image", "../media/image" + I(_imageCounter) + "." + blip.Extension));
            }

            int shapeId = (1024 * drawingNumber) + 1 + noteCount + written;
            written++;
            int[] a = o.Anchor;
            double width = Extent(a, 0, 1, 4, 5, 64) / 12700.0;
            double height = Extent(a, 2, 3, 6, 7, 20) / 12700.0;
            vml.Append("<v:shape id=\"_x0000_s").Append(I(shapeId)).Append("\" type=\"#_x0000_t75\" style=\"position:absolute;margin-left:0;margin-top:0;width:")
                .Append(width.ToString("0.##", CultureInfo.InvariantCulture)).Append("pt;height:").Append(height.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("pt;z-index:").Append(I(noteCount + written)).Append("\" o:ole=\"\" filled=\"t\" fillcolor=\"window\" stroked=\"t\" strokecolor=\"windowText\" o:insetmode=\"auto\">");
            if (imageRid is not null)
            {
                vml.Append("<v:imagedata o:relid=\"").Append(imageRid).Append("\" o:title=\"\"/>");
            }

            // [MS-XLS] 2.5.14 ClientAnchor: column, offset (1/1024 of a cell width), row, offset (1/256 of a row height); VML takes pixels.
            vml.Append("<x:ClientData ObjectType=\"Pict\"><x:SizeWithCells/><x:Anchor>").Append(I(a[0])).Append(", ").Append(I(a[1] * 64 / 1024)).Append(", ").Append(I(a[2])).Append(", ").Append(I(a[3] * 20 / 256))
                .Append(", ").Append(I(a[4])).Append(", ").Append(I(a[5] * 64 / 1024)).Append(", ").Append(I(a[6])).Append(", ").Append(I(a[7] * 20 / 256))
                .Append("</x:Anchor><x:CF>Pict</x:CF><x:AutoPict/></x:ClientData></v:shape>");

            sb.Append("<oleObject");
            if (o.Info.ProgId is { } progId)
            {
                sb.Append(" progId=\"").Append(Esc(progId)).Append('"');
            }

            sb.Append(" shapeId=\"").Append(I(shapeId)).Append("\" xmlns:r=\"").Append(RelNs).Append("\" r:id=\"").Append(rid).Append("\"/>");
        }

        sb.Append("</oleObjects>");
        return written == 0 ? string.Empty : sb.ToString();
    }

    private int _oleCounter;
}
