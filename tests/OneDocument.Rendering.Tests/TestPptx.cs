using System.IO.Compression;
using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Builds small PPTX files in memory for the slide rendering tests and the PowerPoint differential. Sizes are in points. The theme's
// fonts are Liberation Sans so that text metrics come straight from the open font the tests ship with.
internal static class TestPptx
{
    internal const string Ns =
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    internal sealed class Deck
    {
        public double Width = 720, Height = 540;
        public string? MasterShapes;
        public string? MasterTextStyles { get; set; }
        public string? MasterBackground { get; set; }
        public string? LayoutShapes { get; set; }
        public string? LayoutBackground;
        public bool LayoutShowMasterShapes = true;
        public string LayoutType = "obj";
        public string? TableStyles { get; set; }
        public List<SlideSpec> Slides = [];
        public Dictionary<string, byte[]> Media = [];
        public bool NotesMaster;
        public int FirstSlideNumber = 1;
        public string? DefaultTextStyle { get; set; }
    }

    internal sealed class SlideSpec
    {
        public string Shapes = string.Empty;
        public string? Background;
        public string? Notes;
        public bool Hidden;
        public bool ShowMasterShapes = true;
        public List<(string Id, string Type, string Target, bool External)> Relationships = [];
    }

    internal static long E(double points) => (long)Math.Round(points * 12700);

    internal static string P(string text, string pPr = "", string rPr = "") =>
        "<a:p>" + (pPr.Length > 0 ? "<a:pPr" + pPr + "/>" : string.Empty) + R(text, rPr) + "</a:p>";

    // A paragraph with explicit properties element content: <a:pPr attrs>children</a:pPr>.
    internal static string PP(string pPrAttrs, string pPrChildren, params string[] runs) =>
        "<a:p><a:pPr" + pPrAttrs + ">" + pPrChildren + "</a:pPr>" + string.Concat(runs) + "</a:p>";

    internal static string R(string text, string rPr = "") =>
        "<a:r><a:rPr lang=\"en-US\"" + (rPr.StartsWith('<') ? ">" + rPr + "</a:rPr>" : rPr + "/>") + "<a:t>" + Escape(text) + "</a:t></a:r>";

