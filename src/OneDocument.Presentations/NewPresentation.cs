using System.Text;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>The parts of a new presentation: a master, three layouts and a theme.</summary>
internal static class NewPresentation
{
    private const string Declarations =
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Pml = "application/vnd.openxmlformats-officedocument.presentationml.";
    private const string Prolog = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    public static void Fill(OpcPackage package)
    {
        package.SetRelationships(null, [new Relationship("rId1", RelBase + "officeDocument", "ppt/presentation.xml")]);
        package.AddPart("/ppt/presentation.xml", Pml + "presentation.main+xml", Bytes(PresentationXml));
        package.SetRelationships("/ppt/presentation.xml",
        [
            new Relationship("rId1", RelBase + "slideMaster", "slideMasters/slideMaster1.xml"),
            new Relationship("rId2", RelBase + "theme", "theme/theme1.xml"),
        ]);
        package.AddPart("/ppt/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml", Bytes(ThemeXml));
        package.AddPart("/ppt/slideMasters/slideMaster1.xml", Pml + "slideMaster+xml", Bytes(MasterXml));
        package.SetRelationships("/ppt/slideMasters/slideMaster1.xml",
        [
            new Relationship("rId1", RelBase + "slideLayout", "../slideLayouts/slideLayout1.xml"),
            new Relationship("rId2", RelBase + "slideLayout", "../slideLayouts/slideLayout2.xml"),
            new Relationship("rId3", RelBase + "slideLayout", "../slideLayouts/slideLayout3.xml"),
            new Relationship("rId4", RelBase + "theme", "../theme/theme1.xml"),
        ]);
        string[] layouts = [TitleLayout, ContentLayout, BlankLayout];
        for (int i = 0; i < layouts.Length; i++)
        {
            string name = "/ppt/slideLayouts/slideLayout" + (i + 1) + ".xml";
            package.AddPart(name, Pml + "slideLayout+xml", Bytes(layouts[i]));
            package.SetRelationships(name, [new Relationship("rId1", RelBase + "slideMaster", "../slideMasters/slideMaster1.xml")]);
        }
    }

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(Prolog + xml);

    // ECMA-376 Part 1 §19.2.1.26 (sldSz) and §19.2.1.20 (notesSz): EMU; 12,192,000 x 6,858,000 is 16:9 at 13.333 x 7.5 in.
    private const string PresentationXml =
        "<p:presentation " + Declarations + " saveSubsetFonts=\"1\">" +
        "<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>" +
        "<p:sldSz cx=\"12192000\" cy=\"6858000\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>" +
        "<p:defaultTextStyle><a:defPPr><a:defRPr lang=\"en-US\"/></a:defPPr>" +
        "<a:lvl1pPr marL=\"0\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\"><a:defRPr sz=\"1800\" kern=\"1200\">" +
        "<a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl1pPr>" +
        "</p:defaultTextStyle></p:presentation>";

    private const string ThemeXml =
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office Theme\"><a:themeElements>" +
        "<a:clrScheme name=\"Office\"><a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"44546A\"/></a:dk2><a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2><a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1>" +
        "<a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2><a:accent3><a:srgbClr val=\"A5A5A5\"/></a:accent3><a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"5B9BD5\"/></a:accent5><a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6><a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink>" +
        "<a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink></a:clrScheme>" +
        "<a:fontScheme name=\"Office\"><a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
        "<a:fmtScheme name=\"Office\"><a:fillStyleLst>" + Fill3 + "</a:fillStyleLst><a:lnStyleLst>" + Line3 + "</a:lnStyleLst>" +
        "<a:effectStyleLst>" + Effect3 + "</a:effectStyleLst><a:bgFillStyleLst>" + Fill3 + "</a:bgFillStyleLst></a:fmtScheme>" +
        "</a:themeElements></a:theme>";

    private const string Solid = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>";
    private const string Fill3 = Solid + Solid + Solid;
    private const string Line3 = "<a:ln w=\"6350\">" + Solid + "</a:ln><a:ln w=\"12700\">" + Solid + "</a:ln><a:ln w=\"19050\">" + Solid + "</a:ln>";
    private const string Effect3 = "<a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle>";

    private const string GroupHead =
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/>" +
        "<a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    private static string Xfrm(long x, long y, long cx, long cy) =>
        "<p:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/><a:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>";

    // A placeholder shape. 'body' is the text body markup (null for none), 'spPr' the shape properties.
    private static string Placeholder(int id, string name, string ph, string spPr, string? body) =>
        "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"" + name + "\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph " + ph + "/></p:nvPr></p:nvSpPr>" +
        spPr + (body is null ? string.Empty : "<p:txBody>" + body + "</p:txBody>") + "</p:sp>";

    private static string Prompt(string text) =>
        "<a:p><a:r><a:rPr lang=\"en-US\"/><a:t>" + text + "</a:t></a:r></a:p>";

    private const string PlainBody = "<a:bodyPr/><a:lstStyle/>";

