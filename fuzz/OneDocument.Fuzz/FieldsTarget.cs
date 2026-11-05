using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Comments;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Documents.Notes;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>Field instructions, and the field, note and comment readers and editors over arbitrary document and part bytes.</summary>
internal static class FieldsTarget
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private static readonly OfficeLimits s_limits = new() { MaxXmlDepth = 64, MaxPartSize = 1 << 20 };

    /// <summary>The instruction parser alone: it never fails and keeps the text.</summary>
    public static void RunInstruction(ReadOnlySpan<byte> data)
    {
        string text = Encoding.UTF8.GetString(data);
        FieldInstruction f = FieldInstruction.Parse(text);
        if (f.Text != text)
        {
            throw new InvalidOperationException("the instruction text changed");
        }

        foreach (FieldSwitch s in f.Switches)
        {
            if (s.Name.Length != 1)
            {
                throw new InvalidOperationException("a switch name is not one character");
            }
        }
    }

    /// <summary>
    /// The input is split in three at its first two zero bytes: the document part, the notes part and the comments part. Readers
    /// must give typed outcomes, and an edited part must stay well-formed.
    /// </summary>
    public static void RunHub(ReadOnlySpan<byte> data)
    {
        byte[] all = data.ToArray();
        int a = Array.IndexOf(all, (byte)0);
        byte[] main = a < 0 ? all : all[..a];
        byte[] rest = a < 0 ? [] : all[(a + 1)..];
        int b = Array.IndexOf(rest, (byte)0);
        byte[] notes = b < 0 ? rest : rest[..b];
        byte[] comments = b < 0 ? [] : rest[(b + 1)..];

        var p = new InMemoryPackage(s_limits);
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml")]);
        p.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", main);
        var rels = new System.Collections.Generic.List<Relationship>();
        if (notes.Length > 0)
        {
            p.AddPart("/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml", notes);
            rels.Add(new Relationship("rId2", Rel + "footnotes", "footnotes.xml"));
        }

        if (comments.Length > 0)
        {
            p.AddPart("/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml", comments);
            rels.Add(new Relationship("rId3", Rel + "comments", "comments.xml"));
        }

        p.SetRelationships("/word/document.xml", rels);
        try
        {
            using var d = Document.FromPackage(p, new OpenContext(s_limits));
            foreach (Field f in d.Fields())
            {
                _ = f.Parsed;
                _ = f.ResultText;
            }

            Field[] fields = [.. d.Fields()];
            if (fields.Length > 0)
            {
                Edit(() => fields[0].SetInstruction(" X \\h "));
                Edit(() => d.Fields()[0].SetResult("r\t\n"));
            }

            _ = d.NoteReferences(NoteKind.Footnote);
            foreach (Note n in d.Footnotes)
            {
                _ = n.Text;
            }

            if (d.Footnotes.Count > 0)
            {
                Edit(() => d.Footnotes[d.Footnotes.Count - 1].SetText("t"));
            }

            foreach (Comment c in d.Comments)
            {
                _ = c.Text;
                _ = c.Parent;
                _ = c.Anchor;
            }

            Run? run = null;
            foreach (Paragraph para in d.Body.Paragraphs())
            {
                foreach (Run r in para.Runs())
                {
                    run = r;
                    break;
                }

                if (run is not null)
                {
                    break;
                }
            }

            if (run is not null)
            {
                Edit(() => d.Comments.Add(run, run, "a", "c"));
                Edit(() => d.Footnotes.Add("n").InsertReference(run));
            }

            d.CommitChanges();
            foreach (string part in new[] { "/word/document.xml", "/word/footnotes.xml", "/word/comments.xml" })
            {
                if (p.FindPart(part) is { } pp)
                {
                    _ = MarkupDocument.Parse(pp.GetBytes(), new OpenContext(s_limits), part);
                }
            }
        }
        catch (OfficeException)
        {
            // A typed rejection is the contract.
        }
    }

    private static void Edit(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            // Documented refusal: an unclosed field, a note that cannot be referenced.
        }
        catch (ArgumentException)
        {
            // Documented refusal: markup the edit cannot place or text XML cannot carry.
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        const string ns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        string doc = $"<w:document xmlns:w=\"{ns}\"><w:body><w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> TOC \\o \"1-3\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>x</w:t><w:footnoteReference w:id=\"1\"/></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r><w:commentRangeStart w:id=\"0\"/><w:r><w:t>y</w:t></w:r><w:commentRangeEnd w:id=\"0\"/></w:p></w:body></w:document>";
        string notes = $"<w:footnotes xmlns:w=\"{ns}\"><w:footnote w:id=\"1\"><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t>n</w:t></w:r></w:p></w:footnote></w:footnotes>";
        string comments = $"<w:comments xmlns:w=\"{ns}\"><w:comment w:id=\"0\" w:author=\"a\"><w:p><w:r><w:t>c</w:t></w:r></w:p></w:comment></w:comments>";
        File.WriteAllBytes(Path.Combine(directory, "hub"), Encoding.UTF8.GetBytes(doc + "\0" + notes + "\0" + comments));
        File.WriteAllBytes(Path.Combine(directory, "instr"), Encoding.UTF8.GetBytes(" TOC \\o \"1-3\" \\h \\* MERGEFORMAT"));
        return 0;
    }
}
