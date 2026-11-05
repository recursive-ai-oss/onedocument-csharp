using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.ContentControls;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Tools.DocxProbe;

/// <summary>
/// Content-control commands. <c>sdt-census FILE...</c> prints one JSON line per file with the controls by type and level, how many are
/// bound and how the bindings resolve. <c>sdt-edit IN OUT</c> sets the value of every control that takes one and writes a new zip.
/// <c>sdt-generate DIR</c> writes one small document with a control of every type and a bound custom XML part.
/// </summary>
internal static partial class Program
{
    private static int SdtCensus(string[] files)
    {
        int failures = 0;
        foreach (string file in files)
        {
            var defects = new DefectCollection();
            try
            {
                using InMemoryPackage package = Read(file);
                using Document doc = Document.FromPackage(package, new OpenContext(defects: defects));
                var types = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var levels = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var bindings = new SortedDictionary<string, int>(StringComparer.Ordinal);
                int total = 0, bound = 0, unbound = 0, placeholders = 0;
                foreach (ContentControl c in doc.ContentControls())
                {
                    total++;
                    Bump(types, c.Type.ToString());
                    Bump(levels, c.Level.ToString());
                    if (c.IsShowingPlaceholder)
                    {
                        placeholders++;
                    }

                    if (c.Binding is null)
                    {
                        unbound++;
                    }
                    else
                    {
                        bound++;
                        Bump(bindings, c.EvaluateBinding().Status.ToString());
                    }
                }

                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["file"] = Path.GetFileName(file), ["ok"] = true, ["controls"] = total, ["bound"] = bound, ["unbound"] = unbound,
                    ["placeholders"] = placeholders, ["customXmlParts"] = doc.CustomXmlParts.Count, ["types"] = types, ["levels"] = levels,
                    ["bindingStatus"] = bindings, ["defects"] = defects.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Count()),
                }));
            }
            catch (Exception ex) when (ex is OfficeException or InvalidDataException or IOException)
            {
                failures++;
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["file"] = Path.GetFileName(file), ["ok"] = false, ["error"] = ex is OfficeException oe ? oe.Defect.Code : "container", ["message"] = ex.Message,
                }));
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static void Bump(SortedDictionary<string, int> map, string key) => map[key] = map.GetValueOrDefault(key) + 1;

    private static int SdtEdit(string input, string output)
    {
        using InMemoryPackage package = Read(input);
        Dictionary<string, byte[]> before = package.Parts.ToDictionary(p => p.Name, p => p.GetBytes().ToArray(), StringComparer.OrdinalIgnoreCase);
        using Document doc = Document.FromPackage(package);
        int edited = 0;
        foreach (ContentControl c in doc.ContentControls().ToList())
        {
            try
            {
                switch (c.Type)
                {
                    case ContentControlType.Checkbox:
                        c.SetChecked(c.IsChecked != true);
                        edited++;
                        break;
                    case ContentControlType.DropDownList or ContentControlType.ComboBox when c.ListItems.Count > 0:
                        c.SelectItem(c.ListItems.Count - 1);
                        edited++;
                        break;
                    case ContentControlType.RichText or ContentControlType.PlainText or ContentControlType.Date or ContentControlType.Citation
                        when c.Level is ContentControlLevel.Block or ContentControlLevel.Inline && c.Children().Any() == false:
                        c.SetText("Edited & <value>");
                        edited++;
                        break;
                }
            }
            catch (InvalidOperationException)
            {
                // A control whose type or place has no settable value is left alone.
            }
        }

        doc.CommitChanges();
        using var source = new ZipArchive(File.OpenRead(input), ZipArchiveMode.Read);
        using FileStream fs = File.Create(output);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (ZipArchiveEntry e in source.Entries)
        {
            ZipArchiveEntry copy = zip.CreateEntry(e.FullName, CompressionLevel.Optimal);
            copy.LastWriteTime = e.LastWriteTime;
            using Stream o = copy.Open();
            if (package.FindPart("/" + e.FullName) is { } p && before.TryGetValue(p.Name, out byte[]? old) && !p.GetBytes().Span.SequenceEqual(old))
            {
                o.Write(p.GetBytes().Span);
            }
            else
            {
                o.Write(Bytes(e));
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["file"] = Path.GetFileName(input), ["edited"] = edited }));
        return 0;
    }

    private static void SdtGenerate(string dir)
    {
        Directory.CreateDirectory(dir);
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
        const string W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
        const string Store = "{11111111-2222-3333-4444-555555555555}";
        string body =
            "<w:sdt><w:sdtPr><w:alias w:val=\"Title\"/><w:tag w:val=\"title\"/><w:id w:val=\"101\"/><w:lock w:val=\"sdtLocked\"/><w:showingPlcHdr/><w:text/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Click here</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:p><w:r><w:t xml:space=\"preserve\">Name: </w:t></w:r><w:sdt><w:sdtPr><w:rPr><w:b/></w:rPr><w:tag w:val=\"name\"/><w:id w:val=\"102\"/>" +
            "<w:dataBinding w:prefixMappings=\"xmlns:ns0='urn:cust'\" w:xpath=\"/ns0:root[1]/ns0:name[1]\" w:storeItemID=\"" + Store + "\"/><w:text/></w:sdtPr><w:sdtContent><w:r><w:rPr><w:b/></w:rPr><w:t>Ada</w:t></w:r></w:sdtContent></w:sdt></w:p>" +
            "<w:p><w:sdt><w:sdtPr><w:tag w:val=\"colour\"/><w:dropDownList w:lastValue=\"r\"><w:listItem w:displayText=\"Red\" w:value=\"r\"/><w:listItem w:displayText=\"Green\" w:value=\"g\"/></w:dropDownList></w:sdtPr><w:sdtContent><w:r><w:t>Red</w:t></w:r></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w:tag w:val=\"combo\"/><w:comboBox><w:listItem w:displayText=\"a\" w:value=\"a\"/></w:comboBox></w:sdtPr><w:sdtContent><w:r><w:t>a</w:t></w:r></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w:tag w:val=\"when\"/><w:date w:fullDate=\"2024-05-01T00:00:00Z\"><w:dateFormat w:val=\"d MMMM yyyy\"/><w:lid w:val=\"en-GB\"/><w:storeMappedDataAs w:val=\"dateTime\"/><w:calendar w:val=\"gregorian\"/></w:date></w:sdtPr><w:sdtContent><w:r><w:t>1 May 2024</w:t></w:r></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w:tag w:val=\"agree\"/><w14:checkbox><w14:checked w14:val=\"0\"/><w14:checkedState w14:val=\"2612\" w14:font=\"MS Gothic\"/><w14:uncheckedState w14:val=\"2610\" w14:font=\"MS Gothic\"/></w14:checkbox></w:sdtPr><w:sdtContent><w:r><w:rPr><w:rFonts w:ascii=\"MS Gothic\" w:hAnsi=\"MS Gothic\"/></w:rPr><w:t>\u2610</w:t></w:r></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w:tag w:val=\"pic\"/><w:picture/></w:sdtPr><w:sdtContent><w:r><w:t>[picture]</w:t></w:r></w:sdtContent></w:sdt></w:p>" +
            "<w:sdt><w:sdtPr><w:tag w:val=\"rep\"/><w15:repeatingSection/></w:sdtPr><w:sdtContent>" +
            "<w:sdt><w:sdtPr><w15:repeatingSectionItem/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>item1</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w15:repeatingSectionItem/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>item2</w:t></w:r></w:p></w:sdtContent></w:sdt></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w:group/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>grouped</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w:docPartObj><w:docPartGallery w:val=\"Table of Contents\"/><w:docPartUnique/></w:docPartObj></w:sdtPr><w:sdtContent><w:p><w:r><w:t>toc</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w:citation/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>cite</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w:bibliography/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>bib</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:sdt><w:sdtPr><w:tag w:val=\"eq\"/><w:equation/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>eq</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:sdt><w:sdtPr><w:tag w:val=\"rowsdt\"/></w:sdtPr><w:sdtContent><w:tr><w:sdt><w:sdtPr><w:tag w:val=\"cellsdt\"/></w:sdtPr><w:sdtContent><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:sdtContent></w:sdt></w:tr></w:sdtContent></w:sdt></w:tbl><w:p/>";
        string path = Path.Combine(dir, "sdt-all-types.docx");
        using FileStream fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
        const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        AddEntry(zip, "[Content_Types].xml", Decl + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/customXml/itemProps1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.customXmlProperties+xml\"/></Types>");
        AddEntry(zip, "_rels/.rels", Decl + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + Rel + "officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        AddEntry(zip, "word/_rels/document.xml.rels", Decl + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + Rel + "customXml\" Target=\"../customXml/item1.xml\"/></Relationships>");
        AddEntry(zip, "word/document.xml", Decl + $"<w:document xmlns:w=\"{W}\" xmlns:w14=\"{W14}\" xmlns:w15=\"{W15}\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"w14 w15\"><w:body>{body}<w:sectPr/></w:body></w:document>");
        AddEntry(zip, "customXml/item1.xml", Decl + "<ns0:root xmlns:ns0=\"urn:cust\"><ns0:name>Ada Lovelace</ns0:name></ns0:root>");
        AddEntry(zip, "customXml/_rels/item1.xml.rels", Decl + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + Rel + "customXmlProps\" Target=\"itemProps1.xml\"/></Relationships>");
        AddEntry(zip, "customXml/itemProps1.xml", Decl + "<ds:datastoreItem xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\" ds:itemID=\"" + Store + "\"><ds:schemaRefs><ds:schemaRef ds:uri=\"urn:cust\"/></ds:schemaRefs></ds:datastoreItem>");
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
        e.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream s = e.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