    private static string Footers(int firstId, bool withGeometry)
    {
        string slideNumber =
            "<a:p><a:fld id=\"{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>‹#›</a:t></a:fld><a:endParaRPr lang=\"en-US\"/></a:p>";
        string foot(string anchor, string algn) =>
            "<a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\" anchor=\"ctr\"/><a:lstStyle><a:lvl1pPr algn=\"" + algn +
            "\"><a:defRPr sz=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"><a:tint val=\"75000\"/></a:schemeClr></a:solidFill></a:defRPr></a:lvl1pPr></a:lstStyle>";
        return
            Placeholder(firstId, "Date Placeholder " + (firstId - 1), "type=\"dt\" sz=\"half\" idx=\"" + (withGeometry ? 2 : 10) + "\"",
                withGeometry ? Xfrm(838200, 6356350, 2743200, 365125) : "<p:spPr/>",
                withGeometry ? foot("ctr", "l") + "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>" : null) +
            Placeholder(firstId + 1, "Footer Placeholder " + firstId, "type=\"ftr\" sz=\"quarter\" idx=\"" + (withGeometry ? 3 : 11) + "\"",
                withGeometry ? Xfrm(4038600, 6356350, 4114800, 365125) : "<p:spPr/>",
                withGeometry ? foot("ctr", "ctr") + "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>" : null) +
            Placeholder(firstId + 2, "Slide Number Placeholder " + (firstId + 1), "type=\"sldNum\" sz=\"quarter\" idx=\"" + (withGeometry ? 4 : 12) + "\"",
                withGeometry ? Xfrm(8610600, 6356350, 2743200, 365125) : "<p:spPr/>",
                withGeometry ? foot("ctr", "r") + slideNumber : null);
    }

    private static string Level(int n, long marL, int sz) =>
        "<a:lvl" + n + "pPr marL=\"" + marL + "\" indent=\"-228600\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\">" +
        "<a:lnSpc><a:spcPct val=\"90000\"/></a:lnSpc><a:spcBef><a:spcPts val=\"1000\"/></a:spcBef><a:buFont typeface=\"Arial\" panose=\"020B0604020202020204\"/>" +
        "<a:buChar char=\"•\"/><a:defRPr sz=\"" + sz + "\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/>" +
        "<a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl" + n + "pPr>";

    private static readonly string MasterXml =
        "<p:sldMaster " + Declarations + "><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>" + GroupHead +
        Placeholder(2, "Title Placeholder 1", "type=\"title\"", Xfrm(838200, 365125, 10515600, 1325563),
            "<a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\" anchor=\"ctr\"><a:normAutofit/></a:bodyPr><a:lstStyle/>" +
            Prompt("Click to edit Master title style")) +
        Placeholder(3, "Text Placeholder 2", "type=\"body\" idx=\"1\"", Xfrm(838200, 1825625, 10515600, 4351338),
            "<a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\"><a:normAutofit/></a:bodyPr><a:lstStyle/>" +
            Prompt("Click to edit Master text styles")) +
        Footers(4, withGeometry: true) +
        "</p:spTree></p:cSld>" +
        "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
        "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/><p:sldLayoutId id=\"2147483650\" r:id=\"rId2\"/><p:sldLayoutId id=\"2147483651\" r:id=\"rId3\"/></p:sldLayoutIdLst>" +
        "<p:txStyles><p:titleStyle><a:lvl1pPr algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\"><a:lnSpc><a:spcPct val=\"90000\"/></a:lnSpc>" +
        "<a:spcBef><a:spcPct val=\"0\"/></a:spcBef><a:buNone/><a:defRPr sz=\"4400\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mj-lt\"/>" +
        "<a:ea typeface=\"+mj-ea\"/><a:cs typeface=\"+mj-cs\"/></a:defRPr></a:lvl1pPr></p:titleStyle>" +
        "<p:bodyStyle>" + Level(1, 228600, 2800) + Level(2, 685800, 2400) + Level(3, 1143000, 2000) + Level(4, 1600200, 1800) + Level(5, 2057400, 1800) + "</p:bodyStyle>" +
        "<p:otherStyle><a:defPPr><a:defRPr lang=\"en-US\"/></a:defPPr><a:lvl1pPr marL=\"0\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\">" +
        "<a:defRPr sz=\"1800\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl1pPr></p:otherStyle>" +
        "</p:txStyles></p:sldMaster>";

    private static string LayoutXml(string type, string name, string shapes) =>
        "<p:sldLayout " + Declarations + " type=\"" + type + "\" preserve=\"1\"><p:cSld name=\"" + name + "\"><p:spTree>" + GroupHead + shapes +
        "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";

    private static readonly string TitleLayout = LayoutXml("title", "Title Slide",
        Placeholder(2, "Title 1", "type=\"ctrTitle\"", Xfrm(1524000, 1122363, 9144000, 2387600),
            "<a:bodyPr anchor=\"b\"/><a:lstStyle><a:lvl1pPr algn=\"ctr\"><a:defRPr sz=\"6000\"/></a:lvl1pPr></a:lstStyle>" + Prompt("Click to edit Master title style")) +
        Placeholder(3, "Subtitle 2", "type=\"subTitle\" idx=\"1\"", Xfrm(1524000, 3602038, 9144000, 1655762),
            "<a:bodyPr/><a:lstStyle><a:lvl1pPr marL=\"0\" indent=\"0\" algn=\"ctr\"><a:buNone/><a:defRPr sz=\"2400\"/></a:lvl1pPr></a:lstStyle>" + Prompt("Click to edit Master subtitle style")) +
        Footers(4, withGeometry: false));

    private static readonly string ContentLayout = LayoutXml("obj", "Title and Content",
        Placeholder(2, "Title 1", "type=\"title\"", "<p:spPr/>", PlainBody + Prompt("Click to edit Master title style")) +
        Placeholder(3, "Content Placeholder 2", "idx=\"1\"", "<p:spPr/>", PlainBody + Prompt("Click to edit Master text styles")) +
        Footers(4, withGeometry: false));

    private static readonly string BlankLayout = LayoutXml("blank", "Blank", Footers(2, withGeometry: false));
}
