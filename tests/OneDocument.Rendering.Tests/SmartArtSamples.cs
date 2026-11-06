using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Rendering.SmartArt;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Generated SmartArt diagrams, one family at a time, written from scratch: the data model of a text tree, a layout definition per
// algorithm family, a colour transform and a quick style. The layout definitions use the standard's own algorithms and constraint
// language but are not copies of anything Office ships. The same generator writes the differential inputs under testdata/smartart.
internal static class SmartArtSamples
{
    internal const string Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    internal const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    internal const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Ns = $"xmlns:dgm=\"{Dgm}\" xmlns:a=\"{A}\" xmlns:r=\"{R}\"";

    private static readonly Lazy<RecursiveAI.OneDocument.Layout.FontLibrary> Fonts = new(RecursiveAI.OneDocument.Layout.FontLibrary.CreateDefault);

    internal sealed class Node(string text, params Node[] children)
    {
        public string Text { get; } = text;

        public Node[] Children { get; } = children;
    }

    internal static Node[] Flat(params string[] texts) => [.. texts.Select(t => new Node(t))];

    // ---------------------------------------------------------------- data model

    internal static string DataModel(IReadOnlyList<Node> roots, string? drawingRelId = null, string? docVars = null)
    {
        var pts = new StringBuilder();
        var cxns = new StringBuilder();
        int id = 0;
        const string Empty = "<dgm:spPr/><dgm:t><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></dgm:t>";
        pts.Append("<dgm:pt modelId=\"0\" type=\"doc\"><dgm:prSet loTypeId=\"urn:onedocument.test/layout\" loCatId=\"list\" qsTypeId=\"urn:onedocument.test/quickstyle/simple1\" qsCatId=\"simple\" csTypeId=\"urn:onedocument.test/colors/accent1_2\" csCatId=\"accent1\" phldr=\"0\">").Append(docVars is null ? string.Empty : "<dgm:presLayoutVars>" + docVars + "</dgm:presLayoutVars>").Append("</dgm:prSet>").Append(Empty).Append("</dgm:pt>");

        void Add(Node n, string parent, int order)
        {
            int me = ++id;
            int cx = ++id, par = ++id, sib = ++id;
            pts.Append("<dgm:pt modelId=\"").Append(me).Append("\"><dgm:prSet phldrT=\"[Text]\"/><dgm:spPr/><dgm:t><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>")
                .Append(System.Security.SecurityElement.Escape(n.Text)).Append("</a:t></a:r></a:p></dgm:t></dgm:pt>");
            pts.Append("<dgm:pt modelId=\"").Append(par).Append("\" type=\"parTrans\" cxnId=\"").Append(cx).Append("\"><dgm:prSet/>").Append(Empty).Append("</dgm:pt>");
            pts.Append("<dgm:pt modelId=\"").Append(sib).Append("\" type=\"sibTrans\" cxnId=\"").Append(cx).Append("\"><dgm:prSet/>").Append(Empty).Append("</dgm:pt>");
            cxns.Append("<dgm:cxn modelId=\"").Append(cx).Append("\" srcId=\"").Append(parent).Append("\" destId=\"").Append(me).Append("\" srcOrd=\"").Append(order).Append("\" destOrd=\"0\" parTransId=\"").Append(par).Append("\" sibTransId=\"").Append(sib).Append("\"/>");
            for (int i = 0; i < n.Children.Length; i++)
            {
                Add(n.Children[i], me.ToString(System.Globalization.CultureInfo.InvariantCulture), i);
            }
        }

        for (int i = 0; i < roots.Count; i++)
        {
            Add(roots[i], "0", i);
        }

        string ext = drawingRelId is null ? string.Empty
            : $"<dgm:extLst><a:ext uri=\"http://schemas.microsoft.com/office/drawing/2008/diagram\"><dsp:dataModelExt xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" relId=\"{drawingRelId}\" minVer=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\"/></a:ext></dgm:extLst>";
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><dgm:dataModel {Ns}><dgm:ptLst>{pts}</dgm:ptLst><dgm:cxnLst>{cxns}</dgm:cxnLst><dgm:bg/><dgm:whole/>{ext}</dgm:dataModel>";
    }

