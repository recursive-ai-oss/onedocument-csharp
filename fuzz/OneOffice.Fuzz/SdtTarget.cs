using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Documents;
using RecursiveAI.OneOffice.Documents.ContentControls;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// Content controls. <c>sdt</c> takes the input as the body of a document with a bound custom XML part: it enumerates every control,
/// reads every property, evaluates every binding and applies every edit that the control's type allows; only typed library errors
/// and the documented argument errors may come out, and the edited document must still parse. <c>sdtpath</c> takes the input as
/// <c>xpath NL prefix mappings NL xml</c> and evaluates the binding path subset over the xml.
/// </summary>
internal static class SdtTarget
{
    private const string Head = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\"><w:body>";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    public static void RunDocument(ReadOnlySpan<byte> data)
    {
        string body = Encoding.UTF8.GetString(data);
        var p = new InMemoryPackage();
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml")]);
        p.AddPart("/word/document.xml", "application/xml", Encoding.UTF8.GetBytes(Head + body + "</w:body></w:document>"));
        p.SetRelationships("/word/document.xml", [new Relationship("rId2", Rel + "customXml", "../customXml/item1.xml")]);
        p.AddPart("/customXml/item1.xml", "application/xml", "<a:r xmlns:a=\"urn:a\"><a:n k=\"v\">x</a:n></a:r>"u8.ToArray());
        p.SetRelationships("/customXml/item1.xml", [new Relationship("rId1", Rel + "customXmlProps", "itemProps1.xml")]);
        p.AddPart("/customXml/itemProps1.xml", "application/xml", "<d:datastoreItem xmlns:d=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\" d:itemID=\"{X}\"/>"u8.ToArray());

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
            ContentControl[] all = doc.ContentControls().ToArray();
            foreach (ContentControl c in all)
            {
                _ = (c.Type, c.Level, c.Tag, c.Alias, c.Id, c.Lock, c.IsTemporary, c.IsShowingPlaceholder, c.PlaceholderDocPart, c.BuildingBlockGallery,
                    c.ListItems, c.SelectedIndex, c.IsChecked, c.DateFormat, c.Date, c.Text, c.IsMultiLine, c.EvaluateBinding(), c.Parent);
                _ = c.Children().Count();
            }

            foreach (ContentControl c in all.Take(32))
            {
                try
                {
                    switch (c.Type)
                    {
                        case ContentControlType.Checkbox:
                            c.SetChecked(c.IsChecked != true);
                            break;
                        case ContentControlType.DropDownList or ContentControlType.ComboBox when c.ListItems.Count > 0:
                            c.SelectItem(0);
                            break;
                        default:
                            c.SetText("e<&>\n\t");
                            break;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    // Documented refusals: a type or place without a value, an unknown list item, a node that was removed by an earlier edit.
                }
                catch (OfficeException)
                {
                }
            }

            if (all.Length > 0)
            {
                try
                {
                    all[^1].RemoveKeepingContent();
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OfficeException)
                {
                }
            }

            doc.CommitChanges();
            byte[] written = p.FindPart("/word/document.xml")!.GetBytes().ToArray();
            if (!MarkupDocument.TryParse(written, out _))
            {
                throw new InvalidOperationException("an edit left the part not well-formed");
            }
        }
    }

    public static void RunPath(ReadOnlySpan<byte> data)
    {
        string text = Encoding.UTF8.GetString(data);
        string[] parts = text.Split('\n', 3);
        string xpath = parts[0];
        string prefixes = parts.Length > 1 ? parts[1] : string.Empty;
        string xml = parts.Length > 2 ? parts[2] : "<a:r xmlns:a=\"urn:a\"><a:n k=\"v\">x</a:n></a:r>";
        if (!MarkupDocument.TryParse(Encoding.UTF8.GetBytes(xml), out MarkupDocument? doc) || doc is null)
        {
            doc = MarkupDocument.Parse("<a:r xmlns:a=\"urn:a\"><a:n k=\"v\">x</a:n></a:r>"u8.ToArray());
        }

        IReadOnlyDictionary<string, string> map = new ContentControlBinding(xpath, prefixes, null).GetPrefixMap();
        _ = BindingPath.Evaluate(doc, xpath, map);
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "seed-path"), "/a:r/a:n[1]\nxmlns:a='urn:a'\n<a:r xmlns:a=\"urn:a\"><a:n k=\"v\">x</a:n></a:r>");
        File.WriteAllText(Path.Combine(dir, "seed-path-attr"), "/a:r/a:n[@k='v']/@k\nxmlns:a=\"urn:a\"\n<a:r xmlns:a=\"urn:a\"><a:n k=\"v\">x</a:n></a:r>");
        File.WriteAllText(Path.Combine(dir, "seed-body-text"), "<w:sdt><w:sdtPr><w:tag w:val=\"t\"/><w:text/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>x</w:t></w:r></w:p></w:sdtContent></w:sdt>");
        File.WriteAllText(Path.Combine(dir, "seed-body-all"), "<w:p><w:sdt><w:sdtPr><w:dataBinding w:prefixMappings=\"xmlns:a='urn:a'\" w:xpath=\"/a:r/a:n\" w:storeItemID=\"{X}\"/><w14:checkbox><w14:checked w14:val=\"1\"/></w14:checkbox></w:sdtPr><w:sdtContent><w:r><w:t>x</w:t></w:r></w:sdtContent></w:sdt><w:sdt><w:sdtPr><w:dropDownList><w:listItem w:displayText=\"A\" w:value=\"a\"/></w:dropDownList></w:sdtPr><w:sdtContent><w:r><w:t>A</w:t></w:r></w:sdtContent></w:sdt></w:p>");
        return 0;
    }
}
