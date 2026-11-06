namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>
/// Reads a chart sheet part ([MS-XLSB] §2.1.7.7) as far as a SpreadsheetML chart sheet needs: its margins and the relationship to its drawing.
/// The records are not written back; a binary workbook keeps the part as stored.
/// </summary>
internal static class XlsbChartSheetPart
{
    public static byte[] ToXml(ReadOnlyMemory<byte> bin, OpenContext context, string partName)
    {
        string? drawing = null;
        byte[]? margins = null;
        var r = new XlsbRecordReader(bin, context, partName);
        while (r.Read())
        {
            if (r.Type == XlsbRecordType.Drawing)
            {
                drawing = new XlsbRecordFields(r.Data).NullableWideString();
            }
            else if (r.Type == XlsbRecordType.Margins)
            {
                margins = r.Data.ToArray();
            }
        }

        var x = new XmlSink(512);
        x.Raw("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<chartsheet xmlns=\"").Raw(XlsbMarkup.MainNamespace).Raw("\" xmlns:r=\"").Raw(XlsbMarkup.RelationshipNamespace).Raw("\">");
        x.Raw("<sheetViews><sheetView workbookViewId=\"0\" zoomToFit=\"1\"/></sheetViews>");
        if (margins is { Length: >= 48 })
        {
            var f = new XlsbRecordFields(margins);
            x.Raw("<pageMargins").Attr("left", f.Number()).Attr("right", f.Number()).Attr("top", f.Number()).Attr("bottom", f.Number())
                .Attr("header", f.Number()).Attr("footer", f.Number()).Raw("/>");
        }

        if (!string.IsNullOrEmpty(drawing))
        {
            x.Raw("<drawing").Attr("r:id", drawing).Raw("/>");
        }

        x.Raw("</chartsheet>");
        return x.ToArray();
    }
}