    // ---------------------------------------------------------------- layout definitions

    private static string LayoutDef(string id, string category, string root) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><dgm:layoutDef {Ns} uniqueId=\"urn:onedocument.test/layout/{id}\"><dgm:title val=\"{id}\"/><dgm:desc val=\"\"/><dgm:catLst><dgm:cat type=\"{category}\" pri=\"400\"/></dgm:catLst>{root}</dgm:layoutDef>";

    private const string TextNode = """
        <dgm:alg type="tx"/>
        <dgm:shape type="{0}" r:blip=""><dgm:adjLst/></dgm:shape>
        <dgm:presOf axis="{1}" ptType="node"/>
        <dgm:constrLst>
          <dgm:constr type="lMarg" refType="primFontSz" fact="0.3"/>
          <dgm:constr type="rMarg" refType="primFontSz" fact="0.3"/>
          <dgm:constr type="tMarg" refType="primFontSz" fact="0.3"/>
          <dgm:constr type="bMarg" refType="primFontSz" fact="0.3"/>
        </dgm:constrLst>
        <dgm:ruleLst><dgm:rule type="primFontSz" val="5" fact="NaN" max="NaN"/></dgm:ruleLst>
        """;

    private static string Node1(string shape, string name = "node", string varLst = "<dgm:varLst><dgm:bulletEnabled val=\"1\"/></dgm:varLst>", string presAxis = "desOrSelf") =>
        $"<dgm:layoutNode name=\"{name}\">{varLst}{TextNode.Replace("{0}", shape, StringComparison.Ordinal).Replace("{1}", presAxis, StringComparison.Ordinal)}</dgm:layoutNode>";

    private static string Spacer(string name = "sibTrans") =>
        $"<dgm:layoutNode name=\"{name}\"><dgm:alg type=\"sp\"/><dgm:shape r:blip=\"\"><dgm:adjLst/></dgm:shape><dgm:presOf/><dgm:constrLst/><dgm:ruleLst/></dgm:layoutNode>";

    private static string Arrow(string name = "sibTrans") =>
        $"<dgm:layoutNode name=\"{name}\"><dgm:alg type=\"conn\"><dgm:param type=\"begPts\" val=\"auto\"/><dgm:param type=\"endPts\" val=\"auto\"/></dgm:alg><dgm:shape type=\"conn\" r:blip=\"\"><dgm:adjLst/></dgm:shape><dgm:presOf axis=\"self\"/><dgm:constrLst/><dgm:ruleLst/></dgm:layoutNode>";

    private const string Head = "<dgm:shape r:blip=\"\"><dgm:adjLst/></dgm:shape><dgm:presOf/>";

    // Block list: a snake of boxes, with the standard's `dir` choice for right-to-left.
    internal static string List() => LayoutDef("list", "list", $$"""
        <dgm:layoutNode name="diagram">
          <dgm:varLst><dgm:dir/><dgm:resizeHandles val="exact"/></dgm:varLst>
          <dgm:choose name="c0">
            <dgm:if name="c1" func="var" arg="dir" op="equ" val="norm">
              <dgm:alg type="snake"><dgm:param type="grDir" val="tL"/><dgm:param type="flowDir" val="row"/><dgm:param type="contDir" val="sameDir"/><dgm:param type="off" val="ctr"/></dgm:alg>
            </dgm:if>
            <dgm:else name="c2">
              <dgm:alg type="snake"><dgm:param type="grDir" val="tR"/><dgm:param type="flowDir" val="row"/><dgm:param type="contDir" val="sameDir"/><dgm:param type="off" val="ctr"/></dgm:alg>
            </dgm:else>
          </dgm:choose>
          {{Head}}
          <dgm:constrLst>
            <dgm:constr type="w" for="ch" forName="node" refType="w"/>
            <dgm:constr type="h" for="ch" forName="node" refType="w" refFor="ch" refForName="node" fact="0.6"/>
            <dgm:constr type="w" for="ch" forName="sibTrans" refType="w" refFor="ch" refForName="node" fact="0.1"/>
            <dgm:constr type="sp" refType="w" refFor="ch" refForName="sibTrans"/>
            <dgm:constr type="primFontSz" for="ch" forName="node" op="equ" val="65"/>
          </dgm:constrLst>
          <dgm:ruleLst/>
          <dgm:forEach name="n" axis="ch" ptType="node">
            {{Node1("rect")}}
            <dgm:forEach name="s" axis="followSib" ptType="sibTrans" cnt="1">{{Spacer()}}</dgm:forEach>
          </dgm:forEach>
        </dgm:layoutNode>
        """);

