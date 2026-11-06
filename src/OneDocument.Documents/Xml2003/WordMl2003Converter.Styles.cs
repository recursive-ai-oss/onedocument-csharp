using System.Buffers;
using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Xml2003;

internal sealed partial class WordMl2003Converter
{
    // ECMA-376 Part 1 §17.7.4.17 (CT_Style).
    private static readonly string[] StyleOrder =
    [
        "name", "aliases", "basedOn", "next", "link", "autoRedefine", "hidden", "uiPriority", "semiHidden", "unhideWhenUsed", "qFormat", "locked",
        "personal", "personalCompose", "personalReply", "rsid", "pPr", "rPr", "tblPr", "trPr", "tcPr", "tblStylePr",
    ];

    private static readonly string[] StylePartOrder = ["pPr", "rPr", "tblPr", "trPr", "tcPr"];

    // §17.9.3 (CT_Lvl) and §17.9.1 (CT_AbstractNum).
    private static readonly string[] LvlOrder =
        ["start", "numFmt", "lvlRestart", "pStyle", "isLgl", "suff", "lvlText", "lvlPicBulletId", "legacy", "lvlJc", "pPr", "rPr"];

    private static readonly string[] AbstractNumOrder = ["nsid", "multiLevelType", "tmpl", "name", "styleLink", "numStyleLink", "lvl"];

    private static readonly Dictionary<string, string> NumberFormatNames = new(StringComparer.Ordinal)
    {
        ["0"] = "decimal", ["1"] = "upperRoman", ["2"] = "lowerRoman", ["3"] = "upperLetter", ["4"] = "lowerLetter", ["5"] = "ordinal",
        ["6"] = "cardinalText", ["7"] = "ordinalText", ["8"] = "hex", ["9"] = "chicago", ["10"] = "ideographDigital", ["11"] = "japaneseCounting",
        ["12"] = "aiueo", ["13"] = "iroha", ["14"] = "decimalFullWidth", ["15"] = "decimalHalfWidth", ["16"] = "japaneseLegal",
        ["17"] = "japaneseDigitalTenThousand", ["18"] = "decimalEnclosedCircle", ["19"] = "decimalFullWidth2", ["20"] = "aiueoFullWidth",
        ["21"] = "irohaFullWidth", ["22"] = "decimalZero", ["23"] = "bullet", ["56"] = "numberInDash", ["255"] = "none",
    };

    // ---- styles -------------------------------------------------------------------------------------------------------------------

    private Story BuildStyles(MarkupNode root)
    {
        var story = new Story("/word/styles.xml");
        story.Begin("w:styles");
        MarkupNode fonts = Child(root, Ns2003.Word, "fonts");
        MarkupNode defaults = fonts.IsNull ? default : Child(fonts, Ns2003.Word, "defaultFonts");
        if (!defaults.IsNull)
        {
            var kept = new List<(string Name, string Value)>();
            foreach (MarkupAttributeRef a in defaults.Attributes)
            {
                string value = a.Value;
                if (!a.IsNamespaceDeclaration && value.Length > 0)
                {
                    kept.Add((Camel(System.Text.Encoding.UTF8.GetString(a.LocalName)), value));
                }
            }

            if (kept.Count > 0)
            {
                story.W.WriteStartElement("w:docDefaults");
                story.W.WriteStartElement("w:rPrDefault");
                story.W.WriteStartElement("w:rPr");
                story.W.WriteStartElement("w:rFonts");
                foreach ((string name, string value) in kept)
                {
                    story.W.WriteAttribute("w:" + name, value);
                }

                story.W.WriteEndElement();
                story.W.WriteEndElement();
                story.W.WriteEndElement();
                story.W.WriteEndElement();
            }
        }

        bool hasDefaultParagraph = false;
        MarkupNode styles = Child(root, Ns2003.Word, "styles");
        if (!styles.IsNull)
        {
            foreach (MarkupNode st in styles.ChildElements)
            {
                if (Ns2003.Of(st) != Ns2003.Word || st.LocalNameString != "style")
                {
                    continue;
                }

                bool isDefault = IsOn(Attr(st, "default")) && Attr(st, "default") is not null;
                if (isDefault && (Attr(st, "type") ?? "paragraph") == "paragraph")
                {
                    hasDefaultParagraph = true;
                }

                EmitStyle(story, st);
            }
        }

        if (!hasDefaultParagraph)
        {
            story.W.WriteStartElement("w:style");
            story.W.WriteAttribute("w:type", "paragraph");
            story.W.WriteAttribute("w:default", "1");
            story.W.WriteAttribute("w:styleId", "Normal");
            story.W.WriteStartElement("w:name");
            story.W.WriteAttribute("w:val", "Normal");
            story.W.WriteEndElement();
            story.W.WriteEndElement();
        }

        story.End();
        return story;
    }

