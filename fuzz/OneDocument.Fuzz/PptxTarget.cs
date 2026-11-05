using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The presentation hub over arbitrary bytes. The input is split at zero bytes into the presentation part, a slide, its layout, the
/// layout's master and a comments part, wired together the way a real deck is. Only <see cref="OfficeException"/> may come out of
/// reading; the edits (text, table cell, add, duplicate, move, delete) may refuse with an argument or state error, and every part
/// that parsed before an edit must still parse after it.
/// </summary>
internal static class PptxTarget
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Pml = "application/vnd.openxmlformats-officedocument.presentationml.";
    private static readonly OfficeLimits s_limits = new() { MaxXmlDepth = 64, MaxPartSize = 1 << 20, MaxRecursionDepth = 32 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        var pieces = new List<byte[]>();
        byte[] all = data.ToArray();
        int start = 0;
        for (int i = 0; i < all.Length && pieces.Count < 4; i++)
        {
            if (all[i] == 0)
            {
                pieces.Add(all[start..i]);
                start = i + 1;
            }
        }

        pieces.Add(all[start..]);
        while (pieces.Count < 5)
        {
            pieces.Add([]);
        }

        var p = new InMemoryPackage(s_limits);
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "ppt/presentation.xml")]);
        p.AddPart("/ppt/presentation.xml", Pml + "presentation.main+xml", pieces[0]);
        p.AddPart("/ppt/slides/slide1.xml", Pml + "slide+xml", pieces[1]);
        p.AddPart("/ppt/slideLayouts/slideLayout1.xml", Pml + "slideLayout+xml", pieces[2]);
        p.AddPart("/ppt/slideMasters/slideMaster1.xml", Pml + "slideMaster+xml", pieces[3]);
        p.AddPart("/ppt/comments/comment1.xml", Pml + "comments+xml", pieces[4]);
        p.SetRelationships("/ppt/presentation.xml",
        [
            new Relationship("rId1", Rel + "slide", "slides/slide1.xml"),
            new Relationship("rId2", Rel + "slideMaster", "slideMasters/slideMaster1.xml"),
        ]);
        p.SetRelationships("/ppt/slides/slide1.xml",
        [
            new Relationship("rId1", Rel + "slideLayout", "../slideLayouts/slideLayout1.xml"),
            new Relationship("rId2", Rel + "comments", "../comments/comment1.xml"),
        ]);
        p.SetRelationships("/ppt/slideLayouts/slideLayout1.xml", [new Relationship("rId1", Rel + "slideMaster", "../slideMasters/slideMaster1.xml")]);
        p.SetRelationships("/ppt/slideMasters/slideMaster1.xml", [new Relationship("rId1", Rel + "slideLayout", "../slideLayouts/slideLayout1.xml")]);

        var parsed = new HashSet<string>(StringComparer.Ordinal);
        foreach (IPackagePart part in p.Parts)
        {
            if (RecursiveAI.OneDocument.Markup.MarkupDocument.TryParse(part.GetBytes(), out _, null))
            {
                parsed.Add(part.Name);
            }
        }

        try
        {
            using var deck = Presentation.FromPackage(p, new OpenContext(s_limits));
            Read(deck);
            Edit(() => deck.Slides[0].Shapes[0].TextBody!.SetText("a\nb"));
            Edit(() => deck.Slides[0].Shapes[0].Table!.Rows[0].Cells[0].SetText("cell"));
            Edit(() => deck.Slides[0].SetNotesText("n"));
            Edit(() => deck.Slides[0].IsHidden = true);
            Edit(() => deck.DuplicateSlide(deck.Slides[0]));
            Edit(() => deck.MoveSlide(deck.Slides[^1], 0));
            Edit(() => deck.AddSlide(deck.Masters[0].Layouts[0]));
            Edit(() => deck.DeleteSlide(deck.Slides[0]));
            Read(deck);
            deck.CommitChanges();
            foreach (IPackagePart part in p.Parts)
            {
                // A part that parsed before and was written by an edit must still parse.
                if (parsed.Contains(part.Name) && !RecursiveAI.OneDocument.Markup.MarkupDocument.TryParse(part.GetBytes(), out _, null))
                {
                    throw new InvalidOperationException("an edited part does not parse: " + part.Name);
                }
            }
        }
        catch (OfficeException)
        {
        }
    }

    private static void Read(Presentation deck)
    {
        _ = deck.SlideSize;
        _ = deck.Sections;
        _ = deck.CustomShows;
        _ = deck.CommentAuthors;
        _ = deck.NotesMaster;
        foreach (SlideMaster master in deck.Masters)
        {
            _ = master.Theme;
            _ = master.HeaderFooter;
            _ = master.Layouts;
        }

        foreach (Slide slide in deck.Slides)
        {
            _ = slide.Id;
            _ = slide.Title;
            _ = slide.Text;
            _ = slide.IsHidden;
            _ = slide.HasTransition;
            _ = slide.HasAnimations;
            _ = slide.Comments;
            _ = slide.ColorMap;
            _ = slide.Notes?.NotesText;
            foreach (Shape shape in slide.AllShapes())
            {
                _ = shape.Id;
                _ = shape.Name;
                _ = shape.References;
                _ = shape.InheritanceChain;
                _ = shape.ResolveBounds();
                _ = shape.ResolveBodyFormat();
                if (shape.TextBody is { } body)
                {
                    foreach (TextParagraph para in body.Paragraphs)
                    {
                        _ = para.Text;
                        ParagraphFormat f = para.ResolveFormat();
                        foreach (TextRun run in para.Runs)
                        {
                            _ = para.ResolveRunFormat(run);
                            _ = slide.ResolveColor(f.Run.Color);
                        }
                    }
                }

                if (shape.Table is { } table)
                {
                    foreach (TableRow row in table.Rows)
                    {
                        foreach (TableCell cell in row.Cells)
                        {
                            _ = cell.Origin;
                            _ = cell.Text;
                        }
                    }
                }
            }
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
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(deck.Masters[0].Layouts[1]);
        slide.Shapes[0].TextBody!.SetText("Title");
        slide.Shapes[1].TextBody!.SetText("one\ntwo");
        deck.CommitChanges();
        string Part(string name) => Encoding.UTF8.GetString(deck.Package.FindPart(name)!.GetBytes().Span);
        string comments = "<p:cmLst xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"><p:cm authorId=\"0\" dt=\"2024-01-01T00:00:00.000\" idx=\"1\"><p:pos x=\"1\" y=\"2\"/><p:text>c</p:text></p:cm></p:cmLst>";
        string seed = Part("/ppt/presentation.xml") + "\0" + Part("/ppt/slides/slide1.xml") + "\0" + Part("/ppt/slideLayouts/slideLayout2.xml") + "\0" + Part("/ppt/slideMasters/slideMaster1.xml") + "\0" + comments;
        File.WriteAllBytes(Path.Combine(directory, "pptx"), Encoding.UTF8.GetBytes(seed));
        return 0;
    }
}