    // Basic process: boxes in a row joined by block arrows.
    internal static string Process() => LayoutDef("process", "process", $$"""
        <dgm:layoutNode name="diagram">
          <dgm:alg type="lin"><dgm:param type="linDir" val="fromL"/><dgm:param type="nodeVertAlign" val="mid"/></dgm:alg>
          {{Head}}
          <dgm:constrLst>
            <dgm:constr type="w" for="ch" forName="node" refType="w"/>
            <dgm:constr type="h" for="ch" forName="node" refType="w" refFor="ch" refForName="node" fact="0.6"/>
            <dgm:constr type="w" for="ch" forName="sibTrans" refType="w" refFor="ch" refForName="node" fact="0.4"/>
            <dgm:constr type="h" for="ch" forName="sibTrans" refType="w" refFor="ch" refForName="sibTrans" fact="0.6"/>
            <dgm:constr type="primFontSz" for="ch" forName="node" op="equ" val="65"/>
          </dgm:constrLst>
          <dgm:ruleLst/>
          <dgm:forEach name="n" axis="ch" ptType="node">
            {{Node1("roundRect")}}
            <dgm:forEach name="s" axis="followSib" ptType="sibTrans" cnt="1">{{Arrow()}}</dgm:forEach>
          </dgm:forEach>
        </dgm:layoutNode>
        """);

    // Basic cycle: nodes on a circle, arrows between them (the last arrow closes the ring).
    internal static string Cycle() => LayoutDef("cycle", "cycle", $$"""
        <dgm:layoutNode name="diagram">
          <dgm:alg type="cycle"><dgm:param type="stAng" val="0"/><dgm:param type="spanAng" val="360"/></dgm:alg>
          {{Head}}
          <dgm:constrLst>
            <dgm:constr type="w" for="ch" forName="node" refType="w" fact="0.3"/>
            <dgm:constr type="h" for="ch" forName="node" refType="w" refFor="ch" refForName="node" fact="0.6"/>
            <dgm:constr type="primFontSz" for="ch" forName="node" op="equ" val="65"/>
          </dgm:constrLst>
          <dgm:ruleLst/>
          <dgm:forEach name="n" axis="ch" ptType="node">
            {{Node1("ellipse")}}
            <dgm:forEach name="s" axis="followSib" ptType="sibTrans" cnt="1" hideLastTrans="0">{{Arrow()}}</dgm:forEach>
          </dgm:forEach>
        </dgm:layoutNode>
        """);

