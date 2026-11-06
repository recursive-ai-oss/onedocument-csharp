using System.Text;
using OneRender.Painting;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Rendering.SmartArt;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Runs a diagram layout (a row of three boxes joined by arrows) and paints it, with and without a stored drawing: under NativeAOT and WASM.</summary>
internal static class SmartArtSmoke
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Ns = "xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

    private const string Layout = "<dgm:layoutDef " + Ns + " uniqueId=\"urn:smoke\"><dgm:layoutNode name=\"diagram\"><dgm:alg type=\"lin\"/><dgm:shape r:blip=\"\"><dgm:adjLst/></dgm:shape><dgm:presOf/>" +
        "<dgm:constrLst><dgm:constr type=\"w\" for=\"ch\" forName=\"node\" refType=\"w\"/><dgm:constr type=\"h\" for=\"ch\" forName=\"node\" refType=\"w\" refFor=\"ch\" refForName=\"node\" fact=\"0.6\"/>" +
        "<dgm:constr type=\"w\" for=\"ch\" forName=\"sibTrans\" refType=\"w\" refFor=\"ch\" refForName=\"node\" fact=\"0.4\"/><dgm:constr type=\"h\" for=\"ch\" forName=\"sibTrans\" refType=\"w\" refFor=\"ch\" refForName=\"sibTrans\" fact=\"0.6\"/>" +
        "<dgm:constr type=\"primFontSz\" for=\"ch\" forName=\"node\" op=\"equ\" val=\"65\"/></dgm:constrLst><dgm:ruleLst/>" +
        "<dgm:forEach name=\"n\" axis=\"ch\" ptType=\"node\"><dgm:layoutNode name=\"node\"><dgm:alg type=\"tx\"/><dgm:shape type=\"roundRect\" r:blip=\"\"><dgm:adjLst/></dgm:shape><dgm:presOf axis=\"desOrSelf\" ptType=\"node\"/>" +
        "<dgm:constrLst><dgm:constr type=\"lMarg\" refType=\"primFontSz\" fact=\"0.3\"/></dgm:constrLst><dgm:ruleLst><dgm:rule type=\"primFontSz\" val=\"5\"/></dgm:ruleLst></dgm:layoutNode>" +
        "<dgm:forEach name=\"s\" axis=\"followSib\" ptType=\"sibTrans\" cnt=\"1\"><dgm:layoutNode name=\"sibTrans\"><dgm:alg type=\"conn\"/><dgm:shape type=\"conn\" r:blip=\"\"><dgm:adjLst/></dgm:shape><dgm:presOf axis=\"self\"/><dgm:constrLst/><dgm:ruleLst/></dgm:layoutNode></dgm:forEach>" +
        "</dgm:forEach></dgm:layoutNode></dgm:layoutDef>";

    private static string Data()
    {
        var pts = new StringBuilder("<dgm:pt modelId=\"0\" type=\"doc\"/>");
        var cxns = new StringBuilder();
        string[] names = ["One", "Two", "Three"];
        for (int i = 0; i < 3; i++)
        {
            int n = 10 + (i * 10);
            pts.Append("<dgm:pt modelId=\"").Append(n).Append("\"><dgm:t><a:bodyPr/><a:p><a:r><a:t>").Append(names[i]).Append("</a:t></a:r></a:p></dgm:t></dgm:pt>");
            pts.Append("<dgm:pt modelId=\"").Append(n + 1).Append("\" type=\"parTrans\" cxnId=\"").Append(n + 3).Append("\"/><dgm:pt modelId=\"").Append(n + 2).Append("\" type=\"sibTrans\" cxnId=\"").Append(n + 3).Append("\"/>");
            cxns.Append("<dgm:cxn modelId=\"").Append(n + 3).Append("\" srcId=\"0\" destId=\"").Append(n).Append("\" srcOrd=\"").Append(i).Append("\" destOrd=\"0\" parTransId=\"").Append(n + 1).Append("\" sibTransId=\"").Append(n + 2).Append("\"/>");
        }

        return "<dgm:dataModel " + Ns + "><dgm:ptLst>" + pts + "</dgm:ptLst><dgm:cxnLst>" + cxns + "</dgm:cxnLst></dgm:dataModel>";
    }

    public static List<string> Run()
    {
        var failures = new List<string>();
        using var package = new InMemoryPackage();
        package.AddPart("/ppt/slides/slide1.xml", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml", Encoding.UTF8.GetBytes("<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"/>"));
        package.AddPart("/ppt/diagrams/data1.xml", "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml", Encoding.UTF8.GetBytes(Data()));
        package.AddPart("/ppt/diagrams/layout1.xml", "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml", Encoding.UTF8.GetBytes(Layout));
        package.SetRelationships("/ppt/slides/slide1.xml",
        [
            new Relationship("rIdD1", Rel + "diagramData", "../diagrams/data1.xml"),
            new Relationship("rIdL1", Rel + "diagramLayout", "../diagrams/layout1.xml"),
        ]);
        var refs = new DiagramReferences("rIdD1", "rIdL1", null, null);
        DiagramLayoutResult? made = DiagramHost.Generate(package, "/ppt/slides/slide1.xml", refs, 432, 288);
        if (made is null || made.Shapes.Count != 5)
        {
            failures.Add("smartart shapes " + (made?.Shapes.Count ?? -1));
            return failures;
        }

        var list = new DisplayList();
        uint n = 0;
        var reports = new List<DiagramReport>();
        bool painted = DiagramHost.Paint(package, "/ppt/slides/slide1.xml", new DiagramPaintRequest(refs, list, () => ItemId.Of(++n), new DrawingContext(), 0, 0, 576, 384, "diagram", false), new DiagramOptions { Report = reports.Add });
        list.Validate();
        if (!painted || reports.Count != 1 || reports[0].Source != DiagramSource.Regenerated)
        {
            failures.Add("smartart paint");
        }

        int glyphRuns = 0;
        for (int i = 0; i < list.Count; i++)
        {
            glyphRuns += list[i].Kind == ItemKind.GlyphRun ? 1 : 0;
        }

        if (glyphRuns < 3)
        {
            failures.Add("smartart text " + glyphRuns);
        }

        return failures;
    }
}
