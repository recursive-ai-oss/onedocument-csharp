using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneOffice.Documents;
using RecursiveAI.OneOffice.Documents.Numbering;
using RecursiveAI.OneOffice.Documents.Styles;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Tools.DocxProbe;

internal static partial class Program
{
    /// <summary>
    /// Adds a paragraph style and a list to a document, applies them to its first paragraphs and writes a new zip in which every
    /// part that was not edited is copied as stored. Used to check that edits stay schema-valid and open in other readers.
    /// </summary>
    private static int Edit(string input, string output) => Rewrite(input, output, doc =>
    {
        Paragraph[] paragraphs = doc.Body.Paragraphs().Take(4).ToArray();
        string? baseId = doc.Styles.Default(StyleType.Paragraph)?.Id;
        string id = "OneOfficeCallout";
        for (int i = 2; doc.Styles.Find(id) is not null; i++)
        {
            id = "OneOfficeCallout" + i;
        }

        StyleDefinition style = doc.Styles.AddStyle(id, "OneOffice Callout", StyleType.Paragraph, baseId);
        style.Markup.Node.AppendXml(Encoding.UTF8.GetBytes("<w:pPr xmlns:w=\"" + style.Markup.NamespaceUriText + "\"><w:spacing w:before=\"120\"/></w:pPr>"));
        long abs = doc.Numbering.AddAbstractNumbering([new NumberingDefinitions.LevelSpec("decimal", "%1."), new NumberingDefinitions.LevelSpec("lowerLetter", "%2)", LeftIndent: 1440)]);
        long num = doc.Numbering.AddInstance(abs);
        for (int i = 0; i < paragraphs.Length; i++)
        {
            if (i == 0)
            {
                doc.Styles.Apply(paragraphs[i], style);
            }
            else
            {
                paragraphs[i].SetNumbering(num, i % 2);
            }
        }

        return new Dictionary<string, object?> { ["styleId"] = id, ["numId"] = num };
    });

    /// <summary>Opens a document, lets <paramref name="change"/> edit it, and writes a new zip in which every part that was not edited is copied as stored.</summary>
    private static int Rewrite(string input, string output, Func<Document, Dictionary<string, object?>> change)
    {
        using InMemoryPackage package = Read(input);
        Dictionary<string, byte[]> before = package.Parts.ToDictionary(p => p.Name, p => p.GetBytes().ToArray(), StringComparer.OrdinalIgnoreCase);
        using Document doc = Document.FromPackage(package);
        Dictionary<string, object?> info = change(doc);
        doc.CommitChanges();

        string main = doc.MainPartName;
        int slash = main.LastIndexOf('/');
        string mainRels = main[..(slash + 1)] + "_rels/" + main[(slash + 1)..] + ".rels";
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool relsWritten = false;
        using var source = new ZipArchive(File.OpenRead(input), ZipArchiveMode.Read);
        using FileStream fs = File.Create(output);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (ZipArchiveEntry e in source.Entries)
        {
            string name = "/" + e.FullName;
            written.Add(name);
            ZipArchiveEntry copy = zip.CreateEntry(e.FullName, CompressionLevel.Optimal);
            copy.LastWriteTime = e.LastWriteTime;
            using Stream o = copy.Open();
            if (string.Equals(name, "/[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
            {
                string xml = Encoding.UTF8.GetString(Bytes(e));
                var extra = new StringBuilder();
                foreach (IPackagePart part in package.Parts)
                {
                    if (!before.ContainsKey(part.Name))
                    {
                        extra.Append("<Override PartName=\"").Append(part.Name).Append("\" ContentType=\"").Append(part.ContentType).Append("\"/>");
                    }
                }

                o.Write(Encoding.UTF8.GetBytes(xml.Replace("</Types>", extra + "</Types>", StringComparison.Ordinal)));
            }
            else if (string.Equals(name, mainRels, StringComparison.OrdinalIgnoreCase))
            {
                o.Write(Encoding.UTF8.GetBytes(RelationshipsXml(package.GetRelationships(main))));
                relsWritten = true;
            }
            else if (package.FindPart(name) is { } p && before.TryGetValue(p.Name, out byte[]? old) && !p.GetBytes().Span.SequenceEqual(old))
            {
                o.Write(p.GetBytes().Span);
            }
            else
            {
                o.Write(Bytes(e));
            }
        }

        foreach (IPackagePart part in package.Parts)
        {
            if (!written.Contains(part.Name))
            {
                ZipArchiveEntry e = zip.CreateEntry(part.Name.TrimStart('/'), CompressionLevel.Optimal);
                using Stream o = e.Open();
                o.Write(part.GetBytes().Span);
            }
        }

        if (!relsWritten)
        {
            ZipArchiveEntry e = zip.CreateEntry(mainRels.TrimStart('/'), CompressionLevel.Optimal);
            using Stream o = e.Open();
            o.Write(Encoding.UTF8.GetBytes(RelationshipsXml(package.GetRelationships(main))));
        }

        info["file"] = Path.GetFileName(input);
        Console.WriteLine(JsonSerializer.Serialize(info));
        return 0;
    }

    private static string RelationshipsXml(IReadOnlyList<Relationship> relationships)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (Relationship r in relationships)
        {
            sb.Append("<Relationship Id=\"").Append(r.Id).Append("\" Type=\"").Append(r.Type).Append("\" Target=\"").Append(System.Security.SecurityElement.Escape(r.Target)).Append('"');
            if (r.TargetMode == RelationshipTargetMode.External)
            {
                sb.Append(" TargetMode=\"External\"");
            }

            sb.Append("/>");
        }

        return sb.Append("</Relationships>").ToString();
    }
}
