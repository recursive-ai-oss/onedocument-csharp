using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Conversions between the SpreadsheetML hub and the binary package.</summary>
internal static class XlsbExport
{
    // Parts of a SpreadsheetML package that have no binary counterpart written yet; they are reported and left out.
    private static readonly string[] DroppedTypes =
    [
        "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.queryTable+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.volatileDependencies+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheetMetadata+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.userNames+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.revisionHeaders+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.revisionLog+xml",
    ];

    private static readonly string[] UnsupportedSheetTypes =
    [
        "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.dialogsheet+xml",
        "application/vnd.ms-excel.macrosheet+xml",
        "application/vnd.ms-excel.intlmacrosheet+xml",
    ];

    /// <summary>Writes a workbook that was opened from a binary workbook as SpreadsheetML (or Flat OPC).</summary>
    public static void ToOoxml(Workbook wb, XlsbBook book, OfficeFormat format, Stream destination, ILossSink loss, CancellationToken cancellationToken)
    {
        if (!Workbook.IsWorkbookFormat(format) || format == OfficeFormat.Xlsb)
        {
            throw new FormatNotAvailableException(format, "write a workbook as");
        }

        InMemoryPackage hub = book.Hub;
        if (book.HasBinarySheets)
        {
            throw new OfficeUnsupportedException("dialog and macro sheets of a binary workbook in another format");
        }

        if (format is OfficeFormat.Xlsx or OfficeFormat.Xltx && hub.Parts.Any(p => string.Equals(p.ContentType, Workbook.VbaContentType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new OfficeUnsupportedException("saving a workbook that holds a macro project as a macro-free format");
        }

        OpcPackage opc = OpcPackage.Create(wb.Limits);
        string target = format == OfficeFormat.FlatOpcWorkbook ? XlsbBook.WorkbookXmlType : Workbook.MainContentType(format);
        foreach (IPackagePart p in hub.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyMemory<byte> bytes = p.GetBytes();
            if (p.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && ContainsPrivateMarkup(bytes.Span))
            {
                bytes = Strip(bytes, wb.OpenContext, p.Name, loss);
            }

            string type = string.Equals(p.Name, book.HubMainName, StringComparison.OrdinalIgnoreCase) ? target : p.ContentType;
            opc.AddPart(p.Name, type, bytes);
        }

        foreach (IPackagePart p in hub.Parts)
        {
            IReadOnlyList<Relationship> rels = hub.GetRelationships(p.Name);
            if (rels.Count > 0)
            {
                opc.SetRelationships(p.Name, rels);
            }
        }

        opc.SetRelationships(null, hub.GetRelationships());
        if (book.HiddenParts.Count > 0)
        {
            loss.Report("xlsb.part-not-converted", "binary-only parts", book.HiddenParts.Count,
                "Parts of the binary workbook with no SpreadsheetML counterpart here (" + string.Join(", ", book.HiddenParts.Take(6)) + (book.HiddenParts.Count > 6 ? ", …" : string.Empty) + ") are left out.");
        }

        if (format == OfficeFormat.FlatOpcWorkbook)
        {
            opc.WriteFlatOpcTo(destination, cancellationToken);
        }
        else
        {
            opc.WriteZipTo(destination, cancellationToken);
        }
    }

    private static bool ContainsPrivateMarkup(ReadOnlySpan<byte> xml) => xml.IndexOf("urn:onedocument:xlsb"u8) >= 0;

    /// <summary>Removes the private elements that carry binary-only records from SpreadsheetML markup.</summary>
    private static ReadOnlyMemory<byte> Strip(ReadOnlyMemory<byte> xml, OpenContext context, string partName, ILossSink loss)
    {
        MarkupDocument doc = MarkupDocument.Parse(xml, context, partName);
        var doomed = new List<MarkupNode>();
        long dropped = 0;
        void Walk(MarkupNode n)
        {
            foreach (MarkupNode c in n.ChildElements)
            {
                if (c.NamespaceUri == XlsbMarkup.Namespace)
                {
                    doomed.Add(c);
                    if (XlsbMarkup.IsRaw(c))
                    {
                        dropped++;
                    }
                }
                else
                {
                    Walk(c);
                }
            }
        }

        Walk(doc.Root);
        for (int i = doomed.Count - 1; i >= 0; i--)
        {
            doomed[i].Remove();
        }

        if (dropped > 0)
        {
            loss.Report("xlsb.records-not-converted", "binary records without a SpreadsheetML form", dropped,
                "Records of the binary workbook that the workbook model does not represent (extension and future records) are left out.");
        }

        return doc.ToArray();
    }

    /// <summary>Writes a workbook that was opened from SpreadsheetML as a binary workbook.</summary>
    public static void ToBinary(Workbook wb, Stream destination, ILossSink loss, CancellationToken cancellationToken)
    {
        IOpcPackage src = wb.Package;
        OpenContext context = wb.OpenContext;
        string main = wb.MainPartName;
        foreach (IPackagePart p in src.Parts)
        {
            if (UnsupportedSheetTypes.Contains(p.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                throw new OfficeUnsupportedException("chart, dialog and macro sheets in a binary workbook", DefectLocation.InPart(p.Name));
            }
        }

        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IPackagePart p in src.Parts)
        {
            if (DroppedTypes.Contains(p.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                dropped.Add(p.Name);
                loss.Report("xlsb.part-not-written", "part type " + p.ContentType.Replace("application/vnd.openxmlformats-officedocument.spreadsheetml.", string.Empty, StringComparison.Ordinal), 1,
                    "A part of this type (comments, tables, pivot tables, connections, external links) has no binary form written yet and is left out.");
            }
        }

        Func<string, List<string>?> noExternal = _ => null;
        MarkupDocument wbDoc = MarkupDocument.Parse(src.FindPart(main)!.GetBytes(), context, main);
        XlsbFormulaContext cx = XlsbWorkbookPart.ContextFromXml(wbDoc.Root, noExternal, out _);
        var fonts = new XlsbFontTable();
        string? stylesName = src.Parts.FirstOrDefault(p => string.Equals(p.ContentType, XlsbBook.StylesXmlType, StringComparison.OrdinalIgnoreCase))?.Name;
        if (stylesName is not null)
        {
            MarkupDocument sd = MarkupDocument.Parse(src.FindPart(stylesName)!.GetBytes(), context, stylesName);
            foreach (MarkupNode f in sd.Root.Element("fonts").Elements("font"))
            {
                fonts.Fonts.Add(XlsbFont.FromXml(f));
            }
        }

        static string BinName(string n) => n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? n[..^4] + ".bin" : n;
        var rename = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IPackagePart p in src.Parts)
        {
            if (dropped.Contains(p.Name))
            {
                continue;
            }

            if (string.Equals(p.Name, main, StringComparison.OrdinalIgnoreCase))
            {
                rename[p.Name] = BinName(p.Name);
                kinds[p.Name] = "workbook";
            }
            else if (string.Equals(p.ContentType, XlsbBook.SheetXmlType, StringComparison.OrdinalIgnoreCase))
            {
                rename[p.Name] = BinName(p.Name);
                kinds[p.Name] = "sheet";
            }
            else if (string.Equals(p.ContentType, XlsbBook.StylesXmlType, StringComparison.OrdinalIgnoreCase))
            {
                rename[p.Name] = BinName(p.Name);
                kinds[p.Name] = "styles";
            }
            else if (string.Equals(p.ContentType, XlsbBook.SharedStringsXmlType, StringComparison.OrdinalIgnoreCase))
            {
                rename[p.Name] = BinName(p.Name);
                kinds[p.Name] = "strings";
            }
        }

        OpcPackage opc = OpcPackage.Create(wb.Limits);
        var done = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string order in new[] { "sheet", "strings", "styles", "workbook" })
        {
            foreach (IPackagePart p in src.Parts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!kinds.TryGetValue(p.Name, out string? kind) || kind != order)
                {
                    continue;
                }

                ReadOnlyMemory<byte> bytes = p.GetBytes();
                done[p.Name] = kind switch
                {
                    "sheet" => XlsbSheetWriter.FromXml(bytes, context, p.Name, cx, fonts, loss),
                    "strings" => XlsbSharedStringsPart.FromXml(bytes, context, p.Name, fonts),
                    "styles" => XlsbStylesPart.FromXml(bytes, context, p.Name, fonts, loss),
                    _ => XlsbWorkbookPart.FromXml(bytes, context, p.Name, cx, loss),
                };
            }
        }

        foreach (IPackagePart p in src.Parts)
        {
            if (dropped.Contains(p.Name))
            {
                continue;
            }

            if (kinds.TryGetValue(p.Name, out string? kind))
            {
                string type = kind switch
                {
                    "workbook" => XlsbBook.WorkbookBinType,
                    "sheet" => XlsbBook.SheetBinType,
                    "styles" => XlsbBook.StylesBinType,
                    _ => XlsbBook.SharedStringsBinType,
                };
                opc.AddPart(rename[p.Name], type, done[p.Name]);
            }
            else
            {
                opc.AddPart(p.Name, p.ContentType, p.GetBytes());
            }
        }

        string Target(string? from, string to) => XlsbBook.RelativeTarget(from is null ? null : rename.GetValueOrDefault(from, from), rename.GetValueOrDefault(to, to));
        foreach (string? owner in src.Parts.Select(p => p.Name).Prepend(null!).Where(n => n is null || !dropped.Contains(n)))
        {
            var list = new List<Relationship>();
            foreach (Relationship r in src.GetRelationships(owner))
            {
                if (r.TargetMode == RelationshipTargetMode.Internal && OpcPackage.TryResolveTarget(owner, r, out PartUri t))
                {
                    if (dropped.Contains(t.Value))
                    {
                        continue;
                    }

                    list.Add(r with { Target = Target(owner, t.Value) });
                }
                else
                {
                    list.Add(r);
                }
            }

            if (list.Count > 0 || owner is null)
            {
                opc.SetRelationships(owner is null ? null : rename.GetValueOrDefault(owner, owner), list);
            }
        }

        opc.WriteZipTo(destination, cancellationToken);
    }
}
