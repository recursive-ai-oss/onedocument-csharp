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

        failures.AddRange(RunBinary());
        failures.AddRange(RunTemplate());
        return failures;
    }

    // Builds a one-slide PowerPoint 97-2003 file from records, opens it through the hub (record reader, conversion to PresentationML, typed views) and
    // reads the text back: under NativeAOT and WASM.
    private static List<string> RunBinary()
    {
        var failures = new List<string>();
        static byte[] Rec(int version, int instance, int type, params byte[][] parts)
        {
            int length = parts.Sum(p => p.Length);
            var bytes = new byte[8 + length];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(version | (instance << 4)));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)type);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)length);
            int at = 8;
            foreach (byte[] part in parts)
            {
                part.CopyTo(bytes, at);
                at += part.Length;
            }

            return bytes;
        }

        static byte[] U32(params uint[] values)
        {
            var bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
            }

            return bytes;
        }

        byte[] slideAtom = Rec(0, 0, 0x3EF, U32(1), new byte[8], U32(0x80000000, 0), new byte[4]);
        byte[] patriarch = Rec(0xF, 0, 0xF004, Rec(2, 0, 0xF00A, U32(1024, 5)), Rec(1, 0, 0xF009, U32(0, 0, 5760, 4320)));
        byte[] shape = Rec(0xF, 0, 0xF004, Rec(2, 202, 0xF00A, U32(1026, 0xA00)), Rec(0, 0, 0xF010, new byte[8]),
            Rec(0, 0, 0xF00D, Rec(0, 0, 0xF9F, U32(4)), Rec(0, 0, 0xFA0, System.Text.Encoding.Unicode.GetBytes("binary smoke"))));
        byte[] Drawing(params byte[][] shapes) =>
            Rec(0xF, 0, 0x40C, Rec(0xF, 1, 0xF002, Rec(0, 1, 0xF008, U32(2, 1025)), Rec(0xF, 0, 0xF003, [patriarch, .. shapes])));
        var stream = new List<byte>();
        int Add(byte[] record)
        {
            int at = stream.Count;
            stream.AddRange(record);
            return at;
        }

        int master = Add(Rec(0xF, 0, 0x3F8, slideAtom, Drawing()));
        int slide = Add(Rec(0xF, 0, 0x3EE, slideAtom, Drawing(shape)));
        int document = Add(Rec(0xF, 0, 0x3E8, Rec(0, 0, 0x3E9, U32(5760, 4320, 4320, 5760, 1, 2, 1, 2, 0, 0), new byte[8]),
            Rec(0xF, 1, 0xFF0, Rec(0, 0, 0x3F3, U32(2, 0, 0, 0x80000000, 0))), Rec(0xF, 0, 0xFF0, Rec(0, 0, 0x3F3, U32(3, 0, 0, 256, 0)))));
        int directory = Add(Rec(0, 0, 0x1772, U32(1u | (3u << 20), (uint)document, (uint)master, (uint)slide)));
        int edit = Add(Rec(0, 0, 0xFF5, U32(0, 0, 0, (uint)directory, 1, 4), new byte[4]));
        using var package = RecursiveAI.OneDocument.Cfb.CfbPackage.Create();
        package.AddPart("/PowerPoint Document", "", stream.ToArray());
        package.AddPart("/Current User", "", Rec(0, 0, 0xFF6, U32(0x14, 0xE391C05F, (uint)edit), new byte[12]));
        using var file = new MemoryStream();
        package.WriteTo(file);
        file.Position = 0;
        using Presentation legacy = Presentation.Open(file);
        if (legacy.ConvertedFrom != OfficeFormat.Ppt || legacy.Slides.Count != 1 || !legacy.Slides[0].Text.Contains("binary smoke", StringComparison.Ordinal))
        {
            failures.Add("presentation binary");
        }

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
