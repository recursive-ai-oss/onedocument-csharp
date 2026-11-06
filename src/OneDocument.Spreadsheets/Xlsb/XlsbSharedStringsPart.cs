using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Translates the shared string table between <c>sharedStrings.bin</c> and <c>sharedStrings.xml</c> ([MS-XLSB] §2.1.7.45, §2.5.122 RichStr).</summary>
internal static class XlsbSharedStringsPart
{
    /// <summary>Reads the table. Rich-text runs take their properties from the style sheet's fonts.</summary>
    public static byte[] ToXml(ReadOnlyMemory<byte> bin, OpenContext context, string partName, XlsbFontTable fonts)
    {
        var x = new XmlSink(1 << 16);
        long total = 0;
        long unique = 0;
        var body = new XmlSink(1 << 16);
        var r = new XlsbRecordReader(bin, context, partName);
        while (r.Read())
        {
            switch (r.Type)
            {
                case XlsbRecordType.BeginSst:
                {
                    var f = new XlsbRecordFields(r.Data);
                    total = f.U32();
                    unique = f.U32();
                    break;
                }

                case XlsbRecordType.SSTItem:
                    Item(body, r.Data, fonts, context, partName, r.Offset);
                    break;
            }
        }

        x.Raw("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<sst xmlns=\"").Raw(XlsbMarkup.MainNamespace).Raw("\"")
            .Attr("count", total).Attr("uniqueCount", unique).Raw(">").Raw(body.Memory.Span).Raw("</sst>");
        return x.ToArray();
    }