    internal static string Escape(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    // A text box at a place; bodyPr is the whole <a:bodyPr .../> element, paragraphs the a:p elements.
    internal static string Box(int id, double x, double y, double w, double h, string paragraphs, string bodyPr = "<a:bodyPr wrap=\"square\"><a:noAutofit/></a:bodyPr>", string fill = "", string lstStyle = "", string rot = "", string geometry = "rect") =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"Box {id}\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm{rot}><a:off x=\"{E(x)}\" y=\"{E(y)}\"/><a:ext cx=\"{E(w)}\" cy=\"{E(h)}\"/></a:xfrm><a:prstGeom prst=\"{geometry}\"><a:avLst/></a:prstGeom>{(fill.Length > 0 ? fill : "<a:noFill/>")}</p:spPr><p:txBody>{bodyPr}<a:lstStyle>{lstStyle}</a:lstStyle>{paragraphs}</p:txBody></p:sp>";

    internal static string Placeholder(int id, string phAttrs, string paragraphs, string bodyPr = "<a:bodyPr/>", string spPr = "<p:spPr/>") =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"Placeholder {id}\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph {phAttrs}/></p:nvPr></p:nvSpPr>{spPr}<p:txBody>{bodyPr}<a:lstStyle/>{paragraphs}</p:txBody></p:sp>";

    internal static string Xfrm(double x, double y, double w, double h) =>
        $"<p:spPr><a:xfrm><a:off x=\"{E(x)}\" y=\"{E(y)}\"/><a:ext cx=\"{E(w)}\" cy=\"{E(h)}\"/></a:xfrm></p:spPr>";

    internal static string Rect(int id, double x, double y, double w, double h, string rgb) =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"Rect {id}\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"{E(x)}\" y=\"{E(y)}\"/><a:ext cx=\"{E(w)}\" cy=\"{E(h)}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"{rgb}\"/></a:solidFill><a:ln><a:noFill/></a:ln></p:spPr></p:sp>";

    internal static string Solid(string rgb) => $"<a:solidFill><a:srgbClr val=\"{rgb}\"/></a:solidFill>";

    private static string Tree(string shapes) =>
        "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" + shapes + "</p:spTree>";

    internal const string DefaultMasterShapes =
        "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Title\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"title\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"457200\" y=\"274638\"/><a:ext cx=\"8229600\" cy=\"1143000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" anchor=\"ctr\"><a:normAutofit/></a:bodyPr><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Title</a:t></a:r></a:p></p:txBody></p:sp>" +
        "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Body\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"457200\" y=\"1600200\"/><a:ext cx=\"8229600\" cy=\"4525963\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\"><a:normAutofit/></a:bodyPr><a:lstStyle/><a:p><a:pPr lvl=\"0\"/><a:r><a:rPr lang=\"en-US\"/><a:t>Body</a:t></a:r></a:p></p:txBody></p:sp>" +
        "<p:sp><p:nvSpPr><p:cNvPr id=\"4\" name=\"Date\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"dt\" sz=\"half\" idx=\"2\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"457200\" y=\"6356350\"/><a:ext cx=\"2133600\" cy=\"365125\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr anchor=\"ctr\"/><a:lstStyle><a:lvl1pPr algn=\"l\"><a:defRPr sz=\"1200\"/></a:lvl1pPr></a:lstStyle><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>" +
        "<p:sp><p:nvSpPr><p:cNvPr id=\"5\" name=\"Footer\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"ftr\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"3124200\" y=\"6356350\"/><a:ext cx=\"2895600\" cy=\"365125\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr anchor=\"ctr\"/><a:lstStyle><a:lvl1pPr algn=\"ctr\"><a:defRPr sz=\"1200\"/></a:lvl1pPr></a:lstStyle><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>" +
        "<p:sp><p:nvSpPr><p:cNvPr id=\"6\" name=\"Number\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldNum\" sz=\"quarter\" idx=\"4\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"6553200\" y=\"6356350\"/><a:ext cx=\"2133600\" cy=\"365125\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr anchor=\"ctr\"/><a:lstStyle><a:lvl1pPr algn=\"r\"><a:defRPr sz=\"1200\"/></a:lvl1pPr></a:lstStyle><a:p><a:fld id=\"{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>‹#›</a:t></a:fld></a:p></p:txBody></p:sp>";

    internal static readonly string DefaultTextStyles =
        "<p:txStyles><p:titleStyle><a:lvl1pPr algn=\"ctr\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\"><a:spcBef><a:spcPct val=\"0\"/></a:spcBef><a:buNone/><a:defRPr sz=\"4400\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mj-lt\"/><a:ea typeface=\"+mj-ea\"/><a:cs typeface=\"+mj-cs\"/></a:defRPr></a:lvl1pPr></p:titleStyle>" +
        "<p:bodyStyle>" + Levels(32, 28, 24, 20, 20) + "</p:bodyStyle>" +
        "<p:otherStyle><a:defPPr><a:defRPr lang=\"en-US\"/></a:defPPr>" + OtherLevels() + "</p:otherStyle></p:txStyles>";

    private static string Levels(params int[] sizes)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 9; i++)
        {
            int size = i < sizes.Length ? sizes[i] : sizes[^1];
            long marL = 342900 + (i * 457200);
            sb.Append($"<a:lvl{i + 1}pPr marL=\"{marL}\" indent=\"-342900\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\"><a:spcBef><a:spcPct val=\"20000\"/></a:spcBef><a:buFont typeface=\"Arial\" pitchFamily=\"34\" charset=\"0\"/><a:buChar char=\"•\"/><a:defRPr sz=\"{size * 100}\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl{i + 1}pPr>");
        }

