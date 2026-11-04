using System;
using System.IO;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents.Revisions;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// The revision model over arbitrary XML. Any input that parses is scanned, read in both views, accepted in full, rejected in full,
/// and edited through the authoring calls with positions taken from the input; only OfficeException may come out of resolving
/// (authoring may refuse a bad argument with ArgumentException or InvalidOperationException, which is its contract).
/// </summary>
internal static class RevisionTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        byte[] input = data.ToArray();
        if (!MarkupDocument.TryParse(input, out MarkupDocument? probe, null) || probe is null)
        {
            return;
        }

        int a = input.Length > 0 ? input[0] : 0;
        int b = input.Length > 1 ? input[^1] : 0;

        RevisionSet viewed = Scan(input, out _);
        _ = viewed.Census();
        foreach (IRevisionPart p in viewed.Parts)
        {
            _ = RevisionSet.GetText(p, RevisionView.Accepted);
            _ = RevisionSet.GetText(p, RevisionView.Rejected);
        }

        foreach (bool accept in new[] { true, false })
        {
            RevisionSet set = Scan(input, out MarkupDocument doc);
            _ = accept ? set.AcceptAll() : set.RejectAll();
            // The result must still be a well-formed part.
            if (!MarkupDocument.TryParse(doc.ToArray(), out _, null))
            {
                throw new InvalidOperationException("resolving revisions produced a part that does not parse");
            }
        }

        // Single revisions, chosen from the input.
        RevisionSet one = Scan(input, out MarkupDocument oneDoc);
        if (one.Revisions.Count > 0)
        {
            Revision r = one.Revisions[a % one.Revisions.Count];
            _ = (b & 1) == 0 ? one.Accept(r) : one.Reject(r);
            _ = MarkupDocument.Parse(oneDoc.ToArray());
        }

        // Authoring against the first paragraph.
        RevisionSet edit = Scan(input, out MarkupDocument editDoc);
        MarkupNode p0 = FirstParagraph(editDoc.Root);
        if (p0.IsNull)
        {
            return;
        }

        RevisionAuthor au = edit.CreateAuthor("fuzz", null);
        try
        {
            int len = RevisionSet.GetParagraphText(p0, RevisionView.Accepted).Length;
            int off = len == 0 ? 0 : a % (len + 1);
            int n = len == 0 ? 0 : b % (len - off + 1);
            switch ((a ^ b) % 7)
            {
                case 0: au.InsertText(p0, off, "x\ty"); break;
                case 1: au.DeleteText(p0, off, n); break;
                case 2: au.ReplaceText(p0, off, n, "z"); break;
                case 3: au.FormatText(p0, off, n, "<w:b/>"); break;
                case 4: au.InsertParagraph(p0, "p"); break;
                case 5: au.DeleteParagraph(p0); break;
                default: au.MoveText(p0, off, Math.Max(1, n), p0, 0); break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A refused edit is the documented outcome for markup the edit cannot work with.
        }

        edit.AcceptAll();
        _ = MarkupDocument.Parse(editDoc.ToArray());
    }

    private static RevisionSet Scan(byte[] input, out MarkupDocument doc)
    {
        doc = MarkupDocument.Parse(input);
        return RevisionSet.Scan([new RevisionPart("/word/document.xml", doc)], new OpenContext(defects: new DefectCollection(64)));
    }

    private static MarkupNode FirstParagraph(MarkupNode root)
    {
        for (MarkupNode n = root; !n.IsNull;)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == "p")
            {
                return n;
            }

            if (!n.FirstChild.IsNull)
            {
                n = n.FirstChild;
                continue;
            }

            MarkupNode c = n;
            n = default;
            for (; !c.IsNull && c != root; c = c.Parent)
            {
                if (!c.NextSibling.IsNull)
                {
                    n = c.NextSibling;
                    break;
                }
            }
        }

        return default;
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        const string head = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>";
        string[] bodies =
        [
            "<w:p><w:r><w:t>Plain </w:t></w:r><w:ins w:id=\"1\" w:author=\"Ann\"><w:r><w:t>added</w:t></w:r></w:ins><w:del w:id=\"2\" w:author=\"Bob\"><w:r><w:delText>gone</w:delText></w:r></w:del></w:p>",
            "<w:p><w:pPr><w:rPr><w:del w:id=\"1\" w:author=\"A\"/></w:rPr></w:pPr><w:r><w:t>one</w:t></w:r></w:p><w:p><w:r><w:t>two</w:t></w:r></w:p>",
            "<w:p><w:pPr><w:jc w:val=\"center\"/><w:pPrChange w:id=\"4\" w:author=\"B\"><w:pPr><w:jc w:val=\"left\"/></w:pPr></w:pPrChange></w:pPr><w:r><w:rPr><w:b/><w:rPrChange w:id=\"5\" w:author=\"B\"><w:rPr/></w:rPrChange></w:rPr><w:t>b</w:t></w:r></w:p>",
            "<w:p><w:moveFromRangeStart w:id=\"6\" w:name=\"m\"/><w:moveFrom w:id=\"7\" w:author=\"A\"><w:r><w:delText>m</w:delText></w:r></w:moveFrom><w:moveFromRangeEnd w:id=\"6\"/></w:p>",
            "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1\"/><w:tblGridChange w:id=\"1\" w:author=\"A\"><w:tblGrid/></w:tblGridChange></w:tblGrid><w:tr><w:trPr><w:ins w:id=\"8\" w:author=\"A\"/></w:trPr><w:tc><w:tcPr><w:cellMerge w:id=\"9\" w:author=\"A\" w:vMerge=\"cont\"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>",
        ];
        for (int i = 0; i < bodies.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(directory, "rev" + i), Encoding.UTF8.GetBytes(head + bodies[i] + "</w:body></w:document>"));
        }

        return 0;
    }
}
