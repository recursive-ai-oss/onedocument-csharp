using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The word-processing style, numbering, section and table readers over arbitrary parts. The input is split at zero bytes into the document part,
/// the style sheet, the numbering part and a header part, wired as a real file is (the document refers to the header as <c>rId3</c>). Every style is
/// resolved, every paragraph and run resolved with its list label, every section read with its header and footer choices, and every table built
/// as a logical grid with its cell properties resolved. Only <see cref="OfficeException"/> may escape.
/// </summary>
internal static class DocPartsTarget
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Wml = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
    private static readonly OfficeLimits Limits = new() { MaxXmlDepth = 64, MaxPartSize = 1 << 20, MaxRecursionDepth = 32, MaxAllocationBytes = 1 << 26 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        List<byte[]> pieces = Split(data, 4);
        var p = new InMemoryPackage(Limits);
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml")]);
        p.AddPart("/word/document.xml", Wml + "document.main+xml", pieces[0]);
        p.AddPart("/word/styles.xml", Wml + "styles+xml", pieces[1]);
        p.AddPart("/word/numbering.xml", Wml + "numbering+xml", pieces[2]);
        p.AddPart("/word/header1.xml", Wml + "header+xml", pieces[3]);
        p.SetRelationships("/word/document.xml",
        [
            new Relationship("rId1", Rel + "styles", "styles.xml"),
            new Relationship("rId2", Rel + "numbering", "numbering.xml"),
            new Relationship("rId3", Rel + "header", "header1.xml"),
        ]);
        try
        {
            using var d = Document.FromPackage(p, new OpenContext(Limits));
            long sum = 0;
            foreach (StyleDefinition s in d.Styles.Styles)
            {
                sum += d.Styles.ResolveStyle(s, false).Properties.Count + d.Styles.ResolveStyle(s, true).Properties.Count + s.Lineage.Count + s.TableRegions.Count;
            }

            foreach (AbstractNumberingDefinition a in d.Numbering.AbstractDefinitions)
            {
                sum += a.Levels.Count;
            }

            foreach (NumberingInstance i in d.Numbering.Instances)
            {
                for (int level = 0; level < 9; level++)
                {
                    sum += d.Numbering.GetLevel(i.Id, level)?.Text.Length ?? 0;
                }
            }

            sum += d.Numbering.LabelBody().Count;
            foreach (Paragraph para in d.Paragraphs)
            {
                sum += para.ResolvedProperties.Properties.Count + (para.Numbering is null ? 0 : 1);
                foreach (Run r in para.Runs())
                {
                    sum += r.ResolvedProperties.Properties.Count;
                }
            }

            long page = 0;
            foreach (Section s in d.Sections)
            {
                sum += s.PageSize.Width + s.Margins.Top + s.Columns.Count + (s.PageNumbering.Start ?? 0) + (s.LineNumbering is null ? 0 : 1) + (s.DocumentGrid is null ? 0 : 1);
                _ = s.BreakType;
                _ = s.HeaderFooterReferences();
                page = s.FirstPrintedPageNumber(page);
                _ = s.ForPage(HeaderFooterKind.Header, true, page);
                _ = s.Resolve(HeaderFooterKind.Footer, HeaderFooterType.Default);
            }

            foreach (Table t in d.Tables)
            {
                LogicalTable model = t.Logical();
                foreach (LogicalCell cell in model.Cells)
                {
                    sum += d.Styles.ResolveTableCell(t, model, cell).Properties.Count + d.Styles.GetTableRegions(t, model, cell).Count;
                }
            }

            if (sum == long.MinValue)
            {
                throw new InvalidOperationException();
            }
        }
        catch (OfficeException)
        {
        }
    }

    internal static List<byte[]> Split(ReadOnlySpan<byte> data, int count)
    {
        var pieces = new List<byte[]>();
        byte[] all = data.ToArray();
        int start = 0;
        for (int i = 0; i < all.Length && pieces.Count < count - 1; i++)
        {
            if (all[i] == 0)
            {
                pieces.Add(all[start..i]);
                start = i + 1;
            }
        }

        pieces.Add(all[start..]);
        while (pieces.Count < count)
        {
            pieces.Add([]);
        }

        return pieces;
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
        string doc = "<w:document " + W + "><w:body><w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:rPr><w:rStyle w:val=\"Strong\"/><w:b/></w:rPr><w:t>One</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"1\"/></w:numPr><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rId3\"/><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/><w:cols w:num=\"2\" w:space=\"720\"/><w:pgNumType w:start=\"3\"/><w:lnNumType w:countBy=\"1\"/><w:titlePg/></w:sectPr></w:pPr><w:r><w:t>Two</w:t></w:r></w:p>" +
            "<w:tbl><w:tblPr><w:tblStyle w:val=\"Grid\"/><w:tblLook w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:noHBand=\"0\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
            "<w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"2\"/></w:tcPr><w:p/></w:tc></w:tr><w:tr><w:tc><w:tcPr><w:vMerge w:val=\"restart\"/></w:tcPr><w:p/></w:tc><w:tc><w:p/></w:tc></w:tr><w:tr><w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc><w:tc><w:p/></w:tc></w:tr></w:tbl>" +
            "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rId3\"/><w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/></w:sectPr></w:body></w:document>";
        string styles = "<w:styles " + W + "><w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault></w:docDefaults><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:numPr><w:numId w:val=\"1\"/></w:numPr><w:spacing w:before=\"240\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"Strong\"><w:name w:val=\"Strong\"/><w:rPr><w:b/></w:rPr></w:style>" +
            "<w:style w:type=\"table\" w:styleId=\"Grid\"><w:name w:val=\"Table Grid\"/><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/></w:tblBorders></w:tblPr><w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/></w:rPr><w:tcPr><w:shd w:val=\"clear\" w:fill=\"DDDDDD\"/></w:tcPr></w:tblStylePr><w:tblStylePr w:type=\"band1Horz\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"EEEEEE\"/></w:tcPr></w:tblStylePr></w:style></w:styles>";
        string numbering = "<w:numbering " + W + "><w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:pStyle w:val=\"Heading1\"/></w:lvl><w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerLetter\"/><w:lvlText w:val=\"%1.%2)\"/><w:lvlRestart w:val=\"1\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"5\"/></w:lvlOverride></w:num></w:numbering>";
        string header = "<w:hdr " + W + "><w:p><w:r><w:t>Head</w:t></w:r></w:p></w:hdr>";
        File.WriteAllBytes(Path.Combine(dir, "seed0"), Encoding.UTF8.GetBytes(doc + "\0" + styles + "\0" + numbering + "\0" + header));
        File.WriteAllBytes(Path.Combine(dir, "seed1"), Encoding.UTF8.GetBytes(doc + "\0\0\0"));
        return 0;
    }
}