    // Hierarchy: each node above its children, joined by elbow connectors. Recursion goes through forEach/@ref.
    internal static string Hierarchy() => LayoutDef("hierarchy", "hierarchy", $$"""
        <dgm:layoutNode name="diagram">
          <dgm:varLst><dgm:orgChart val="1"/><dgm:dir/><dgm:resizeHandles val="exact"/></dgm:varLst>
          <dgm:alg type="hierChild"/>
          {{Head}}
          <dgm:constrLst>
            <dgm:constr type="w" for="des" forName="node" refType="w" fact="0.2"/>
            <dgm:constr type="h" for="des" forName="node" refType="w" refFor="des" refForName="node" fact="0.45"/>
            <dgm:constr type="primFontSz" for="des" forName="node" op="equ" val="65"/>
            <dgm:constr type="sp" for="des" forName="hierRoot" refType="h" refFor="des" refForName="node" fact="0.7"/>
            <dgm:constr type="sibSp" for="des" forName="hierChild" refType="w" refFor="des" refForName="node" fact="0.15"/>
          </dgm:constrLst>
          <dgm:ruleLst/>
          <dgm:forEach name="roots" axis="ch" ptType="node">
            <dgm:layoutNode name="hierRoot">
              <dgm:alg type="hierRoot"/>
              {{Head}}
              <dgm:constrLst/>
              <dgm:ruleLst/>
              {{Node1("roundRect", varLst: "", presAxis: "self")}}
              <dgm:layoutNode name="hierChild">
                <dgm:alg type="hierChild"/>
                {{Head}}
                <dgm:constrLst/>
                <dgm:ruleLst/>
                <dgm:forEach name="rec" ref="roots"/>
              </dgm:layoutNode>
              <dgm:forEach name="links" axis="ch" ptType="parTrans">
                <dgm:layoutNode name="link">
                  <dgm:alg type="conn">
                    <dgm:param type="dim" val="1D"/><dgm:param type="connRout" val="bend"/><dgm:param type="begSty" val="noArr"/><dgm:param type="endSty" val="noArr"/>
                    <dgm:param type="srcNode" val="node"/><dgm:param type="dstNode" val="node"/><dgm:param type="begPts" val="bCtr"/><dgm:param type="endPts" val="tCtr"/>
                  </dgm:alg>
                  <dgm:shape type="conn" r:blip=""><dgm:adjLst/></dgm:shape>
                  <dgm:presOf axis="self"/>
                  <dgm:constrLst/>
                  <dgm:ruleLst/>
                </dgm:layoutNode>
              </dgm:forEach>
            </dgm:layoutNode>
          </dgm:forEach>
        </dgm:layoutNode>
        """);

    // Basic pyramid: the first node is the apex, each next node a wider level below it.
    internal static string Pyramid() => LayoutDef("pyramid", "pyramid", $$"""
        <dgm:layoutNode name="diagram">
          <dgm:alg type="pyra"><dgm:param type="linDir" val="fromT"/><dgm:param type="txDir" val="fromT"/></dgm:alg>
          {{Head}}
          <dgm:constrLst><dgm:constr type="primFontSz" for="ch" forName="level" val="40"/></dgm:constrLst>
          <dgm:ruleLst/>
          <dgm:forEach name="n" axis="ch" ptType="node">{{Node1("trapezoid", "level", "")}}</dgm:forEach>
        </dgm:layoutNode>
        """);

    // Relationship by composite: three overlapping circles when there are three nodes, two side by side otherwise.
    internal static string Venn() => LayoutDef("venn", "relationship", $$"""
        <dgm:layoutNode name="diagram">
          <dgm:alg type="composite"/>
          {{Head}}
          <dgm:constrLst>
            <dgm:constr type="diam" for="ch" forName="c1" refType="h" fact="0.55"/>
            <dgm:constr type="diam" for="ch" forName="c2" refType="h" fact="0.55"/>
            <dgm:constr type="diam" for="ch" forName="c3" refType="h" fact="0.55"/>
            <dgm:constr type="ctrX" for="ch" forName="c1" refType="w" fact="0.5"/>
            <dgm:constr type="ctrY" for="ch" forName="c1" refType="h" fact="0.3"/>
            <dgm:constr type="ctrX" for="ch" forName="c2" refType="w" fact="0.38"/>
            <dgm:constr type="ctrY" for="ch" forName="c2" refType="h" fact="0.65"/>
            <dgm:constr type="ctrX" for="ch" forName="c3" refType="w" fact="0.62"/>
            <dgm:constr type="ctrY" for="ch" forName="c3" refType="h" fact="0.65"/>
            <dgm:constr type="primFontSz" for="ch" forName="c1" op="equ" val="40"/>
            <dgm:constr type="primFontSz" for="ch" forName="c2" op="equ" val="40"/>
            <dgm:constr type="primFontSz" for="ch" forName="c3" op="equ" val="40"/>
          </dgm:constrLst>
          <dgm:ruleLst/>
          <dgm:forEach name="a" axis="ch" ptType="node" st="1" cnt="1">{{Node1("ellipse", "c1", "")}}</dgm:forEach>
          <dgm:forEach name="b" axis="ch" ptType="node" st="2" cnt="1">{{Node1("ellipse", "c2", "")}}</dgm:forEach>
          <dgm:forEach name="c" axis="ch" ptType="node" st="3" cnt="1">{{Node1("ellipse", "c3", "")}}</dgm:forEach>
        </dgm:layoutNode>
        """);

