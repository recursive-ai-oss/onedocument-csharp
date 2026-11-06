using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Doc.Binary;

namespace RecursiveAI.OneDocument.Doc;

internal sealed partial class DocConverter
{
    private const long EmuPerTwip = 635;

    // Adds a picture as a media part related to `part` and returns the relationship identifier.
    private string AddMedia(Part part, ArtPicture pic)
    {
        string name = "/word/media/image" + Seq(_media.Count + 1) + "." + pic.Extension;
        _media.Add((name, pic.ContentType, pic.Bytes));
        return part.AddRel("http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/image" + Seq(_media.Count) + "." + pic.Extension);
    }

    private static string GraphicXml(string rid, long cx, long cy, int id)
    {
        string c = cx.ToString(CultureInfo.InvariantCulture);
        string h = cy.ToString(CultureInfo.InvariantCulture);
        return "<wp:docPr id=\"" + Seq(id) + "\" name=\"Picture " + Seq(id) + "\"/><wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>" +
            "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"" + Seq(id) + "\" name=\"Picture " + Seq(id) + "\"/><pic:cNvPicPr/></pic:nvPicPr>" +
            "<pic:blipFill><a:blip r:embed=\"" + rid + "\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
            "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"" + c + "\" cy=\"" + h + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic>";
    }

    // An inline picture: the character is 0x01 with sprmCPicLocation pointing at a PICF in the Data stream.
    private void WritePicture(StringBuilder sb, string rprXml, RunFormat rf, StoryState st)
    {
        if (rf.IsData)
        {
            _formFields++;
            return;
        }

        if (rf.IsOle)
        {
            // For an embedded object the location is the object's storage number, not a picture; the object is counted with its field.
            return;
        }

        bool located = rf.PicLocation >= 0 && rf.PicLocation + 68L <= _f.Data.Length;
        ArtPicture? pic = located ? DocArt.ReadInlinePicture(_f.Data, rf.PicLocation, out _, out _, out _) : null;
        if (pic is null)
        {
            if (located)
            {
                // A shape with no picture inside it (a drawing, a linked picture): counted as a dropped shape.
                _shapes++;
                return;
            }

            _sink.Report(new Defect(DefectKind.Record, "doc.picture-unreadable", DefectSeverity.Dropped, "A picture character has no readable picture: its location " + Seq(rf.PicLocation) + " lies outside the Data stream (" + Seq(_f.Data.Length) + " bytes).", DefectLocation.InRecord("/Data", rf.PicLocation)));
            return;
        }

        DocArt.ReadInlinePicture(_f.Data, rf.PicLocation, out int w, out int h, out _);
        string rid = AddMedia(st.Part, pic.Value);
        long cx = Math.Max(1, w) * EmuPerTwip;
        long cy = Math.Max(1, h) * EmuPerTwip;
        if (st.Marks.Count > 0 && st.Marks[^1].ObjectId is not null)
        {
            st.Marks[^1].Preview = (rid, cx, cy);
        }

        sb.Append("<w:r>").Append(rprXml).Append("<w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
            .Append("\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>").Append(GraphicXml(rid, cx, cy, _drawingId++)).Append("</wp:inline></w:drawing></w:r>");
    }

    // A floating object: the character is 0x08, and the shape table says where it sits and what it shows.
    private void WriteShapeAnchor(StringBuilder sb, string rprXml, int cp, StoryState st)
    {
        if (st.Kind != StoryKind.Main || !_spa.TryGetValue(cp, out SpaInfo spa))
        {
            _shapes++;
            return;
        }

        ArtPicture? pic = null;
        if (_art is { } art && art.ShapePicture.TryGetValue(spa.Spid, out int pib) && pib >= 1 && pib <= art.Store.Count)
        {
            (long fo, ArtPicture? emb) = art.Store[pib - 1];
            pic = emb ?? DocArt.ReadAt(_f.Word, fo);
        }

        if (pic is null)
        {
            if (_art is { } a2 && a2.TextShapes.Contains(spa.Spid))
            {
                return; // a text box: its text is appended after the body
            }

            _shapes++;
            return;
        }

        // [MS-DOC] Spa: bx (bits 1-2) and by (bits 3-4) say what the rectangle is measured from; wr (bits 5-8) is the wrapping style.
        int bx = (spa.Flags >> 1) & 3;
        int by = (spa.Flags >> 3) & 3;
        int wr = (spa.Flags >> 5) & 0xF;
        bool behind = (spa.Flags & 0x4000) != 0;
        long cx = Math.Max(1, spa.Right - spa.Left) * EmuPerTwip;
        long cy = Math.Max(1, spa.Bottom - spa.Top) * EmuPerTwip;
        string rid = AddMedia(st.Part, pic.Value);
        string hRel = bx switch { 0 => "margin", 1 => "page", _ => "column" };
        string vRel = by switch { 0 => "margin", 1 => "page", _ => "paragraph" };
        string wrap = wr switch
        {
            1 => "<wp:wrapTopAndBottom/>",
            3 => "<wp:wrapNone/>",
            4 => "<wp:wrapTight wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/><wp:lineTo x=\"0\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"0\"/><wp:lineTo x=\"0\" y=\"0\"/></wp:wrapPolygon></wp:wrapTight>",
            5 => "<wp:wrapThrough wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/><wp:lineTo x=\"0\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"0\"/><wp:lineTo x=\"0\" y=\"0\"/></wp:wrapPolygon></wp:wrapThrough>",
            _ => "<wp:wrapSquare wrapText=\"bothSides\"/>",
        };

        sb.Append("<w:r>").Append(rprXml).Append("<w:drawing><wp:anchor distT=\"0\" distB=\"0\" distL=\"114300\" distR=\"114300\" simplePos=\"0\" relativeHeight=\"")
            .Append(Seq(251658240 + _drawingId)).Append("\" behindDoc=\"").Append(behind ? '1' : '0').Append("\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\"><wp:simplePos x=\"0\" y=\"0\"/>")
            .Append("<wp:positionH relativeFrom=\"").Append(hRel).Append("\"><wp:posOffset>").Append(spa.Left * EmuPerTwip).Append("</wp:posOffset></wp:positionH>")
            .Append("<wp:positionV relativeFrom=\"").Append(vRel).Append("\"><wp:posOffset>").Append(spa.Top * EmuPerTwip).Append("</wp:posOffset></wp:positionV>")
            .Append("<wp:extent cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>").Append(wrap)
            .Append(GraphicXml(rid, cx, cy, _drawingId++)).Append("</wp:anchor></w:drawing></w:r>");
    }
}
