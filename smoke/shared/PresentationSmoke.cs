using System.Linq;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Presentations.Templating;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>A template model bound by the source generator.</summary>
[TemplateModel]
internal sealed partial class SmokeDeckData
{
    public string Number { get; init; } = string.Empty;

    public List<SmokeDeckLine> Lines { get; init; } = [];
}

[TemplateModel]
internal sealed partial record SmokeDeckLine(string Description, int Qty);

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

        failures.AddRange(RunTemplate());
        return failures;
    }

    // Renders a presentation template against a generated model: a paragraph loop, then a slide per line, under NativeAOT and WASM.
    private static List<string> RunTemplate()
    {
        var failures = new List<string>();
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(deck.Masters[0].Layouts[1]);
        slide.Shapes[0].TextBody!.SetText("Invoice {{ number }}");
        slide.Shapes[1].TextBody!.SetText("{% for l in lines %}\n{{ l.description }} x {{ l.qty }}\n{% endfor %}");
        Slide each = deck.AddSlide(deck.Masters[0].Layouts[0]);
        each.Shapes[0].TextBody!.SetText("{% for l in lines scope=slide %}{{ l.description }}{% endfor %}");
        var data = new SmokeDeckData { Number = "7", Lines = [new SmokeDeckLine("Pen", 2), new SmokeDeckLine("Ink", 1)] };
        PresentationTemplateResult result = PresentationTemplate.Render(deck, data);
        if (result.SlideCount != 3 || deck.Slides[0].Title != "Invoice 7" || deck.Slides[1].Title != "Pen" || deck.Slides[2].Title != "Ink")
        {
            failures.Add("presentation template slides");
        }

        if (deck.Slides[0].Shapes[1].TextBody!.Text != "Pen x 2\nInk x 1")
        {
            failures.Add("presentation template paragraphs");
        }

        return failures;
    }
}
