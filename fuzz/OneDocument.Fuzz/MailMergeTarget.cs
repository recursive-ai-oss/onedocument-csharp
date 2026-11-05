using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.MailMerge;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Mail merge. The input is a field instruction (the text after <c>MERGEFIELD</c>, <c>IF</c>, <c>NEXTIF</c> and so on is part of it, so
/// the first line names the field), placed in a simple field and in a complex field of a two-paragraph document and merged against three
/// records in both modes. Only typed library errors may come out, and the merge twice gives the same text.
/// </summary>
internal static class MailMergeTarget
{
    private const string Head = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private static readonly object[] Records =
    [
        new Dictionary<string, object?> { ["Name"] = "Ann", ["Qty"] = 3, ["Balance"] = -12.5, ["When"] = new DateTime(2026, 11, 5, 14, 30, 0, DateTimeKind.Utc) },
        new Dictionary<string, object?> { ["Name"] = "", ["Qty"] = 0, ["Balance"] = 1e300 },
        new Dictionary<string, object?> { ["Name"] = "B & <C>", ["Qty"] = 222492, ["Balance"] = 0.125 },
    ];

    public static void Run(ReadOnlySpan<byte> data)
    {
        string instruction = SecurityElement.Escape(Encoding.UTF8.GetString(data).Replace("\0", string.Empty)) ?? string.Empty;
        string body = "<w:p><w:fldSimple w:instr=\"" + instruction + "\"><w:r><w:t>x</w:t></w:r></w:fldSimple></w:p>"
            + "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> " + instruction
            + " </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>y</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
        var p = new InMemoryPackage();
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml")]);
        p.AddPart("/word/document.xml", "application/xml", Encoding.UTF8.GetBytes(Head + body + "</w:body></w:document>"));

        Document doc;
        try
        {
            doc = Document.FromPackage(p);
        }
        catch (OfficeException)
        {
            return;
        }

        using (doc)
        {
            foreach (MailMergeMode mode in new[] { MailMergeMode.PerRecord, MailMergeMode.SingleDocument })
            {
                string first = Merge(doc, mode);
                if (first != Merge(doc, mode))
                {
                    throw new InvalidOperationException("a mail merge must be deterministic");
                }
            }
        }
    }

    private static string Merge(Document doc, MailMergeMode mode)
    {
        try
        {
            using MailMergeResult result = doc.MailMerge(Records, new MailMergeOptions { Mode = mode, MaxRecords = 10 });
            var sb = new StringBuilder();
            foreach (Document d in result.Documents)
            {
                sb.Append(d.ExtractText()).Append('\u0001');
            }

            return sb.ToString();
        }
        catch (OfficeException ex)
        {
            return ex.GetType().Name;
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        string[] seeds =
        [
            "MERGEFIELD Name \\b \"Dear \" \\f \",\" \\* Upper",
            "MERGEFIELD Balance \\# \"$#,##0.00;($#,##0.00);'none'\"",
            "MERGEFIELD Qty \\# x##",
            "MERGEFIELD When \\@ \"dddd, MMMM d, yyyy h:mm AM/PM\"",
            "IF { MERGEFIELD Qty } > 5 \"many\" \"few\"",
            "NEXTIF Qty > 1",
            "SKIPIF Name = \"\"",
            "SET v \"a\" REF v MERGEREC MERGESEQ",
        ];
        for (int i = 0; i < seeds.Length; i++)
        {
            File.WriteAllText(Path.Combine(dir, "seed-mm-" + i), seeds[i]);
        }

        return 0;
    }
}
