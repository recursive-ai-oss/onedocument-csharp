using System;
using System.Collections.Generic;
using System.IO;
using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Redaction;
using RecursiveAI.OneDocument.Rendering.Tests;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Inspection, sanitising, redaction and the accessibility check over a hostile part of an otherwise sound file. The input is a kind byte, a part
/// index and the bytes that replace that part of a generated word-processing, presentation or workbook file (every part of the file can be hit,
/// relationships and properties included). The damaged file is inspected, sanitised, redacted by match and by region, and checked for
/// accessibility, each through the public entry points. Only <see cref="OfficeException"/> may escape; a redaction that reports success must
/// verify.
/// </summary>
internal static class OperationsTarget
{
    private static readonly OfficeLimits Limits = new() { MaxXmlDepth = 64, MaxPartSize = 1 << 20, MaxRecursionDepth = 32, MaxAllocationBytes = 1 << 26, MaxTotalUncompressedSize = 16L << 20 };

    private static readonly byte[][] Bases = BuildBases();

    private static byte[][] BuildBases()
    {
        TestPptx.Deck deck = LayoutTargets.SampleDeck("Alice Smith 555-0100");
        return
        [
            TestDocx.Bytes("<w:p><w:r><w:t>Alice Smith 555-0100</w:t></w:r></w:p><w:p><w:r><w:rPr><w:vanish/></w:rPr><w:t>hidden</w:t></w:r></w:p>" +
                "<w:p><w:ins w:id=\"1\" w:author=\"Bob\" w:date=\"2020-01-01T00:00:00Z\"><w:r><w:t>added</w:t></w:r></w:ins></w:p>", TestDocx.DefaultStyles),
            TestPptx.Build(deck),
            Workbook(),
        ];
    }

    private static byte[] Workbook()
    {
        const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        using OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", R + "officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            System.Text.Encoding.UTF8.GetBytes("<workbook xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"));
        p.SetRelationships("/xl/workbook.xml", [new Relationship("rId1", R + "worksheet", "worksheets/sheet1.xml")]);
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
            System.Text.Encoding.UTF8.GetBytes("<worksheet xmlns=\"" + Main + "\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Alice Smith 555-0100</t></is></c><c r=\"B1\"><f>1+1</f><v>2</v></c></row></sheetData></worksheet>"));
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        return ms.ToArray();
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        DocumentFormats.Register(FormatRegistry.Default);
        SpreadsheetFormats.Register(FormatRegistry.Default);
        PresentationFormats.Register(FormatRegistry.Default);
        byte[] file = Bases[data[0] % Bases.Length];
        byte[] damaged;
        try
        {
            using OpcPackage p = OpcPackage.Open(new MemoryStream(file), new OpenContext(Limits), leaveOpen: false);
            IReadOnlyList<IPackagePart> parts = p.Parts;
            parts[data[1] % parts.Count].SetBytes(data[2..].ToArray());
            using var ms = new MemoryStream();
            p.WriteTo(ms);
            damaged = ms.ToArray();
        }
        catch (OfficeException)
        {
            return;
        }

        try
        {
            _ = Inspector.Inspect(new MemoryStream(damaged), new OpenContext(Limits));
            using var clean = new MemoryStream();
            _ = Sanitizer.Sanitize(new MemoryStream(damaged), clean, null, new OpenContext(Limits));
            using OfficeFile f = Office.Open(ByteSource.FromMemory(damaged), new OpenContext(Limits));
            _ = f.CheckAccessibility();
            var plan = new RedactionPlan();
            plan.Matches.Add(RedactionMatch.Literal("Alice"));
            plan.Matches.Add(RedactionMatch.Pattern("[0-9]{3}-[0-9]{4}"));
            plan.Regions.Add(new ParagraphRegion(0, 0));
            plan.Regions.Add(new SlideRegion(1));
            plan.Regions.Add(new CellRangeRegion("Sheet1", "A1:B2"));
            using var redacted = new MemoryStream();
            _ = f.Redact(plan, redacted);
        }
        catch (OfficeException)
        {
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        for (int kind = 0; kind < Bases.Length; kind++)
        {
            using OpcPackage p = OpcPackage.Open(new MemoryStream(Bases[kind]), leaveOpen: false);
            IReadOnlyList<IPackagePart> parts = p.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Length < 20_000)
                {
                    File.WriteAllBytes(Path.Combine(dir, $"seed-{kind}-{i}"), [(byte)kind, (byte)i, .. parts[i].GetBytes().ToArray()]);
                }
            }
        }

        return 0;
    }
}
