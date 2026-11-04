using RecursiveAI.OneOffice.Schema;
using RecursiveAI.OneOffice.Schema.Tests;
using RecursiveAI.OneOffice.Schema.Wordprocessing;

namespace RecursiveAI.OneOffice.Smoke;

/// <summary>Touches a few generated typed views under NativeAOT and WASM: reading, editing through a view, the factory and the visitor.</summary>
internal static class SchemaSmoke
{
    private struct Counter : IViewVisitor
    {
        public int Visited;

        public void Visit<T>(T view) where T : struct, IElementView<T> => Visited += view.Markup.IsValid ? 1 : 0;
    }

    public static List<string> Run()
    {
        var failures = new List<string>();
        const string Xml = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:pPr><w:jc w:val=\"left\"/></w:pPr><w:r><w:t>hi</w:t></w:r></w:p></w:body></w:document>";
        var tree = TextTree.Parse(Xml);
        var body = tree.RootRef.FindChild(SchemaNamespace.Wordprocessing, "body"u8)!.Value;
        var p = Paragraph.Create(body.FindChild(SchemaNamespace.Wordprocessing, "p"u8)!.Value);
        var jc = p.ParagraphProperties!.Value.Justification!.Value;
        if (jc.Val != JustificationValue.Left) failures.Add("schema read enum");
        jc.Val = JustificationValue.Center;
        if (tree.Serialize() != Xml.Replace("left", "center", StringComparison.Ordinal)) failures.Add("schema edit");
        if (p.Runs.Count != 1 || p.Runs.ToArray()[0].Texts.ToArray()[0].Text != "hi") failures.Add("schema children");
        var kind = ViewFactory.RootKind(SchemaNamespace.Wordprocessing, "document"u8);
        var counter = new Counter();
        ViewFactory.Dispatch(kind, tree.RootRef, ref counter);
        if (kind != ViewKind.WordprocessingDocument || counter.Visited != 1) failures.Add("schema factory");
        return failures;
    }
}