        return sb.ToString();
    }

    private static string OtherLevels()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 9; i++)
        {
            sb.Append($"<a:lvl{i + 1}pPr marL=\"{i * 457200}\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\"><a:defRPr sz=\"1800\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl{i + 1}pPr>");
        }

        return sb.ToString();
    }

    internal static string PresentationDefaults() =>
        "<p:defaultTextStyle><a:defPPr><a:defRPr lang=\"en-US\"/></a:defPPr>" + OtherLevels() + "</p:defaultTextStyle>";

    private static string Theme() => DrawingSamples.ThemeXml.Replace("Calibri", "Liberation Sans", StringComparison.Ordinal);

    internal static byte[] Build(Deck d)
    {
        var parts = new List<(string Name, string Xml)>();
        var types = new StringBuilder();
        types.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>");
        void Override(string part, string type) => types.Append($"<Override PartName=\"{part}\" ContentType=\"application/vnd.openxmlformats-officedocument.{type}+xml\"/>");
        Override("/ppt/presentation.xml", "presentationml.presentation.main");
        Override("/ppt/slideMasters/slideMaster1.xml", "presentationml.slideMaster");
        Override("/ppt/slideLayouts/slideLayout1.xml", "presentationml.slideLayout");
        Override("/ppt/theme/theme1.xml", "theme");
        Override("/ppt/tableStyles.xml", "presentationml.tableStyles");

        string slideIds = string.Concat(d.Slides.Select((_, i) => $"<p:sldId id=\"{256 + i}\" r:id=\"rId{10 + i}\"/>"));
        string presRels = $"<Relationship Id=\"rId1\" Type=\"{Rel}slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/><Relationship Id=\"rId2\" Type=\"{Rel}tableStyles\" Target=\"tableStyles.xml\"/>" +
            string.Concat(d.Slides.Select((_, i) => $"<Relationship Id=\"rId{10 + i}\" Type=\"{Rel}slide\" Target=\"slides/slide{i + 1}.xml\"/>")) +
            (d.NotesMaster ? $"<Relationship Id=\"rId3\" Type=\"{Rel}notesMaster\" Target=\"notesMasters/notesMaster1.xml\"/>" : string.Empty);
        parts.Add(("ppt/presentation.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:presentation {Ns} firstSlideNum=\"{d.FirstSlideNumber}\"><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>" +
            (d.NotesMaster ? "<p:notesMasterIdLst><p:notesMasterId r:id=\"rId3\"/></p:notesMasterIdLst>" : string.Empty) +
            $"<p:sldIdLst>{slideIds}</p:sldIdLst><p:sldSz cx=\"{E(d.Width)}\" cy=\"{E(d.Height)}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>{d.DefaultTextStyle ?? PresentationDefaults()}</p:presentation>"));
        parts.Add(("ppt/_rels/presentation.xml.rels", Rels(presRels)));
        parts.Add(("ppt/tableStyles.xml", d.TableStyles ?? "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><a:tblStyleLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" def=\"{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}\"/>"));
        parts.Add(("ppt/theme/theme1.xml", Theme()));
        string masterBg = d.MasterBackground ?? "<p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg>";
        parts.Add(("ppt/slideMasters/slideMaster1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldMaster {Ns}><p:cSld>{masterBg}{Tree(d.MasterShapes ?? DefaultMasterShapes)}</p:cSld><p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/><p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst>{d.MasterTextStyles ?? DefaultTextStyles}</p:sldMaster>"));
        parts.Add(("ppt/slideMasters/_rels/slideMaster1.xml.rels", Rels($"<Relationship Id=\"rId1\" Type=\"{Rel}slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/><Relationship Id=\"rId2\" Type=\"{Rel}theme\" Target=\"../theme/theme1.xml\"/>")));
        string layoutShapes = d.LayoutShapes ??
            "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Title\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"title\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Title</a:t></a:r></a:p></p:txBody></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Content\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:pPr lvl=\"0\"/><a:r><a:rPr lang=\"en-US\"/><a:t>Content</a:t></a:r></a:p></p:txBody></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"4\" name=\"Date\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"dt\" sz=\"half\" idx=\"10\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"5\" name=\"Footer\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"ftr\" sz=\"quarter\" idx=\"11\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"6\" name=\"Number\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldNum\" sz=\"quarter\" idx=\"12\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:fld id=\"{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>‹#›</a:t></a:fld></a:p></p:txBody></p:sp>";
        parts.Add(("ppt/slideLayouts/slideLayout1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldLayout {Ns} type=\"{d.LayoutType}\" showMasterSp=\"{(d.LayoutShowMasterShapes ? "1" : "0")}\"><p:cSld name=\"Title and Content\">{d.LayoutBackground}{Tree(layoutShapes)}</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>"));
        parts.Add(("ppt/slideLayouts/_rels/slideLayout1.xml.rels", Rels($"<Relationship Id=\"rId1\" Type=\"{Rel}slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/>")));
        if (d.NotesMaster)
        {
            Override("/ppt/notesMasters/notesMaster1.xml", "presentationml.notesMaster");
            parts.Add(("ppt/notesMasters/notesMaster1.xml", NotesMasterXml()));
            parts.Add(("ppt/notesMasters/_rels/notesMaster1.xml.rels", Rels($"<Relationship Id=\"rId1\" Type=\"{Rel}theme\" Target=\"../theme/theme2.xml\"/>")));
            Override("/ppt/theme/theme2.xml", "theme");
            parts.Add(("ppt/theme/theme2.xml", Theme()));
        }

        for (int i = 0; i < d.Slides.Count; i++)
        {
            SlideSpec s = d.Slides[i];
            Override($"/ppt/slides/slide{i + 1}.xml", "presentationml.slide");
            parts.Add(($"ppt/slides/slide{i + 1}.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sld {Ns}{(s.Hidden ? " show=\"0\"" : string.Empty)}{(s.ShowMasterShapes ? string.Empty : " showMasterSp=\"0\"")}><p:cSld>{s.Background}{Tree(s.Shapes)}</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>"));
            string rels = $"<Relationship Id=\"rId1\" Type=\"{Rel}slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>";
            if (s.Notes is not null)
            {
                rels += $"<Relationship Id=\"rId2\" Type=\"{Rel}notesSlide\" Target=\"../notesSlides/notesSlide{i + 1}.xml\"/>";
                Override($"/ppt/notesSlides/notesSlide{i + 1}.xml", "presentationml.notesSlide");
                parts.Add(($"ppt/notesSlides/notesSlide{i + 1}.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:notes {Ns}><p:cSld>{Tree("<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>" + Placeholder(3, "type=\"body\" idx=\"1\"", P(s.Notes)))}</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>"));
                parts.Add(($"ppt/notesSlides/_rels/notesSlide{i + 1}.xml.rels", Rels($"<Relationship Id=\"rId1\" Type=\"{Rel}notesMaster\" Target=\"../notesMasters/notesMaster1.xml\"/><Relationship Id=\"rId2\" Type=\"{Rel}slide\" Target=\"../slides/slide{i + 1}.xml\"/>")));
            }

            foreach ((string id, string type, string target, bool external) in s.Relationships)
            {
                rels += $"<Relationship Id=\"{id}\" Type=\"{(type.StartsWith("http", StringComparison.Ordinal) ? type : Rel + type)}\" Target=\"{target}\"{(external ? " TargetMode=\"External\"" : string.Empty)}/>";
            }

            parts.Add(($"ppt/slides/_rels/slide{i + 1}.xml.rels", Rels(rels)));
        }

        parts.Insert(0, ("[Content_Types].xml", types.Append("</Types>").ToString()));
        parts.Insert(1, ("_rels/.rels", Rels($"<Relationship Id=\"rId1\" Type=\"{Rel}officeDocument\" Target=\"ppt/presentation.xml\"/>")));
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string xml) in parts)
            {
                ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
                e.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(xml);
            }

            foreach ((string name, byte[] bytes) in d.Media)
            {
                ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
                e.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using Stream s = e.Open();
                s.Write(bytes);
            }
        }

        return ms.ToArray();
    }

    private static string Rels(string inner) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + inner + "</Relationships>";

    private static string NotesMasterXml() =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:notesMaster {Ns}><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg>" +
        Tree("<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"685800\" y=\"1143000\"/><a:ext cx=\"5486400\" cy=\"3086100\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/><a:ln w=\"12700\"><a:solidFill><a:prstClr val=\"black\"/></a:solidFill></a:ln></p:spPr></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"685800\" y=\"4400550\"/><a:ext cx=\"5486400\" cy=\"3600450\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\"/><a:lstStyle/><a:p><a:pPr lvl=\"0\"/><a:r><a:rPr lang=\"en-US\"/><a:t>Notes</a:t></a:r></a:p></p:txBody></p:sp>") +
        "</p:cSld><p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
        "<p:notesStyle><a:lvl1pPr marL=\"0\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\"><a:defRPr sz=\"1200\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/></a:defRPr></a:lvl1pPr></p:notesStyle></p:notesMaster>";
}
