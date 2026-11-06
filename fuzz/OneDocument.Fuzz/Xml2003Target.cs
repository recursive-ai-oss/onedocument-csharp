using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The Office 2003 XML readers. The input is a whole file: it is opened as Word 2003 XML or Excel 2003 XML (by what the sniffer says), saved as
/// DOCX or XLSX, opened again and its text extracted. Only a typed <see cref="OfficeException"/> may escape.
/// </summary>
internal static class Xml2003Target
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        DocumentFormats.Register(FormatRegistry.Default);
        SpreadsheetFormats.Register(FormatRegistry.Default);
        byte[] bytes = data.ToArray();
        try
        {
            using IByteSource sniff = ByteSource.FromMemory(bytes);
            OfficeFormat format = FormatSniffer.Detect(sniff, OfficeLimits.Default);
            if (format == OfficeFormat.Word2003Xml)
            {
                using Document doc = WordMl2003.Open(ByteSource.FromMemory(bytes));
                using var ms = new MemoryStream();
                doc.SaveAs(ms, OfficeFormat.Docx);
                ms.Position = 0;
                using Document again = Document.Open(ms);
                _ = again.ExtractText();
            }
            else if (format == OfficeFormat.Excel2003Xml)
            {
                using Workbook wb = SpreadsheetMl2003.Open(ByteSource.FromMemory(bytes));
                using var ms = new MemoryStream();
                wb.SaveAs(ms, OfficeFormat.Xlsx);
                ms.Position = 0;
                using Workbook again = Workbook.Open(ms);
                _ = again.ExtractText();
            }
        }
        catch (OfficeException)
        {
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        const string word =
            "<?xml version=\"1.0\"?><?mso-application progid=\"Word.Document\"?><w:wordDocument xmlns:w=\"http://schemas.microsoft.com/office/word/2003/wordml\" " +
            "xmlns:wx=\"http://schemas.microsoft.com/office/word/2003/auxHint\" xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
            "xmlns:aml=\"http://schemas.microsoft.com/aml/2001/core\">";
        string[] seeds =
        [
            word + "<w:body><wx:sect><wx:sub-section><w:p><w:pPr><w:jc w:val=\"center\"/><w:spacing w:before=\"120\" w:line-rule=\"exact\"/></w:pPr><w:r><w:rPr><w:b/><w:rFonts w:h-ansi=\"Arial\"/></w:rPr><w:t>Hi</w:t></w:r></w:p>" +
            "<w:sectPr><w:hdr w:type=\"odd\"><w:p/></w:hdr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1\"/></w:sectPr></wx:sub-section></wx:sect></w:body></w:wordDocument>",
            word + "<w:lists><w:listDef w:listDefId=\"0\"><w:lvl w:ilvl=\"0\"><w:nfc w:val=\"4\"/><w:lvlText w:val=\"%1)\"/></w:lvl></w:listDef><w:list w:ilfo=\"1\"><w:ilst w:val=\"0\"/></w:list></w:lists>" +
            "<w:styles><w:style w:type=\"paragraph\" w:styleId=\"A\"><w:name w:val=\"A\"/><w:basedOn w:val=\"B\"/></w:style></w:styles><w:body><w:p><w:pPr><w:listPr><w:ilvl w:val=\"0\"/><w:ilfo w:val=\"1\"/></w:listPr></w:pPr>" +
            "<w:hlink w:dest=\"http://x/\"><w:r><w:t>l</w:t></w:r></w:hlink><w:r><w:footnote><w:p><w:r><w:t>n</w:t></w:r></w:p></w:footnote></w:r>" +
            "<aml:annotation aml:id=\"1\" w:type=\"Word.Comment\"><aml:content><w:p/></aml:content></aml:annotation>" +
            "<aml:annotation aml:id=\"2\" w:type=\"Word.Insertion\"><aml:content><w:r><w:t>i</w:t></w:r></aml:content></aml:annotation></w:p>" +
            "<w:tbl><w:tr><w:tc><w:tcPr><w:hmerge w:val=\"restart\"/></w:tcPr></w:tc><w:tc><w:tcPr><w:hmerge w:val=\"continue\"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl></w:body></w:wordDocument>",
            word + "<w:body><w:p><w:r><w:pict><w:binData w:name=\"wordml://1.png\">iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==</w:binData>" +
            "<v:shape style=\"width:10pt;height:5pt\"><v:imagedata src=\"wordml://1.png\"/></v:shape></w:pict></w:r></w:p></w:body></w:wordDocument>",
        ];
        const string sheet =
            "<?xml version=\"1.0\"?><?mso-application progid=\"Excel.Sheet\"?><Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\" " +
            "xmlns:x=\"urn:schemas-microsoft-com:office:excel\">";
        string[] books =
        [
            sheet + "<Styles><Style ss:ID=\"Default\"/><Style ss:ID=\"s1\" ss:Parent=\"Default\"><Font ss:Bold=\"1\"/><NumberFormat ss:Format=\"Percent\"/></Style></Styles>" +
            "<Worksheet ss:Name=\"S\"><Table><Row><Cell ss:StyleID=\"s1\"><Data ss:Type=\"Number\">1</Data></Cell><Cell ss:Index=\"3\" ss:Formula=\"=RC[-2]+R[1]C\"><Data ss:Type=\"Number\">2</Data></Cell></Row></Table></Worksheet></Workbook>",
            sheet + "<Names><NamedRange ss:Name=\"n\" ss:RefersTo=\"=S!R1C1:R2C2\"/></Names><Worksheet ss:Name=\"S\"><Table><Row><Cell ss:MergeAcross=\"1\" ss:MergeDown=\"1\" ss:HRef=\"http://x/\"><Data ss:Type=\"DateTime\">2024-01-02T03:04:05.000</Data>" +
            "<Comment ss:Author=\"a\"><ss:Data>c</ss:Data></Comment></Cell></Row></Table><ConditionalFormatting xmlns=\"urn:schemas-microsoft-com:office:excel\"><Range>R1C1:R2C2</Range><Condition><Qualifier>Greater</Qualifier><Value1>1</Value1>" +
            "<Format Style=\"color:red\"/></Condition></ConditionalFormatting><DataValidation xmlns=\"urn:schemas-microsoft-com:office:excel\"><Range>R1C1</Range><Type>List</Type><Value>&quot;a,b&quot;</Value></DataValidation></Worksheet></Workbook>",
            sheet + "<Worksheet ss:Name=\"S\"><Table><Row ss:Span=\"2\"><Cell><ss:Data ss:Type=\"String\" xmlns=\"http://www.w3.org/TR/REC-html40\">a <B>b</B><Sup>c</Sup></ss:Data></Cell><Cell><Data ss:Type=\"Error\">#N/A</Data></Cell>" +
            "<Cell ss:ArrayRange=\"R1C3:R2C3\" ss:Formula=\"=SUM(R[0]C[-3]:R[1]C[-1])\"><Data ss:Type=\"Boolean\">1</Data></Cell></Row></Table><x:WorksheetOptions><x:FreezePanes/><x:SplitHorizontal>1</x:SplitHorizontal></x:WorksheetOptions></Worksheet></Workbook>",
        ];
        int i = 0;
        foreach (string seed in seeds)
        {
            File.WriteAllText(Path.Combine(directory, "word-" + i++), seed, new UTF8Encoding(false));
        }

        i = 0;
        foreach (string seed in books)
        {
            File.WriteAllText(Path.Combine(directory, "sheet-" + i++), seed, new UTF8Encoding(false));
        }

        return 0;
    }
}
