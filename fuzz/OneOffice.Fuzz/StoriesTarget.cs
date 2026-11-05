using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// Text boxes, story list labels and document properties over arbitrary bytes. The input is split at zero bytes into the document
/// part, a numbering part, a core-properties part and a custom-properties part. Only <see cref="OfficeException"/> may come out of
/// reading; edits go through the property setters and a text box edit, and an edited part must stay well-formed.
/// </summary>
internal static class StoriesTarget
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private static readonly OfficeLimits s_limits = new() { MaxXmlDepth = 64, MaxPartSize = 1 << 20 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        var pieces = new List<byte[]>();
        byte[] all = data.ToArray();
        int start = 0;
        for (int i = 0; i < all.Length && pieces.Count < 3; i++)
        {
            if (all[i] == 0)
            {
                pieces.Add(all[start..i]);
                start = i + 1;
            }
        }

        pieces.Add(all[start..]);
        while (pieces.Count < 4)
        {
            pieces.Add([]);
        }

        var p = new InMemoryPackage(s_limits);
        var root = new List<Relationship> { new("rId1", Rel + "officeDocument", "word/document.xml") };
        p.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", pieces[0]);
        if (pieces[1].Length > 0)
        {
            p.AddPart("/word/numbering.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml", pieces[1]);
            p.SetRelationships("/word/document.xml", [new Relationship("rId2", Rel + "numbering", "numbering.xml")]);
        }

        if (pieces[2].Length > 0)
        {
            p.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", pieces[2]);
            root.Add(new Relationship("rId3", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"));
        }

        if (pieces[3].Length > 0)
        {
            p.AddPart("/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml", pieces[3]);
            root.Add(new Relationship("rId4", Rel + "custom-properties", "docProps/custom.xml"));
        }

        p.SetRelationships(null, root);
        // Only parts that were well-formed to begin with must be well-formed after editing.
        var wellFormed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string part in new[] { "/word/document.xml", "/docProps/core.xml", "/docProps/custom.xml" })
        {
            if (p.FindPart(part) is { } original && RecursiveAI.OneOffice.Markup.MarkupDocument.TryParse(original.GetBytes(), out _, null))
            {
                wellFormed.Add(part);
            }
        }

        try
        {
            using var d = Document.FromPackage(p, new OpenContext(s_limits));
            foreach (TextBox box in d.TextBoxes())
            {
                _ = box.Text;
                _ = box.Chain;
                _ = box.Alternate;
                foreach (TextBox inner in box.TextBoxes())
                {
                    _ = inner.Text;
                }
            }

            _ = d.Numbering.LabelStories();
            _ = DocumentText.ListLabels(d);
            _ = d.Properties.Core.Created;
            _ = d.Properties.Core.Title;
            _ = d.Properties.Extended.Pages;
            _ = d.Properties.Custom.List();

            Edit(() => d.Properties.Core.Title = "t");
            Edit(() => d.Properties.Core.Modified = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));
            Edit(() => d.Properties.Custom.SetString("k", "v"));
            Edit(() => d.Properties.Custom.Remove("k"));
            Edit(() =>
            {
                var boxes = d.TextBoxes();
                if (boxes.Count > 0)
                {
                    foreach (Paragraph para in boxes[0].Paragraphs())
                    {
                        para.SetText("edited");
                        break;
                    }
                }
            });
            d.CommitChanges();
            foreach (string part in new[] { "/word/document.xml", "/docProps/core.xml", "/docProps/custom.xml", "/docProps/app.xml" })
            {
                if (wellFormed.Contains(part) && p.FindPart(part) is { } stored && !RecursiveAI.OneOffice.Markup.MarkupDocument.TryParse(stored.GetBytes(), out _, null))
                {
                    throw new InvalidOperationException("an edited part does not parse: " + part);
                }
            }
        }
        catch (OfficeException)
        {
        }
    }

    private static void Edit(Action edit)
    {
        try
        {
            edit();
        }
        catch (OfficeException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        string doc = $"<w:document xmlns:w=\"{w}\" xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:v=\"urn:schemas-microsoft-com:vml\"><w:body><w:p><w:r><w:t>a</w:t></w:r><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wps:wsp><wps:txbx id=\"1\"><w:txbxContent><w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>b</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></w:drawing></mc:Choice><mc:Fallback><w:pict><v:shape id=\"s\"><v:textbox style=\"mso-next-textbox:#s\"><w:txbxContent><w:p><w:r><w:t>b</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p></w:body></w:document>";
        string numbering = $"<w:numbering xmlns:w=\"{w}\"><w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>";
        string core = "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\"><dc:title>t</dc:title><dcterms:created>2020-01-02T03:04:05Z</dcterms:created></cp:coreProperties>";
        string custom = "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" pid=\"2\" name=\"A\"><vt:i4>1</vt:i4></property></Properties>";
        File.WriteAllBytes(Path.Combine(directory, "stories"), Encoding.UTF8.GetBytes(doc + "\0" + numbering + "\0" + core + "\0" + custom));
        return 0;
    }
}