    internal static string LayoutFor(string family) => family switch
    {
        "list" => List(),
        "process" => Process(),
        "cycle" => Cycle(),
        "hierarchy" => Hierarchy(),
        "pyramid" => Pyramid(),
        "venn" => Venn(),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    // ---------------------------------------------------------------- colours and style

    internal static string Colors(bool span = false)
    {
        string fill = span
            ? "<dgm:fillClrLst meth=\"span\" hueDir=\"cw\"><a:schemeClr val=\"accent2\"/><a:schemeClr val=\"accent6\"/></dgm:fillClrLst>"
            : "<dgm:fillClrLst meth=\"repeat\"><a:schemeClr val=\"accent1\"/></dgm:fillClrLst>";
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><dgm:colorsDef {Ns} uniqueId=\"urn:onedocument.test/colors/accent1_2\"><dgm:title val=\"\"/><dgm:desc val=\"\"/><dgm:catLst><dgm:cat type=\"accent1\" pri=\"11200\"/></dgm:catLst>" +
            $"<dgm:styleLbl name=\"node1\">{fill}<dgm:linClrLst meth=\"repeat\"><a:schemeClr val=\"lt1\"/></dgm:linClrLst><dgm:effectClrLst/><dgm:txLinClrLst/><dgm:txFillClrLst meth=\"repeat\"><a:schemeClr val=\"lt1\"/></dgm:txFillClrLst><dgm:txEffectClrLst/></dgm:styleLbl>" +
            "<dgm:styleLbl name=\"sibTrans2D1\"><dgm:fillClrLst meth=\"repeat\"><a:schemeClr val=\"accent1\"><a:tint val=\"60000\"/></a:schemeClr></dgm:fillClrLst><dgm:linClrLst meth=\"repeat\"><a:schemeClr val=\"accent1\"><a:tint val=\"60000\"/></a:schemeClr></dgm:linClrLst><dgm:effectClrLst/><dgm:txLinClrLst/><dgm:txFillClrLst meth=\"repeat\"><a:schemeClr val=\"lt1\"/></dgm:txFillClrLst><dgm:txEffectClrLst/></dgm:styleLbl>" +
            "<dgm:styleLbl name=\"parChTrans1D1\"><dgm:fillClrLst meth=\"repeat\"><a:schemeClr val=\"accent1\"/></dgm:fillClrLst><dgm:linClrLst meth=\"repeat\"><a:schemeClr val=\"accent1\"><a:shade val=\"60000\"/></a:schemeClr></dgm:linClrLst><dgm:effectClrLst/><dgm:txLinClrLst/><dgm:txFillClrLst meth=\"repeat\"><a:schemeClr val=\"tx1\"/></dgm:txFillClrLst><dgm:txEffectClrLst/></dgm:styleLbl>" +
            "</dgm:colorsDef>";
    }

    internal static string QuickStyle()
    {
        const string Scene = "<dgm:scene3d><a:camera prst=\"orthographicFront\"/><a:lightRig rig=\"threePt\" dir=\"t\"/></dgm:scene3d>";
        string Label(string name, int ln, int fill, string font) =>
            $"<dgm:styleLbl name=\"{name}\">{Scene}<dgm:sp3d/><dgm:txPr/><dgm:style><a:lnRef idx=\"{ln}\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:lnRef><a:fillRef idx=\"{fill}\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:fillRef><a:effectRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:effectRef><a:fontRef idx=\"{font}\"><a:schemeClr val=\"lt1\"/></a:fontRef></dgm:style></dgm:styleLbl>";
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><dgm:styleDef {Ns} uniqueId=\"urn:onedocument.test/quickstyle/simple1\"><dgm:title val=\"\"/><dgm:desc val=\"\"/><dgm:catLst><dgm:cat type=\"simple\" pri=\"10100\"/></dgm:catLst>{Scene}" +
            Label("node1", 2, 1, "minor") + Label("sibTrans2D1", 0, 1, "minor") + Label("parChTrans1D1", 2, 0, "minor") + "</dgm:styleDef>";
    }

    // ---------------------------------------------------------------- packages

    private const string DgmUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    internal static string FrameBody(double widthPt, double heightPt, string? alt, string rels = "rIdD1;rIdL1;rIdQ1;rIdC1")
    {
        string[] r = rels.Split(';');
        long cx = (long)(widthPt * 12700), cy = (long)(heightPt * 12700);
        return "<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
            $"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:docPr id=\"1\" name=\"Diagram 1\"" + (alt is null ? string.Empty : $" descr=\"{System.Security.SecurityElement.Escape(alt)}\"") + "/>" +
            $"<a:graphic xmlns:a=\"{A}\"><a:graphicData uri=\"{DgmUri}\"><dgm:relIds xmlns:dgm=\"{Dgm}\" xmlns:r=\"{R}\" r:dm=\"{r[0]}\" r:lo=\"{r[1]}\" r:qs=\"{r[2]}\" r:cs=\"{r[3]}\"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
    }

    // A DOCX with one diagram. `cache` is a dsp:drawing part to store with it (null for none).
    internal static byte[] Docx(string family, IReadOnlyList<Node> roots, double widthPt = 432, double heightPt = 288, string? cache = null, string? alt = "A diagram", bool span = false, string? layoutXml = null, string? docVars = null)
    {
        const string RT = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        string rels =
            $"<Relationship Id=\"rIdD1\" Type=\"{RT}/diagramData\" Target=\"diagrams/data1.xml\"/>" +
            $"<Relationship Id=\"rIdL1\" Type=\"{RT}/diagramLayout\" Target=\"diagrams/layout1.xml\"/>" +
            $"<Relationship Id=\"rIdQ1\" Type=\"{RT}/diagramQuickStyle\" Target=\"diagrams/quickStyle1.xml\"/>" +
            $"<Relationship Id=\"rIdC1\" Type=\"{RT}/diagramColors\" Target=\"diagrams/colors1.xml\"/>" +
            $"<Relationship Id=\"rIdTh\" Type=\"{RT}/theme\" Target=\"theme/theme1.xml\"/>" +
            (cache is null ? string.Empty : "<Relationship Id=\"rIdX1\" Type=\"http://schemas.microsoft.com/office/2007/relationships/diagramDrawing\" Target=\"diagrams/drawing1.xml\"/>");
        string types =
            "<Override PartName=\"/word/diagrams/data1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml\"/>" +
            "<Override PartName=\"/word/diagrams/layout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml\"/>" +
            "<Override PartName=\"/word/diagrams/quickStyle1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml\"/>" +
            "<Override PartName=\"/word/diagrams/colors1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml\"/>" +
            "<Override PartName=\"/word/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>" +
            (cache is null ? string.Empty : "<Override PartName=\"/word/diagrams/drawing1.xml\" ContentType=\"application/vnd.ms-office.drawingml.diagramDrawing+xml\"/>");
        var parts = new List<(string, byte[])>
        {
            ("word/diagrams/data1.xml", Encoding.UTF8.GetBytes(DataModel(roots, cache is null ? null : "rIdX1", docVars))),
            ("word/diagrams/layout1.xml", Encoding.UTF8.GetBytes(layoutXml ?? LayoutFor(family))),
            ("word/diagrams/quickStyle1.xml", Encoding.UTF8.GetBytes(QuickStyle())),
            ("word/diagrams/colors1.xml", Encoding.UTF8.GetBytes(Colors(span))),
            ("word/theme/theme1.xml", Encoding.UTF8.GetBytes(DrawingSamples.ThemeXml)),
        };
        if (cache is not null)
        {
            parts.Add(("word/diagrams/drawing1.xml", Encoding.UTF8.GetBytes(cache)));
        }

        return TestDocx.Bytes(FrameBody(widthPt, heightPt, alt), extraRels: rels, extraParts: parts, extraTypes: types);
    }

    // A one-slide PPTX with the diagram in a graphic frame at (36, 54) points.
    internal static byte[] Pptx(string family, IReadOnlyList<Node> roots, double widthPt = 648, double heightPt = 400, string? cache = null, string? alt = "A diagram", bool span = false)
    {
        const string RT = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        string Pns = DrawingSamples.Pns;
        const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
        string frame = "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"2\" name=\"Diagram 1\"" + (alt is null ? string.Empty : $" descr=\"{System.Security.SecurityElement.Escape(alt)}\"") + "/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr>" +
            $"<p:xfrm><a:off x=\"{36 * 12700}\" y=\"{54 * 12700}\"/><a:ext cx=\"{(long)(widthPt * 12700)}\" cy=\"{(long)(heightPt * 12700)}\"/></p:xfrm><a:graphic><a:graphicData uri=\"{DgmUri}\"><dgm:relIds xmlns:dgm=\"{Dgm}\" r:dm=\"rIdD1\" r:lo=\"rIdL1\" r:qs=\"rIdQ1\" r:cs=\"rIdC1\"/></a:graphicData></a:graphic></p:graphicFrame>";
        string slide = Decl + $"<p:sld {Pns}><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>{frame}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";
        const string Rels = Decl + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">";
        var parts = new List<(string Name, string Xml)>
        {
            ("[Content_Types].xml", Decl + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/><Override PartName=\"/ppt/slides/slide1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>" +
                "<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/><Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>" +
                "<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>" +
                "<Override PartName=\"/ppt/diagrams/data1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml\"/><Override PartName=\"/ppt/diagrams/layout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml\"/>" +
                "<Override PartName=\"/ppt/diagrams/quickStyle1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml\"/><Override PartName=\"/ppt/diagrams/colors1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml\"/>" +
                (cache is null ? string.Empty : "<Override PartName=\"/ppt/diagrams/drawing1.xml\" ContentType=\"application/vnd.ms-office.drawingml.diagramDrawing+xml\"/>") + "</Types>"),
            ("_rels/.rels", Rels + $"<Relationship Id=\"rId1\" Type=\"{RT}officeDocument\" Target=\"ppt/presentation.xml\"/></Relationships>"),
            ("ppt/presentation.xml", Decl + $"<p:presentation {Pns}><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst><p:sldIdLst><p:sldId id=\"256\" r:id=\"rId2\"/></p:sldIdLst><p:sldSz cx=\"9144000\" cy=\"6858000\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>"),
            ("ppt/_rels/presentation.xml.rels", Rels + $"<Relationship Id=\"rId1\" Type=\"{RT}slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/><Relationship Id=\"rId2\" Type=\"{RT}slide\" Target=\"slides/slide1.xml\"/></Relationships>"),
            ("ppt/slideMasters/slideMaster1.xml", Decl + $"<p:sldMaster {Pns}><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld><p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/><p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst></p:sldMaster>"),
            ("ppt/slideMasters/_rels/slideMaster1.xml.rels", Rels + $"<Relationship Id=\"rId1\" Type=\"{RT}slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/><Relationship Id=\"rId2\" Type=\"{RT}theme\" Target=\"../theme/theme1.xml\"/></Relationships>"),
            ("ppt/slideLayouts/slideLayout1.xml", Decl + $"<p:sldLayout {Pns} type=\"blank\"><p:cSld name=\"Blank\"><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>"),
            ("ppt/slideLayouts/_rels/slideLayout1.xml.rels", Rels + $"<Relationship Id=\"rId1\" Type=\"{RT}slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/></Relationships>"),
            ("ppt/slides/slide1.xml", slide),
            ("ppt/slides/_rels/slide1.xml.rels", Rels + $"<Relationship Id=\"rId1\" Type=\"{RT}slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>" +
                $"<Relationship Id=\"rIdD1\" Type=\"{RT}diagramData\" Target=\"../diagrams/data1.xml\"/><Relationship Id=\"rIdL1\" Type=\"{RT}diagramLayout\" Target=\"../diagrams/layout1.xml\"/>" +
                $"<Relationship Id=\"rIdQ1\" Type=\"{RT}diagramQuickStyle\" Target=\"../diagrams/quickStyle1.xml\"/><Relationship Id=\"rIdC1\" Type=\"{RT}diagramColors\" Target=\"../diagrams/colors1.xml\"/>" +
                (cache is null ? string.Empty : "<Relationship Id=\"rIdX1\" Type=\"http://schemas.microsoft.com/office/2007/relationships/diagramDrawing\" Target=\"../diagrams/drawing1.xml\"/>") + "</Relationships>"),
            ("ppt/theme/theme1.xml", DrawingSamples.ThemeXml),
            ("ppt/diagrams/data1.xml", DataModel(roots, cache is null ? null : "rIdX1")),
            ("ppt/diagrams/layout1.xml", LayoutFor(family)),
            ("ppt/diagrams/quickStyle1.xml", QuickStyle()),
            ("ppt/diagrams/colors1.xml", Colors(span)),
        };
        if (cache is not null)
        {
            parts.Add(("ppt/diagrams/drawing1.xml", cache));
        }

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
        }

        return ms.ToArray();
    }

