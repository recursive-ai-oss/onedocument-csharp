using System.Linq;
using RecursiveAI.OneDocument.Presentations;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Creates a presentation, adds, edits, duplicates, moves and deletes slides, saves to memory and reads it back: under NativeAOT and WASM.</summary>
internal static class PresentationSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        using var first = new MemoryStream();
        using (Presentation created = Presentation.Create())
        {
            Slide slide = created.AddSlide(created.Masters[0].Layouts[1]);
            slide.Shapes[0].TextBody!.SetText("hello");
            slide.Shapes[1].TextBody!.SetText("one\ntwo");
            created.DuplicateSlide(slide);
            created.AddSlide(created.Masters[0].Layouts[2], 0);
            created.MoveSlide(created.Slides[0], 2);
            created.SaveAs(first, OfficeFormat.Pptx);
        }

        first.Position = 0;
        using var second = new MemoryStream();
        using (Presentation opened = Presentation.Open(first))
        {
            if (opened.Format != OfficeFormat.Pptx || opened.Slides.Count != 3 || opened.Slides[0].Title != "hello")
            {
                failures.Add("presentation open");
            }

            ParagraphFormat f = opened.Slides[0].Shapes[1].TextBody!.Paragraphs[0].ResolveFormat();
            if (f.Bullet.Kind != BulletKind.Character || f.Run.Size != 2800)
            {
                failures.Add("presentation inheritance");
            }

            opened.Slides[1].Shapes[0].TextBody!.SetText("edited & saved");
            opened.DeleteSlide(opened.Slides[2]);
            opened.SaveAs(second, OfficeFormat.Pptx);
        }

        second.Position = 0;
        using Presentation again = Presentation.Open(second);
        if (again.Slides.Count != 2 || again.Slides[1].Title != "edited & saved" || !again.ExtractText().Contains("edited & saved", StringComparison.Ordinal))
        {
            failures.Add("presentation edit");
        }

        return failures;
    }
}
