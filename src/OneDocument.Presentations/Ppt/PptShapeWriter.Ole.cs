using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using static RecursiveAI.OneDocument.Ppt.Binary.PptXml;

namespace RecursiveAI.OneDocument.Ppt.Binary;

internal sealed partial class PptShapeWriter
{
    private const string OleGraphicUri = "http://schemas.openxmlformats.org/presentationml/2006/ole";

    // A picture shape whose client data names an external object is the object's picture: it is written as the object (ECMA-376 Part 1
    // 19.3.2.4 oleObj), with the storage as a related part and the preview as the object's own picture. Returns false when the shape stays an
    // ordinary picture.
    private bool TryWriteOle(ArtShape s, Placeholder? ph)
    {
        // Layouts are built from a slide's shapes too; the object belongs to the slide only.
        if (_kind != PageKind.Slide || s.ClientData?.Find(PptRecordType.ExternalObjectRefAtom) is not { } reference || reference.Payload.Length < 4 || ph is not null)
        {
            return false;
        }

        uint exObjId = unchecked((uint)reference.Int32At(0));
        if (_conv.OleObject(exObjId) is not { } obj || obj.Kind == 2)
        {
            return false; // an ActiveX control, or an object the external object list does not describe
        }

        OleStorageImage? storage = _conv.OleStorage(obj);
        if (storage is null)
        {
            return false;
        }

        int index = s.Properties.GetInt(ArtPropertyId.Pib, 0);
        string imageRel = _page.AddImage(index);
        ArtRect? bounds = Bounds(s);
        if (imageRel.Length == 0 || bounds is null)
        {
            return false;
        }

        OleObjectInfo info = storage.Describe();
        string objectRel;
        string kind;
        if (info.Kind == OleObjectKind.Linked || obj.Kind == 1)
        {
            if (info.LinkTarget is null)
            {
                return false;
            }

            objectRel = _page.AddRel(OleEmbedding.ObjectRelationshipType, OleEmbedding.FileUri(info.LinkTarget), RelationshipTargetMode.External);
            kind = "<p:link updateAutomatic=\"" + (info.Kind == OleObjectKind.Linked ? "1" : "0") + "\"/>";
        }
        else
        {
            byte[] bin;
            try
            {
                bin = storage.ToCompoundFile(_read.Limits);
            }
            catch (OfficeException ex)
            {
                _conv.P.File.Context.Defects.Report(ex.Defect with { Severity = DefectSeverity.Dropped });
                return false;
            }

            objectRel = _page.AddRel(OleEmbedding.ObjectRelationshipType, _conv.AddEmbeddedObject(bin));
            kind = "<p:embed/>";
        }

        string progId = obj.ProgId ?? info.ProgId ?? string.Empty;
        string head = Id(s, out string name);
        long cx = Emu(Math.Abs(bounds.Value.Width));
        long cy = Emu(Math.Abs(bounds.Value.Height));
        _sb.Append("<p:graphicFrame><p:nvGraphicFramePr>").Append(head).Append("/><p:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>")
            .Append(Xfrm(s, bounds, group: false, "p:xfrm")).Append("<a:graphic><a:graphicData uri=\"").Append(OleGraphicUri).Append("\"><p:oleObj name=\"").Append(Escape(name)).Append("\" r:id=\"").Append(objectRel)
            .Append("\" imgW=\"").Append(Num(cx)).Append("\" imgH=\"").Append(Num(cy)).Append('"');
        if (progId.Length > 0)
        {
            _sb.Append(" progId=\"").Append(Escape(progId)).Append('"');
        }

        _sb.Append('>').Append(kind).Append("<p:pic><p:nvPicPr><p:cNvPr id=\"0\" name=\"\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\" noChangeArrowheads=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed=\"")
            .Append(imageRel).Append("\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr bwMode=\"auto\"><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(Num(cx)).Append("\" cy=\"").Append(Num(cy))
            .Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic></p:oleObj></a:graphicData></a:graphic></p:graphicFrame>");
        _conv.OleCarried++;
        return true;
    }
}