    private static void TextElement(XmlSink x, string text)
    {
        x.Raw("<t");
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]) || text.Contains('\n', StringComparison.Ordinal)))
        {
            x.Raw(" xml:space=\"preserve\"");
        }

        x.Raw(">").Text(text).Raw("</t>");
    }

    private static void Item(XmlSink x, ReadOnlySpan<byte> data, XlsbFontTable fonts, OpenContext context, string partName, long offset)
    {
        x.Raw("<si>");
        RichInner(x, data, fonts, context, partName, offset);
        x.Raw("</si>");
    }

    /// <summary>Writes the content of a string item (<c>t</c>, <c>r</c> and <c>rPh</c> elements) from a RichStr structure.</summary>
    internal static void RichInner(XmlSink x, ReadOnlySpan<byte> data, XlsbFontTable fonts, OpenContext context, string partName, long offset)
    {
        var f = new XlsbRecordFields(data);
        int flags = f.U8();
        string str = f.WideString();
        var runs = new List<(int Start, int Font)>();
        if ((flags & 1) != 0)
        {
            int n = (int)f.U32();
            for (int i = 0; i < n && !f.Overrun; i++)
            {
                runs.Add((f.U16(), f.U16()));
            }
        }

        string? phonetic = null;
        var phRuns = new List<(int First, int Mom, int Count)>();
        if ((flags & 2) != 0)
        {
            phonetic = f.WideString();
            int n = (int)f.U32();
            for (int i = 0; i < n && !f.Overrun; i++)
            {
                int first = f.U16();
                int mom = f.U16();
                int cch = f.U16();
                f.U16();
                f.U16();
                phRuns.Add((first, mom, cch));
            }
        }

        if (f.Overrun)
        {
            context.Defects.Report(new Diagnostics.Defect(Diagnostics.DefectKind.Record, "xlsb.record-short", Diagnostics.DefectSeverity.Repaired,
                "A shared string record is shorter than its fields claim; what was read is used.", Diagnostics.DefectLocation.InRecord(partName, offset)));
        }

        if (runs.Count == 0)
        {
            TextElement(x, str);
        }
        else
        {
            if (runs[0].Start > 0)
            {
                // Text before the first formatted run keeps the cell's own font; a run without properties says so.
                x.Raw("<r>");
                TextElement(x, str.Substring(0, Math.Min(runs[0].Start, str.Length)));
                x.Raw("</r>");
            }

            for (int i = 0; i < runs.Count; i++)
            {
                int start = Math.Min(runs[i].Start, str.Length);
                int end = i + 1 < runs.Count ? Math.Min(runs[i + 1].Start, str.Length) : str.Length;
                if (end < start)
                {
                    end = start;
                }

                x.Raw("<r>");
                int fi = runs[i].Font;
                if (fi >= 0 && fi < fonts.Fonts.Count)
                {
                    x.Raw("<rPr>");
                    fonts.Fonts[fi].Xml(x, run: true);
                    x.Raw("</rPr>");
                }

                TextElement(x, str.Substring(start, end - start));
                x.Raw("</r>");
            }
        }

        if (phonetic is not null)
        {
            for (int i = 0; i < phRuns.Count; i++)
            {
                int len = (i + 1 < phRuns.Count ? phRuns[i + 1].First : phonetic.Length) - phRuns[i].First;
                int first = Math.Min(phRuns[i].First, phonetic.Length);
                len = Math.Clamp(len, 0, phonetic.Length - first);
                x.Raw("<rPh").Attr("sb", phRuns[i].Mom).Attr("eb", phRuns[i].Mom + phRuns[i].Count).Raw(">");
                TextElement(x, phonetic.Substring(first, len));
                x.Raw("</rPh>");
            }
        }
    }

    /// <summary>Writes the table. A run property block that matches no font is added to <paramref name="fonts"/>, which sets <see cref="XlsbFontTable.Grew"/> so the style sheet is written again.</summary>
    public static byte[] FromXml(ReadOnlyMemory<byte> xml, OpenContext context, string partName, XlsbFontTable fonts)
    {
        MarkupDocument doc = MarkupDocument.Parse(xml, context, partName);
        using var ms = new MemoryStream();
        var w = new XlsbRecordWriter(ms);
        var d = new XlsbRecordData();
        var items = doc.Root.Elements("si").ToList();
        long total = doc.Root.Long("count", items.Count);
        d.U32((uint)total);
        d.U32((uint)items.Count);
        w.Write(XlsbRecordType.BeginSst, d);
        foreach (MarkupNode si in items)
        {
            WriteRich(si, d, fonts);
            w.Write(XlsbRecordType.SSTItem, d);
        }

        w.Write(XlsbRecordType.EndSst);
        return ms.ToArray();
    }

    /// <summary>Composes a RichStr structure ([MS-XLSB] §2.5.122) from the <c>t</c>, <c>r</c> and <c>rPh</c> children of a string item.</summary>
    internal static void WriteRich(MarkupNode si, XlsbRecordData d, XlsbFontTable fonts)
    {
        var text = new StringBuilder();
        var runs = new List<(int Start, int Font)>();
        var phRuns = new List<(int First, int Mom, int Count)>();
        var phonetic = new StringBuilder();
        foreach (MarkupNode c in si.ChildElements)
        {
            if (c.Is("t"))
            {
                text.Append(Decode(c.InnerText));
            }
            else if (c.Is("r"))
            {
                MarkupNode rpr = c.Element("rPr");
                int font = 0;
                if (rpr.IsNull && runs.Count == 0)
                {
                    text.Append(Decode(c.Element("t").InnerText));
                    continue;
                }

                if (!rpr.IsNull)
                {
                    XlsbFont f = XlsbFont.FromXml(rpr);
                    font = fonts.Find(f);
                    if (font < 0)
                    {
                        fonts.Fonts.Add(f);
                        fonts.Grew = true;
                        font = fonts.Fonts.Count - 1;
                    }
                }

                runs.Add((text.Length, font));
                text.Append(Decode(c.Element("t").InnerText));
            }
            else if (c.Is("rPh"))
            {
                string p = Decode(c.Element("t").InnerText);
                phRuns.Add((phonetic.Length, c.Int("sb"), Math.Max(0, c.Int("eb") - c.Int("sb"))));
                phonetic.Append(p);
            }
        }

        bool rich = runs.Count > 0;
        bool ext = phRuns.Count > 0;
        d.U8((rich ? 1 : 0) | (ext ? 2 : 0));
        d.WideString(text.ToString());
        if (rich)
        {
            d.U32((uint)runs.Count);
            foreach ((int s, int fnt) in runs)
            {
                d.U16(s);
                d.U16(fnt);
            }
        }

        if (ext)
        {
            d.WideString(phonetic.ToString());
            d.U32((uint)phRuns.Count);
            foreach ((int first, int mom, int count) in phRuns)
            {
                d.U16(first);
                d.U16(mom);
                d.U16(count);
                d.U16(0);
                d.U16(0);
            }
        }
    }

    /// <summary>Resolves the _xHHHH_ escapes of ECMA-376 Part 1 §22.4.2.4.</summary>
    internal static string Decode(string s)
    {
        if (!s.Contains("_x", StringComparison.Ordinal))
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '_' && i + 6 < s.Length && s[i + 1] == 'x' && s[i + 6] == '_'
                && int.TryParse(s.AsSpan(i + 2, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int code))
            {
                sb.Append((char)code);
                i += 6;
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }
}
