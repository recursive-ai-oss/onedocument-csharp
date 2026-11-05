using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>Builds small presentations in memory and reads saved packages back as plain zip entries.</summary>
internal static class Decks
{
    public const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    public const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    public const string Pml = "application/vnd.openxmlformats-officedocument.presentationml.";

    public static byte[] Save(Presentation deck, OfficeFormat format = OfficeFormat.Pptx)
    {
        using var ms = new MemoryStream();
        deck.SaveAs(ms, format);
        return ms.ToArray();
    }

    public static Presentation Reopen(byte[] bytes, OpenContext? context = null) => Presentation.Open(new MemoryStream(bytes), context);

    /// <summary>Every entry of the zip by name, uncompressed.</summary>
    public static SortedDictionary<string, byte[]> Entries(byte[] zip)
    {
        var map = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry e in archive.Entries)
        {
            using Stream s = e.Open();
            using var buf = new MemoryStream();
            s.CopyTo(buf);
            map[e.FullName] = buf.ToArray();
        }

        return map;
    }

    /// <summary>The names of entries whose bytes differ between two saved packages (added or removed ones included).</summary>
    public static List<string> Changed(byte[] before, byte[] after)
    {
        SortedDictionary<string, byte[]> a = Entries(before);
        SortedDictionary<string, byte[]> b = Entries(after);
        var names = new List<string>();
        foreach (string k in a.Keys.Union(b.Keys))
        {
            if (!a.TryGetValue(k, out byte[]? x) || !b.TryGetValue(k, out byte[]? y) || !x.AsSpan().SequenceEqual(y))
            {
                names.Add(k);
            }
        }

        return names;
    }

    public static string Xml(byte[] zip, string entry) => Encoding.UTF8.GetString(Entries(zip)[entry]);

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    public static SlideLayout Layout(Presentation deck, string name) => deck.Masters[0].Layouts.First(l => l.Name == name);

    /// <summary>Appends raw shape XML (prefixes a, p and r are declared on the slide) to the slide's shape tree.</summary>
    public static void AddShapeXml(PresentationPart part, string xml) => part.ShapeTreeMarkup.Node.AppendXml(Utf8(xml));

    /// <summary>Adds a part and a relationship from 'source' to it. Returns the relationship id.</summary>
    public static string Attach(Presentation deck, string source, string type, string partName, string contentType, byte[] content)
    {
        if (deck.Package.FindPart(partName) is null)
        {
            deck.Package.AddPart(partName, contentType, content);
        }

        var rels = new List<Relationship>(deck.Package.GetRelationships(source));
        string id = "rId" + (rels.Count + 1);
        while (rels.Any(r => r.Id == id))
        {
            id += "x";
        }

        rels.Add(new Relationship(id, Rel + type, Presentation.RelativeTarget(source, partName)));
        deck.Package.SetRelationships(source, rels);
        return id;
    }

    /// <summary>Adds a notes master (with a body placeholder and notes style) to a deck made by Presentation.Create.</summary>
    public static void AddNotesMaster(Presentation deck)
    {
        const string name = "/ppt/notesMasters/notesMaster1.xml";
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<p:notesMaster xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\" xmlns:p=\"" + P + "\"><p:cSld><p:spTree>" +
            "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr/><p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr/><p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"685800\" y=\"4400550\"/><a:ext cx=\"5486400\" cy=\"3600450\"/></a:xfrm></p:spPr>" +
            "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Click to edit Master text styles</a:t></a:r></a:p></p:txBody></p:sp>" +
            "</p:spTree></p:cSld><p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
            "<p:notesStyle><a:lvl1pPr marL=\"0\" algn=\"l\"><a:defRPr sz=\"1200\"/></a:lvl1pPr></p:notesStyle></p:notesMaster>";
        deck.Package.AddPart(name, Pml + "notesMaster+xml", Utf8(xml));
        deck.Package.SetRelationships(name, [new Relationship("rId1", Rel + "theme", "../theme/theme1.xml")]);
        var rels = new List<Relationship>(deck.Package.GetRelationships(deck.MainPartName)) { new Relationship("rId90", Rel + "notesMaster", "notesMasters/notesMaster1.xml") };
        deck.Package.SetRelationships(deck.MainPartName, rels);
        // p:notesMasterIdLst follows p:sldMasterIdLst.
        MarkupRef masters = deck.Markup.FindChild(SchemaNamespace.Presentation, "sldMasterIdLst"u8)!.Value;
        masters.Node.InsertXmlAfter(Utf8("<p:notesMasterIdLst><p:notesMasterId r:id=\"rId90\"/></p:notesMasterIdLst>"));
    }

    /// <summary>A picture shape using the relationship id.</summary>
    public static string Picture(int id, string rid) =>
        "<p:pic><p:nvPicPr><p:cNvPr id=\"" + id + "\" name=\"Picture " + id + "\" descr=\"alt " + id + "\"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr>" +
        "<p:blipFill><a:blip r:embed=\"" + rid + "\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x=\"100\" y=\"200\"/><a:ext cx=\"300\" cy=\"400\"/></a:xfrm></p:spPr></p:pic>";

    public static string TextBox(int id, string text, string rPr = "") =>
        "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"TextBox " + id + "\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"10\" y=\"20\"/><a:ext cx=\"30\" cy=\"40\"/></a:xfrm></p:spPr>" +
        "<p:txBody><a:bodyPr wrap=\"none\"/><a:lstStyle/><a:p><a:r>" + rPr + "<a:t>" + text + "</a:t></a:r></a:p></p:txBody></p:sp>";
}
