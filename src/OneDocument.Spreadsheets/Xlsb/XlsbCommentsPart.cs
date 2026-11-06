using System.Linq;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Translates a comments part between <c>commentsN.bin</c> and <c>commentsN.xml</c> ([MS-XLSB] §2.1.7.8, §2.4.30, §2.4.340, §2.4.341).</summary>
internal static class XlsbCommentsPart
{
    public static byte[] ToXml(ReadOnlyMemory<byte> bin, OpenContext context, string partName, XlsbFontTable fonts)
    {
        var authors = new List<string>();
        var list = new XmlSink(1 << 12);
        var r = new XlsbRecordReader(bin, context, partName);
        bool open = false;
        while (r.Read())
        {
            switch (r.Type)
            {
                case XlsbRecordType.CommentAuthor:
                    authors.Add(new XlsbRecordFields(r.Data).WideString());
                    break;
                case XlsbRecordType.BeginComment:
                {
                    var f = new XlsbRecordFields(r.Data);
                    int author = f.I32();
                    int r1 = f.I32();
                    f.I32();
                    int c1 = f.I32();
                    if (f.Overrun)
                    {
                        context.Defects.Report(new Diagnostics.Defect(Diagnostics.DefectKind.Record, "xlsb.record-short", Diagnostics.DefectSeverity.Repaired,
                            "A comment record is shorter than its fields claim; the comment is skipped.", Diagnostics.DefectLocation.InRecord(partName, r.Offset)));
                        break;
                    }

                    list.Raw("<comment").Attr("ref", XlsbAddress.Cell(r1, c1)).Attr("authorId", Math.Max(0, author)).Raw("><text>");
                    open = true;
                    break;
                }

                case XlsbRecordType.CommentText when open:
                    XlsbSharedStringsPart.RichInner(list, r.Data, fonts, context, partName, r.Offset);
                    break;
                case XlsbRecordType.EndComment when open:
                    list.Raw("</text></comment>");
                    open = false;
                    break;
            }
        }

        var x = new XmlSink(list.Length + 256);
        x.Raw("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<comments xmlns=\"").Raw(XlsbMarkup.MainNamespace).Raw("\"><authors>");
        foreach (string a in authors)
        {
            x.Raw("<author>").Text(a).Raw("</author>");
        }

        x.Raw("</authors><commentList>").Raw(list.Memory.Span).Raw("</commentList></comments>");
        return x.ToArray();
    }

    public static byte[] FromXml(ReadOnlyMemory<byte> xml, OpenContext context, string partName, XlsbFontTable fonts)
    {
        MarkupDocument doc = MarkupDocument.Parse(xml, context, partName);
        using var ms = new MemoryStream();
        var w = new XlsbRecordWriter(ms);
        var d = new XlsbRecordData();
        w.Write(XlsbRecordType.BeginComments);
        w.Write(XlsbRecordType.BeginCommentAuthors);
        foreach (MarkupNode a in doc.Root.Element("authors").Elements("author"))
        {
            d.WideString(a.InnerText);
            w.Write(XlsbRecordType.CommentAuthor, d);
        }

        w.Write(XlsbRecordType.EndCommentAuthors);
        w.Write(XlsbRecordType.BeginCommentList);
        foreach (MarkupNode c in doc.Root.Element("commentList").Elements("comment"))
        {
            if (!XlsbAddress.TryCell(c.Str("ref"), out int row, out int col))
            {
                continue;
            }

            d.I32(c.Int("authorId"));
            d.I32(row);
            d.I32(row);
            d.I32(col);
            d.I32(col);
            d.Zeros(16);
            w.Write(XlsbRecordType.BeginComment, d);
            XlsbSharedStringsPart.WriteRich(c.Element("text"), d, fonts);
            w.Write(XlsbRecordType.CommentText, d);
            w.Write(XlsbRecordType.EndComment);
        }

        w.Write(XlsbRecordType.EndCommentList);
        w.Write(XlsbRecordType.EndComments);
        return ms.ToArray();
    }
}