    // The same families as slides, with the engine's own drawing stored in the cached variants.
    internal static IEnumerable<(string Name, byte[] Pptx)> Presentations()
    {
        foreach ((string name, string family, Node[] roots, bool span) in Cases())
        {
            if (name is not ("list-5" or "process-3" or "cycle-4" or "hierarchy-7" or "pyramid-4" or "venn-3"))
            {
                continue;
            }

            yield return ("smartart-" + name, Pptx(family, roots, 648, 400, null, "Diagram " + name, span));
            yield return ("smartart-" + name + "-cached", Pptx(family, roots, 648, 400, CacheFor(family, roots, 648, 400), "Diagram " + name, span));
        }
    }

    // The drawing part our own engine makes for a generated diagram: used as the stored cache of the "cached" variants.
    internal static string CacheFor(string family, IReadOnlyList<Node> roots, double widthPt, double heightPt)
    {
        byte[] plain = Docx(family, roots, widthPt, heightPt);
        using RecursiveAI.OneDocument.Documents.Document doc = RecursiveAI.OneDocument.Documents.Document.Open(new MemoryStream(plain), leaveOpen: false);
        DiagramLayoutResult result = DiagramHost.Generate(doc.Package, doc.MainPartName, new DiagramReferences("rIdD1", "rIdL1", "rIdQ1", "rIdC1"), widthPt, heightPt, fonts: Fonts.Value)
            ?? throw new InvalidOperationException("layout failed");
        return Encoding.UTF8.GetString(result.Drawing);
    }