    private void EmitStyle(Story story, MarkupNode st)
    {
        string type = Attr(st, "type") ?? "paragraph";
        string? id = Attr(st, "styleId");
        if (id is null)
        {
            Defect(DefectKind.Structure, "wml2003.style-id", DefectSeverity.Repaired, "A style has no styleId; one was made from its name.", st);
            id = Attr(Child(st, Ns2003.Word, "name"), "val") ?? "Style" + st.SourceStart.ToString(CultureInfo.InvariantCulture);
            id = id.Replace(' ', '_');
        }

        EmitProps(story, st, "w:style", StyleOrder, null, 0, () =>
        {
            story.W.WriteAttribute("w:type", type == "list" ? "numbering" : type);
            if (Attr(st, "default") is { } d && IsOn(d))
            {
                story.W.WriteAttribute("w:default", "1");
            }

            story.W.WriteAttribute("w:styleId", id);
            if (Attr(st, "customStyle") is { } c && IsOn(c))
            {
                story.W.WriteAttribute("w:customStyle", "1");
            }
        });
    }

    private void EmitTableStylePart(Story s, MarkupNode node)
    {
        EmitProps(s, node, "w:tblStylePr", StylePartOrder, null, 0, () =>
        {
            if (Attr(node, "type") is { } t)
            {
                s.W.WriteAttribute("w:type", t);
            }
        });
    }

    // ---- lists ----------------------------------------------------------------------------------------------------------------------

    private Story? BuildNumbering(MarkupNode root)
    {
        MarkupNode lists = Child(root, Ns2003.Word, "lists");
        if (lists.IsNull)
        {
            return null;
        }

        var story = new Story("/word/numbering.xml");
        story.Begin("w:numbering");
        int count = 0;
        foreach (MarkupNode def in lists.ChildElements)
        {
            if (Ns2003.Of(def) != Ns2003.Word || def.LocalNameString != "listDef")
            {
                continue;
            }

            string id = Attr(def, "listDefId") ?? count.ToString(CultureInfo.InvariantCulture);
            EmitProps(story, def, "w:abstractNum", AbstractNumOrder, null, 0, () => story.W.WriteAttribute("w:abstractNumId", id));
            count++;
        }

        foreach (MarkupNode list in lists.ChildElements)
        {
            if (Ns2003.Of(list) != Ns2003.Word || list.LocalNameString != "list")
            {
                continue;
            }

            string? ilfo = Attr(list, "ilfo");
            MarkupNode ilst = Child(list, Ns2003.Word, "ilst");
            string? target = ilst.IsNull ? null : Attr(ilst, "val");
            if (ilfo is null || target is null)
            {
                continue;
            }

            story.W.WriteStartElement("w:num");
            story.W.WriteAttribute("w:numId", ilfo);
            story.W.WriteStartElement("w:abstractNumId");
            story.W.WriteAttribute("w:val", target);
            story.W.WriteEndElement();
            foreach (MarkupNode ov in list.ChildElements)
            {
                if (Ns2003.Of(ov) == Ns2003.Word && ov.LocalNameString == "lvlOverride")
                {
                    story.W.WriteStartElement("w:lvlOverride");
                    story.W.WriteAttribute("w:ilvl", Attr(ov, "ilvl") ?? "0");
                    foreach (MarkupNode o in ov.ChildElements)
                    {
                        string local = o.LocalNameString;
                        if (Ns2003.Of(o) != Ns2003.Word)
                        {
                            continue;
                        }

                        if (local == "startOverride")
                        {
                            story.W.WriteStartElement("w:startOverride");
                            story.W.WriteAttribute("w:val", Attr(o, "val") ?? "1");
                            story.W.WriteEndElement();
                        }
                        else if (local == "lvl")
                        {
                            EmitLvl(story, o);
                        }
                    }

                    story.W.WriteEndElement();
                }
            }

            story.W.WriteEndElement();
        }

        story.End();
        return story;
    }

