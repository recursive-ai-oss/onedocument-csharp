using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Documents.Tests;

/// <summary>Builds small word-processing packages in memory.</summary>
internal static class TestDocs
{
    public const string WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public const string RNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    /// <summary>A package with the given body XML (children of w:body) and optional styles, numbering and theme parts.</summary>
    public static InMemoryPackage Package(string bodyXml, string? stylesXml = null, string? numberingXml = null, string? themeXml = null)
    {
        var p = new InMemoryPackage();
        var rels = new List<Relationship>();
        p.SetRelationships(null, [new Relationship("rId1", RelType + "officeDocument", "word/document.xml")]);
        p.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"{WNs}\" xmlns:r=\"{RNs}\"><w:body>{bodyXml}</w:body></w:document>"));
        if (stylesXml is not null)
        {
            p.AddPart("/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml", Part("styles", stylesXml));
            rels.Add(new Relationship("rId2", RelType + "styles", "styles.xml"));
        }

        if (numberingXml is not null)
        {
            p.AddPart("/word/numbering.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml", Part("numbering", numberingXml));
            rels.Add(new Relationship("rId3", RelType + "numbering", "numbering.xml"));
        }

        if (themeXml is not null)
        {
            p.AddPart("/word/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml", Encoding.UTF8.GetBytes(themeXml));
            rels.Add(new Relationship("rId4", RelType + "theme", "theme/theme1.xml"));
        }

        if (rels.Count > 0)
        {
            p.SetRelationships("/word/document.xml", rels);
        }

        return p;
    }

    private static byte[] Part(string root, string inner) =>
        Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:{root} xmlns:w=\"{WNs}\">{inner}</w:{root}>");

    public static Document Open(string bodyXml, string? stylesXml = null, string? numberingXml = null, string? themeXml = null, DefectCollection? defects = null)
    {
        InMemoryPackage p = Package(bodyXml, stylesXml, numberingXml, themeXml);
        return Document.FromPackage(p, new OpenContext(defects: defects));
    }
}