    internal static IReadOnlyList<(string Name, string Family, Node[] Roots, bool Span)> Cases() =>
    [
        ("list-5", "list", Flat("Plan", "Design", "Build", "Test", "Ship"), false),
        ("list-1", "list", Flat("Only one"), false),
        ("list-9-span", "list", Flat("One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine"), true),
        ("list-long-text", "list", [new Node("A considerably longer sentence that has to shrink to fit in its box"), new Node("Short"), new Node("Medium length text")], false),
        ("list-children", "list", [new Node("Phase one", new Node("Scope"), new Node("Budget")), new Node("Phase two", new Node("Build"))], false),
        ("process-3", "process", Flat("Collect", "Process", "Report"), false),
        ("process-6", "process", Flat("Idea", "Spec", "Code", "Test", "Docs", "Release"), false),
        ("cycle-4", "cycle", Flat("Plan", "Do", "Check", "Act"), false),
        ("cycle-6", "cycle", Flat("Spring", "Summer", "Autumn", "Winter", "Rain", "Snow"), false),
        ("hierarchy-7", "hierarchy", [new Node("Director", new Node("Sales", new Node("North"), new Node("South")), new Node("Support", new Node("Tier 1"), new Node("Tier 2")))], false),
        ("hierarchy-3", "hierarchy", [new Node("Root", new Node("Left"), new Node("Right"))], false),
        ("pyramid-4", "pyramid", Flat("Vision", "Strategy", "Tactics", "Operations"), false),
        ("venn-3", "venn", Flat("Design", "Code", "Test"), false),
    ];

    internal static IEnumerable<(string Name, byte[] Docx)> All()
    {
        foreach ((string name, string family, Node[] roots, bool span) in Cases())
        {
            yield return ("smartart-" + name, Docx(family, roots, 432, 288, null, "Diagram " + name, span));
            yield return ("smartart-" + name + "-cached", Docx(family, roots, 432, 288, CacheFor(family, roots, 432, 288), "Diagram " + name, span));
        }
    }
}