    private void EmitLvl(Story s, MarkupNode lvl) =>
        EmitProps(s, lvl, "w:lvl", LvlOrder, null, 0, () =>
        {
            s.W.WriteAttribute("w:ilvl", Attr(lvl, "ilvl") ?? "0");
            if (Attr(lvl, "tplc") is { } tplc)
            {
                s.W.WriteAttribute("w:tplc", tplc);
            }
        });

    private void EmitNumberFormat(Story s, MarkupNode nfc)
    {
        string raw = Attr(nfc, "val") ?? "0";
        string name;
        if (nfc.LocalNameString == "numFmt")
        {
            name = raw;
        }
        else if (!NumberFormatNames.TryGetValue(raw, out string? mapped))
        {
            Defect(DefectKind.Value, "wml2003.number-format", DefectSeverity.Repaired, "List number format " + raw + " has no known name; decimal was used.", nfc);
            name = "decimal";
        }
        else
        {
            name = mapped;
        }

        s.W.WriteStartElement("w:numFmt");
        s.W.WriteAttribute("w:val", name);
        s.W.WriteEndElement();
    }

    // ---- notes and comments -----------------------------------------------------------------------------------------------------

    private Story BuildNotes(string root, string element, string partName, List<(int Id, MarkupNode Node)> notes)
    {
        var story = new Story(partName);
        story.Begin(root);
        foreach ((string type, string id) in new[] { ("separator", "-1"), ("continuationSeparator", "0") })
        {
            story.W.WriteStartElement(element);
            story.W.WriteAttribute("w:type", type);
            story.W.WriteAttribute("w:id", id);
            story.W.WriteStartElement("w:p");
            story.W.WriteStartElement("w:r");
            story.W.WriteStartElement(type == "separator" ? "w:separator" : "w:continuationSeparator");
            story.W.WriteEndElement();
            story.W.WriteEndElement();
            story.W.WriteEndElement();
            story.W.WriteEndElement();
        }

        // Notes found inside notes are numbered after the ones already queued, so the list can grow while it is walked.
        for (int i = 0; i < notes.Count; i++)
        {
            story.W.WriteStartElement(element);
            story.W.WriteAttribute("w:id", notes[i].Id.ToString(CultureInfo.InvariantCulture));
            story.EndsWithParagraph = false;
            EmitBlocks(story, notes[i].Node, null, 2);
            if (!story.EndsWithParagraph)
            {
                story.W.WriteStartElement("w:p");
                story.W.WriteEndElement();
            }

            story.W.WriteEndElement();
        }

        story.End();
        return story;
    }

    private Story BuildComments()
    {
        var story = new Story("/word/comments.xml");
        story.Begin("w:comments");
        for (int i = 0; i < _comments.Count; i++)
        {
            (int id, string aml) = _comments[i];
            MarkupNode a = _commentContent[aml];
            story.W.WriteStartElement("w:comment");
            story.W.WriteAttribute("w:id", id.ToString(CultureInfo.InvariantCulture));
            story.W.WriteAttribute("w:author", Attr(a, "author") is { Length: > 0 } author ? author : "unknown");
            if (Attr(a, "createdate") is { Length: > 0 } date)
            {
                story.W.WriteAttribute("w:date", date);
            }

            if (Attr(a, "initials") is { Length: > 0 } initials)
            {
                story.W.WriteAttribute("w:initials", initials);
            }

            story.EndsWithParagraph = false;
            MarkupNode content = Child(a, Ns2003.Annotation, "content");
            if (!content.IsNull)
            {
                EmitBlocks(story, content, null, 2);
            }

            if (!story.EndsWithParagraph)
            {
                story.W.WriteStartElement("w:p");
                story.W.WriteEndElement();
            }

            story.W.WriteEndElement();
        }

        story.End();
        return story;
    }

    // ---- settings and properties ------------------------------------------------------------------------------------------------

    private Story? BuildSettings(MarkupNode root)
    {
        MarkupNode docPr = Child(root, Ns2003.Word, "docPr");
        MarkupNode zoom = docPr.IsNull ? default : Child(docPr, Ns2003.Word, "zoom");
        MarkupNode track = docPr.IsNull ? default : Child(docPr, Ns2003.Word, "trackRevisions");
        MarkupNode tab = docPr.IsNull ? default : Child(docPr, Ns2003.Word, "defaultTabStop");
        MarkupNode spacing = docPr.IsNull ? default : Child(docPr, Ns2003.Word, "characterSpacingControl");
        bool trackOn = !track.IsNull && IsOn(Attr(track, "val"));
        if (zoom.IsNull && !trackOn && tab.IsNull && !_usesEvenOdd && spacing.IsNull)
        {
            return null;
        }

        var story = new Story("/word/settings.xml");
        story.Begin("w:settings");
        XmlWriter w = story.W;
        if (!zoom.IsNull && Attr(zoom, "percent") is { } percent && int.TryParse(percent, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            w.WriteStartElement("w:zoom");
            w.WriteAttribute("w:percent", percent);
            w.WriteEndElement();
        }

        if (trackOn)
        {
            w.WriteStartElement("w:trackRevisions");
            w.WriteEndElement();
        }

        if (!tab.IsNull && Attr(tab, "val") is { } twips)
        {
            w.WriteStartElement("w:defaultTabStop");
            w.WriteAttribute("w:val", MapValue(tab, "defaultTabStop", "val", twips) ?? "720");
            w.WriteEndElement();
        }

        if (_usesEvenOdd)
        {
            w.WriteStartElement("w:evenAndOddHeaders");
            w.WriteEndElement();
        }

        if (!spacing.IsNull && Attr(spacing, "val") is { } control)
        {
            w.WriteStartElement("w:characterSpacingControl");
            // ECMA-376 Part 1 §17.15.1.18 (ST_CharacterSpacing).
            w.WriteAttribute("w:val", control switch
            {
                "CompressPunctuation" => "compressPunctuation",
                "CompressPunctuationAndKana" => "compressPunctuationAndJapaneseKana",
                _ => "doNotCompress",
            });
            w.WriteEndElement();
        }

        story.End();
        return story;
    }

    private static ReadOnlyMemory<byte> Plain(Action<XmlWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new XmlWriter(buffer);
        w.WriteXmlDeclaration();
        write(w);
        w.Complete();
        return buffer.WrittenMemory;
    }

    private void AddDocumentProperties(OpcPackage package, MarkupNode root, List<Relationship> rootRels)
    {
        MarkupNode props = Child(root, Ns2003.Office, "DocumentProperties");
        if (!props.IsNull)
        {
            AddCoreProperties(package, props, rootRels);
            AddExtendedProperties(package, props, rootRels);
        }

        MarkupNode custom = Child(root, Ns2003.Office, "CustomDocumentProperties");
        if (!custom.IsNull)
        {
            AddCustomProperties(package, custom, rootRels);
        }
    }

    private static string? Text(MarkupNode props, string local)
    {
        MarkupNode n = Child(props, Ns2003.Office, local);
        if (n.IsNull)
        {
            return null;
        }

        string text = n.InnerText.Trim();
        return text.Length == 0 ? null : text;
    }

    private void AddCoreProperties(OpcPackage package, MarkupNode props, List<Relationship> rootRels)
    {
        var fields = new List<(string Element, string Value, bool Date)>();
        void Add(string element, string? value, bool date = false)
        {
            if (value is null)
            {
                return;
            }

            if (date && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            {
                Defect(DefectKind.Value, "wml2003.bad-value", DefectSeverity.Dropped, "The date '" + value + "' in the document properties is not a date.", props);
                return;
            }

            fields.Add((element, value, date));
        }

        Add("dc:title", Text(props, "Title"));
        Add("dc:subject", Text(props, "Subject"));
        Add("dc:creator", Text(props, "Author"));
        Add("cp:keywords", Text(props, "Keywords"));
        Add("dc:description", Text(props, "Description"));
        Add("cp:lastModifiedBy", Text(props, "LastAuthor"));
        Add("cp:revision", Text(props, "Revision"));
        Add("dcterms:created", Text(props, "Created"), date: true);
        Add("dcterms:modified", Text(props, "LastSaved"), date: true);
        Add("cp:category", Text(props, "Category"));
        if (fields.Count == 0)
        {
            return;
        }

        package.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Plain(w =>
        {
            w.WriteStartElement("cp:coreProperties");
            w.WriteNamespaceDeclaration("cp", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
            w.WriteNamespaceDeclaration("dc", "http://purl.org/dc/elements/1.1/");
            w.WriteNamespaceDeclaration("dcterms", "http://purl.org/dc/terms/");
            w.WriteNamespaceDeclaration("xsi", "http://www.w3.org/2001/XMLSchema-instance");
            foreach ((string element, string value, bool date) in fields)
            {
                w.WriteStartElement(element);
                if (date)
                {
                    w.WriteAttribute("xsi:type", "dcterms:W3CDTF");
                }

                w.WriteText(value);
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }));
        rootRels.Add(new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"));
    }

    private void AddExtendedProperties(OpcPackage package, MarkupNode props, List<Relationship> rootRels)
    {
        var fields = new List<(string Element, string Value)>();
        foreach ((string source, string target, bool number) in new[]
        {
            ("Company", "Company", false), ("Manager", "Manager", false), ("TotalTime", "TotalTime", true), ("Pages", "Pages", true),
            ("Words", "Words", true), ("Characters", "Characters", true), ("Lines", "Lines", true), ("Paragraphs", "Paragraphs", true),
            ("CharactersWithSpaces", "CharactersWithSpaces", true),
        })
        {
            string? value = Text(props, source);
            if (value is null)
            {
                continue;
            }

            if (number && !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                Defect(DefectKind.Value, "wml2003.bad-value", DefectSeverity.Dropped, "The count '" + value + "' in the document properties is not a number.", props);
                continue;
            }

            fields.Add((target, value));
        }

        if (fields.Count == 0)
        {
            return;
        }

        package.AddPart("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml", Plain(w =>
        {
            w.WriteStartElement("Properties");
            w.WriteNamespaceDeclaration(null, "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties");
            foreach ((string element, string value) in fields)
            {
                w.WriteStartElement(element);
                w.WriteText(value);
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }));
        rootRels.Add(new Relationship("rId3", RelBase + "extended-properties", "docProps/app.xml"));
    }

    private static void AddCustomProperties(OpcPackage package, MarkupNode custom, List<Relationship> rootRels)
    {
        var fields = new List<(string Name, string Type, string Value)>();
        foreach (MarkupNode p in custom.ChildElements)
        {
            string name = p.LocalNameString;
            string value = p.InnerText;
            string dt = Attr(p, "dt") ?? "string";
            switch (dt)
            {
                case "boolean":
                    fields.Add((name, "vt:bool", value is "1" or "true" or "True" ? "true" : "false"));
                    break;
                case "int" or "i4" or "i2" or "ui4" or "ui2":
                    if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int i))
                    {
                        fields.Add((name, "vt:i4", i.ToString(CultureInfo.InvariantCulture)));
                    }

                    break;
                case "float" or "r4" or "r8" or "number":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
                    {
                        fields.Add((name, "vt:r8", d.ToString("R", CultureInfo.InvariantCulture)));
                    }

                    break;
                default:
                    fields.Add((name, "vt:lpwstr", value));
                    break;
            }
        }

        if (fields.Count == 0)
        {
            return;
        }

        package.AddPart("/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml", Plain(w =>
        {
            w.WriteStartElement("Properties");
            w.WriteNamespaceDeclaration(null, "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
            w.WriteNamespaceDeclaration("vt", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
            int pid = 2;
            foreach ((string name, string type, string value) in fields)
            {
                w.WriteStartElement("property");
                w.WriteAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}");
                w.WriteAttribute("pid", (pid++).ToString(CultureInfo.InvariantCulture));
                w.WriteAttribute("name", name);
                w.WriteStartElement(type);
                w.WriteText(value);
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }));
        rootRels.Add(new Relationship("rId4", RelBase + "custom-properties", "docProps/custom.xml"));
    }
}
