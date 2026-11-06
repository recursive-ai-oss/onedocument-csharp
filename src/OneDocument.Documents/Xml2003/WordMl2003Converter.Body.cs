using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Xml2003;

internal sealed partial class WordMl2003Converter
{
    // ---- blocks ------------------------------------------------------------------------------------------------------------

    private void EmitBlocks(Story s, MarkupNode parent, Revision? rev, int depth = 0)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), _ctx.Limits.MaxRecursionDepth, depth, DefectLocation.InPart("/", Math.Max(0, parent.SourceStart)));
        foreach (MarkupNode c in parent.ChildElements)
        {
            _ct.ThrowIfCancellationRequested();
            int ns = Ns2003.Of(c);
            string local = c.LocalNameString;
            if (ns == Ns2003.Word)
            {
                switch (local)
                {
                    case "p":
                        EmitParagraph(s, c, rev, FollowingSection(c));
                        break;
                    case "tbl":
                        EmitTable(s, c, rev, depth);
                        break;
                    case "sectPr":
                        // The last section's properties close the body; an earlier one rides on the paragraph before it, or on an
                        // empty paragraph when a table comes last.
                        if (!_consumedSections.Contains(c) && !c.Equals(_finalSection))
                        {
                            s.W.WriteStartElement("w:p");
                            s.W.WriteStartElement("w:pPr");
                            EmitSectPr(s, c);
                            s.W.WriteEndElement();
                            s.W.WriteEndElement();
                            s.EndsWithParagraph = true;
                        }

                        break;
                    case "body" or "binData" or "docPr" or "tcPr" or "trPr" or "tblPr" or "tblGrid" or "pPr":
                        break;
                    default:
                        Unmapped(local);
                        break;
                }
            }
            else if (ns == Ns2003.Aux)
            {
                if (local is "sect" or "sub-section")
                {
                    EmitBlocks(s, c, rev, depth + 1);
                }
            }
            else if (ns == Ns2003.Annotation)
            {
                EmitBlockAnnotation(s, c, rev, depth);
            }
        }
    }

    // A section's properties that follow a paragraph directly belong to that paragraph's mark, unless they are the document's last.
    private MarkupNode FollowingSection(MarkupNode p)
    {
        MarkupNode next = p.NextSibling;
        while (!next.IsNull && next.Kind != MarkupNodeKind.Element)
        {
            next = next.NextSibling;
        }

        if (!next.IsNull && Ns2003.Of(next) == Ns2003.Word && next.LocalNameString == "sectPr" && !next.Equals(_finalSection))
        {
            return next;
        }

        return default;
    }

    private void EmitBlockAnnotation(Story s, MarkupNode a, Revision? rev, int depth)
    {
        string? type = Attr(a, "type");
        switch (type)
        {
            case "Word.Bookmark.Start":
            case "Word.Bookmark.End":
                EmitInlineAnnotation(s, a, rev);
                break;
            case "Word.Insertion" or "Word.Deletion":
                MarkupNode content = Child(a, Ns2003.Annotation, "content");
                if (!content.IsNull)
                {
                    EmitBlocks(s, content, MakeRevision(a, type == "Word.Deletion"), depth + 1);
                }

                break;
            case "Word.Comment" or "Word.Comment.Start" or "Word.Comment.End":
                Lost("loss.comments", "comment anchors between paragraphs", "A comment anchored between two paragraphs, not inside one, was dropped.");
                break;
            default:
                Unmapped("annotation:" + type);
                break;
        }
    }

    private Revision MakeRevision(MarkupNode a, bool deleted) => new(deleted, _revisionId++, Attr(a, "author"), Attr(a, "createdate"));

    private static void WriteRevisionStart(Story s, Revision rev)
    {
        s.W.WriteStartElement(rev.Deleted ? "w:del" : "w:ins");
        s.W.WriteAttribute("w:id", rev.Id.ToString(CultureInfo.InvariantCulture));
        s.W.WriteAttribute("w:author", rev.Author ?? "unknown");
        if (rev.Date is { Length: > 0 })
        {
            s.W.WriteAttribute("w:date", rev.Date);
        }
    }

    // ---- paragraphs -----------------------------------------------------------------------------------------------------------

    private void EmitParagraph(Story s, MarkupNode p, Revision? rev, MarkupNode section)
    {
        s.W.WriteStartElement("w:p");
        MarkupNode pPr = Child(p, Ns2003.Word, "pPr");
        if (!pPr.IsNull || !section.IsNull || rev is not null)
        {
            EmitPPr(s, pPr, section, rev);
        }

        if (!section.IsNull)
        {
            _consumedSections.Add(section);
        }

        EmitInline(s, p, rev, 0);
        s.W.WriteEndElement();
        s.EndsWithParagraph = true;
    }

    private void EmitInline(Story s, MarkupNode parent, Revision? rev, int depth)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), _ctx.Limits.MaxRecursionDepth, depth, DefectLocation.InPart("/", Math.Max(0, parent.SourceStart)));
        foreach (MarkupNode c in parent.ChildElements)
        {
            int ns = Ns2003.Of(c);
            string local = c.LocalNameString;
            if (ns == Ns2003.Word)
            {
                switch (local)
                {
                    case "r":
                        if (rev is { } r)
                        {
                            WriteRevisionStart(s, r);
                            EmitRun(s, c, r.Deleted);
                            s.W.WriteEndElement();
                        }
                        else
                        {
                            EmitRun(s, c, false);
                        }

                        break;
                    case "hlink":
                        EmitHyperlink(s, c, rev, depth);
                        break;
                    case "fldSimple":
                        s.W.WriteStartElement("w:fldSimple");
                        s.W.WriteAttribute("w:instr", Attr(c, "instr") ?? string.Empty);
                        EmitInline(s, c, rev, depth + 1);
                        s.W.WriteEndElement();
                        break;
                    case "smartTag":
                        Lost("loss.smart-tags", "smart tags", "Smart tag wrappers were removed; their text was kept.");
                        EmitInline(s, c, rev, depth + 1);
                        break;
                    case "customXml" or "sdt" or "sdtContent":
                        Lost("loss.custom-xml", "custom XML and structured-document tags", "Wrappers were removed; their text was kept.");
                        EmitInline(s, c, rev, depth + 1);
                        break;
                    case "pPr" or "proofErr" or "permStart" or "permEnd" or "bookmarkStart" or "bookmarkEnd":
                        break;
                    default:
                        Unmapped(local);
                        break;
                }
            }
            else if (ns == Ns2003.Annotation)
            {
                EmitInlineAnnotation(s, c, rev);
            }
        }
    }

    private void EmitHyperlink(Story s, MarkupNode h, Revision? rev, int depth)
    {
        s.W.WriteStartElement("w:hyperlink");
        string? dest = Attr(h, "dest");
        string? bookmark = Attr(h, "bookmark");
        if (!string.IsNullOrEmpty(dest))
        {
            // Only a relationship is written: the target is never fetched.
            s.W.WriteAttribute("r:id", s.AddRel(RelBase + "hyperlink", dest, external: true));
        }

        if (!string.IsNullOrEmpty(bookmark))
        {
            s.W.WriteAttribute("w:anchor", bookmark);
        }

        if (Attr(h, "screenTip") is { Length: > 0 } tip)
        {
            s.W.WriteAttribute("w:tooltip", tip);
        }

        if (Attr(h, "target") is { Length: > 0 } frame)
        {
            s.W.WriteAttribute("w:tgtFrame", frame);
        }

        EmitInline(s, h, rev, depth + 1);
        s.W.WriteEndElement();
    }

    private void EmitInlineAnnotation(Story s, MarkupNode a, Revision? rev)
    {
        string? type = Attr(a, "type");
        string id = Attr(a, "id") ?? string.Empty;
        switch (type)
        {
            case "Word.Bookmark.Start":
                s.W.WriteStartElement("w:bookmarkStart");
                s.W.WriteAttribute("w:id", BookmarkId(id));
                s.W.WriteAttribute("w:name", Attr(a, "name") ?? "bookmark" + id);
                if (Attr(a, "colFirst") is { } cf)
                {
                    s.W.WriteAttribute("w:colFirst", cf);
                }

                if (Attr(a, "colLast") is { } cl)
                {
                    s.W.WriteAttribute("w:colLast", cl);
                }

                s.W.WriteEndElement();
                break;
            case "Word.Bookmark.End":
                s.W.WriteStartElement("w:bookmarkEnd");
                s.W.WriteAttribute("w:id", BookmarkId(id));
                s.W.WriteEndElement();
                break;
            case "Word.Comment.Start" when _commentContent.ContainsKey(id):
                s.W.WriteStartElement("w:commentRangeStart");
                s.W.WriteAttribute("w:id", CommentId(id).ToString(CultureInfo.InvariantCulture));
                s.W.WriteEndElement();
                break;
            case "Word.Comment.End" when _commentContent.ContainsKey(id):
                WriteCommentEnd(s, id);
                break;
            case "Word.Comment":
                if (!_commentMarked.Contains(id))
                {
                    // No range was marked: anchor the comment at a point.
                    s.W.WriteStartElement("w:commentRangeStart");
                    s.W.WriteAttribute("w:id", CommentId(id).ToString(CultureInfo.InvariantCulture));
                    s.W.WriteEndElement();
                    WriteCommentEnd(s, id);
                }

                break;
            case "Word.Insertion" or "Word.Deletion":
                MarkupNode content = Child(a, Ns2003.Annotation, "content");
                if (!content.IsNull)
                {
                    EmitInline(s, content, MakeRevision(a, type == "Word.Deletion"), 1);
                }

                break;
            case "Word.Formatting":
                Lost("loss.formatting-revisions", "formatting revisions", "Tracked formatting changes were dropped; the current formatting was kept.");
                break;
            case "Word.Comment.Start" or "Word.Comment.End":
                break;
            default:
                Unmapped("annotation:" + type);
                break;
        }
    }

    private static string BookmarkId(string amlId) =>
        int.TryParse(amlId, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n.ToString(CultureInfo.InvariantCulture) : "0";

    private int CommentId(string amlId)
    {
        if (!_commentIds.TryGetValue(amlId, out int n))
        {
            n = _commentIds.Count;
            _commentIds[amlId] = n;
            _comments.Add((n, amlId));
        }

        return n;
    }

    private void WriteCommentEnd(Story s, string id)
    {
        string n = CommentId(id).ToString(CultureInfo.InvariantCulture);
        s.W.WriteStartElement("w:commentRangeEnd");
        s.W.WriteAttribute("w:id", n);
        s.W.WriteEndElement();
        s.W.WriteStartElement("w:r");
        s.W.WriteStartElement("w:commentReference");
        s.W.WriteAttribute("w:id", n);
        s.W.WriteEndElement();
        s.W.WriteEndElement();
    }

    // ---- runs -------------------------------------------------------------------------------------------------------------------

    private void EmitRun(Story s, MarkupNode r, bool deleted)
    {
        // Some writers nest a run inside a run (inside a hyperlink, for one). That is not valid, so the inner run becomes the outer one's sibling.
        bool open = false;
        MarkupNode rPr = Child(r, Ns2003.Word, "rPr");
        foreach (MarkupNode c in r.ChildElements)
        {
            int ns = Ns2003.Of(c);
            if (ns == Ns2003.Word && c.LocalNameString == "r")
            {
                if (open)
                {
                    s.W.WriteEndElement();
                    open = false;
                }

                EmitRun(s, c, deleted);
                continue;
            }

            if (ns == Ns2003.Annotation)
            {
                // A comment sitting in a run, with no range marked for it, becomes its reference.
                string id = Attr(c, "id") ?? string.Empty;
                if (Attr(c, "type") == "Word.Comment" && !_commentMarked.Contains(id))
                {
                    OpenRun(s, rPr, ref open);
                    s.W.WriteStartElement("w:commentReference");
                    s.W.WriteAttribute("w:id", CommentId(id).ToString(CultureInfo.InvariantCulture));
                    s.W.WriteEndElement();
                }

                continue;
            }

            if (ns == Ns2003.Word && c.LocalNameString is not ("rPr" or "binData"))
            {
                OpenRun(s, rPr, ref open);
                EmitRunContent(s, c, deleted);
            }
        }

        if (open)
        {
            s.W.WriteEndElement();
        }
    }

    private void OpenRun(Story s, MarkupNode rPr, ref bool open)
    {
        if (open)
        {
            return;
        }

        s.W.WriteStartElement("w:r");
        open = true;
        if (!rPr.IsNull)
        {
            EmitRPr(s, rPr, null);
        }
    }

    private void EmitRunContent(Story s, MarkupNode c, bool deleted)
    {
        string local = c.LocalNameString;
        switch (local)
        {
            case "rPr" or "binData" or "lastRenderedPageBreak" or "fldData":
                break;
            case "t":
                s.W.WriteStartElement(deleted ? "w:delText" : "w:t");
                s.W.WriteAttribute("xml:space", "preserve");
                s.W.WriteText(c.InnerText);
                s.W.WriteEndElement();
                break;
            case "instrText":
                s.W.WriteStartElement(deleted ? "w:delInstrText" : "w:instrText");
                s.W.WriteAttribute("xml:space", "preserve");
                s.W.WriteText(c.InnerText);
                s.W.WriteEndElement();
                break;
            case "tab" or "cr" or "noBreakHyphen" or "softHyphen" or "pgNum" or "footnoteRef" or "endnoteRef" or "annotationRef" or "separator" or "continuationSeparator":
                s.W.WriteStartElement("w:" + local);
                s.W.WriteEndElement();
                break;
            case "br":
                s.W.WriteStartElement("w:br");
                foreach (MarkupAttributeRef a in c.Attributes)
                {
                    if (!a.IsNamespaceDeclaration)
                    {
                        string name = Encoding.UTF8.GetString(a.LocalName);
                        s.W.WriteAttribute("w:" + Camel(name), name == "type" ? Camel(a.Value) : a.Value);
                    }
                }

                s.W.WriteEndElement();
                break;
            case "sym":
                s.W.WriteStartElement("w:sym");
                CopyAttributes(s, c);
                s.W.WriteEndElement();
                break;
            case "fldChar":
                s.W.WriteStartElement("w:fldChar");
                CopyAttributes(s, c);
                s.W.WriteEndElement();
                break;
            case "footnote":
                EmitNoteReference(s, c, _footnotes, "w:footnoteReference");
                break;
            case "endnote":
                EmitNoteReference(s, c, _endnotes, "w:endnoteReference");
                break;
            case "pict" or "object":
                EmitPicture(s, c);
                break;
            default:
                Unmapped(local);
                break;
        }
    }

    private void CopyAttributes(Story s, MarkupNode n)
    {
        foreach (MarkupAttributeRef a in n.Attributes)
        {
            if (!a.IsNamespaceDeclaration)
            {
                string local = Encoding.UTF8.GetString(a.LocalName);
                if (MapValue(n, n.LocalNameString, local, a.Value) is { } mapped)
                {
                    s.W.WriteAttribute("w:" + Camel(local), mapped);
                }
            }
        }
    }

    private static void EmitNoteReference(Story s, MarkupNode note, List<(int Id, MarkupNode Node)> notes, string element)
    {
        int id = notes.Count + 1;
        notes.Add((id, note));
        s.W.WriteStartElement(element);
        s.W.WriteAttribute("w:id", id.ToString(CultureInfo.InvariantCulture));
        s.W.WriteEndElement();
    }

    // ---- tables -------------------------------------------------------------------------------------------------------------------

    private void EmitTable(Story s, MarkupNode tbl, Revision? rev, int depth)
    {
        s.W.WriteStartElement("w:tbl");
        MarkupNode tblPr = Child(tbl, Ns2003.Word, "tblPr");
        EmitProps(s, tblPr, "w:tblPr", TblPrOrder);

        MarkupNode grid = Child(tbl, Ns2003.Word, "tblGrid");
        s.W.WriteStartElement("w:tblGrid");
        int columns = 0;
        if (!grid.IsNull)
        {
            foreach (MarkupNode g in grid.ChildElements)
            {
                if (g.LocalNameString == "gridCol")
                {
                    s.W.WriteStartElement("w:gridCol");
                    if (Attr(g, "w") is { } w)
                    {
                        s.W.WriteAttribute("w:w", MapValue(g, "gridCol", "w", w) ?? "1440");
                    }

                    s.W.WriteEndElement();
                    columns++;
                }
            }
        }

        if (columns == 0)
        {
            // A table needs a grid: take the widest row.
            int widest = 1;
            foreach (MarkupNode tr in tbl.ChildElements)
            {
                if (tr.LocalNameString == "tr")
                {
                    int count = 0;
                    foreach (MarkupNode tc in tr.ChildElements)
                    {
                        if (tc.LocalNameString == "tc")
                        {
                            count++;
                        }
                    }

                    widest = Math.Max(widest, count);
                }
            }

            for (int i = 0; i < widest; i++)
            {
                s.W.WriteStartElement("w:gridCol");
                s.W.WriteAttribute("w:w", "1440");
                s.W.WriteEndElement();
            }

            Defect(DefectKind.Structure, "wml2003.table-grid", DefectSeverity.Repaired, "A table has no grid; one was made from its widest row.", tbl);
        }

        s.W.WriteEndElement();

        bool anyRow = false;
        foreach (MarkupNode tr in tbl.ChildElements)
        {
            if (Ns2003.Of(tr) == Ns2003.Word && tr.LocalNameString == "tr")
            {
                EmitRow(s, tr, rev, depth);
                anyRow = true;
            }
        }

        if (!anyRow)
        {
            // CT_Tbl needs a row.
            s.W.WriteStartElement("w:tr");
            s.W.WriteStartElement("w:tc");
            s.W.WriteStartElement("w:p");
            s.W.WriteEndElement();
            s.W.WriteEndElement();
            s.W.WriteEndElement();
        }

        s.W.WriteEndElement();
        s.EndsWithParagraph = false;
    }

    private void EmitRow(Story s, MarkupNode tr, Revision? rev, int depth)
    {
        s.W.WriteStartElement("w:tr");
        EmitProps(s, Child(tr, Ns2003.Word, "trPr"), "w:trPr", TrPrOrder);
        var cells = new List<MarkupNode>();
        foreach (MarkupNode tc in tr.ChildElements)
        {
            if (Ns2003.Of(tc) == Ns2003.Word && tc.LocalNameString == "tc")
            {
                cells.Add(tc);
            }
        }

        for (int i = 0; i < cells.Count; i++)
        {
            MarkupNode tc = cells[i];
            MarkupNode tcPr = Child(tc, Ns2003.Word, "tcPr");
            string? hmerge = HMerge(tcPr);
            if (hmerge == "continue")
            {
                continue;
            }

            int span = 1;
            if (hmerge == "restart")
            {
                for (int j = i + 1; j < cells.Count && HMerge(Child(cells[j], Ns2003.Word, "tcPr")) == "continue"; j++)
                {
                    span++;
                }
            }

            s.W.WriteStartElement("w:tc");
            EmitProps(s, tcPr, "w:tcPr", TcPrOrder, gridSpan: span > 1 && (tcPr.IsNull || Child(tcPr, Ns2003.Word, "gridSpan").IsNull) ? span : 0);
            s.EndsWithParagraph = false;
            EmitBlocks(s, tc, rev, depth + 1);
            if (!s.EndsWithParagraph)
            {
                // A cell must end with a paragraph.
                s.W.WriteStartElement("w:p");
                s.W.WriteEndElement();
            }

            s.W.WriteEndElement();
        }

        s.W.WriteEndElement();
    }

    private static string? HMerge(MarkupNode tcPr)
    {
        if (tcPr.IsNull)
        {
            return null;
        }

        MarkupNode h = Child(tcPr, Ns2003.Word, "hmerge");
        return h.IsNull ? null : Attr(h, "val") ?? "continue";
    }

    // ---- pictures and text boxes ---------------------------------------------------------------------------------------------------

    private void EmitPicture(Story s, MarkupNode container)
    {
        var shapes = new List<MarkupNode>();
        foreach (MarkupNode c in container.ChildElements)
        {
            if (Ns2003.Of(c) == Ns2003.Vml && c.LocalNameString is not ("shapetype" or "imagedata"))
            {
                shapes.Add(c);
            }
            else if (Ns2003.Of(c) == Ns2003.Word && c.LocalNameString == "pict" && container.LocalNameString == "object")
            {
                EmitPicture(s, c);
            }
            else if (Ns2003.Of(c) == Ns2003.Office && c.LocalNameString == "OLEObject")
            {
                Lost("loss.ole-objects", "embedded objects", "An embedded object was reduced to its preview picture.");
            }
        }

        foreach (MarkupNode shape in shapes)
        {
            MarkupNode imageData = FindDescendant(shape, Ns2003.Vml, "imagedata", 0);
            MarkupNode textBox = FindDescendant(shape, Ns2003.Word, "txbxContent", 0);
            if (!textBox.IsNull)
            {
                EmitTextBox(s, shape, textBox);
            }
            else if (!imageData.IsNull)
            {
                EmitImage(s, shape, imageData);
            }
            else
            {
                Lost("loss.shapes", "drawing shapes", "Drawing shapes without a picture or text were dropped.");
            }
        }
    }

    private static MarkupNode FindDescendant(MarkupNode n, int ns, string local, int depth)
    {
        if (depth > 32)
        {
            return default;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (Ns2003.Of(c) == ns && c.LocalNameString == local)
            {
                return c;
            }

            MarkupNode deeper = FindDescendant(c, ns, local, depth + 1);
            if (!deeper.IsNull)
            {
                return deeper;
            }
        }

        return default;
    }

    // CSS-style lengths from the VML style attribute ("width:120pt;height:60pt"), in EMU; 12,700 EMU to the point.
    private static long StyleLength(string? style, string property, long fallback)
    {
        if (style is null)
        {
            return fallback;
        }

        foreach (string part in style.Split(';'))
        {
            int colon = part.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0 || !part[..colon].Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string v = part[(colon + 1)..].Trim();
            int i = 0;
            while (i < v.Length && (char.IsAsciiDigit(v[i]) || v[i] is '.' or '-'))
            {
                i++;
            }

            if (!double.TryParse(v.AsSpan(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || number <= 0 || number > 1e6)
            {
                return fallback;
            }

            string unit = v[i..].Trim().ToLowerInvariant();
            double points = unit switch
            {
                "in" => number * 72,
                "cm" => number * 72 / 2.54,
                "mm" => number * 72 / 25.4,
                "px" => number * 0.75,
                "pc" => number * 12,
                _ => number,
            };
            return (long)Math.Clamp(points * 12700, 12700, 5_000_000_000L);
        }

        return fallback;
    }

    private void EmitImage(Story s, MarkupNode shape, MarkupNode imageData)
    {
        string? src = Attr(imageData, "src");
        if (src is null || !_binData.TryGetValue(src, out MarkupNode bin))
        {
            Defect(DefectKind.Reference, "wml2003.missing-picture", DefectSeverity.Dropped, "A picture refers to data (" + (src ?? "no source") + ") that the file does not contain.", imageData);
            Lost("loss.pictures", "pictures", "Pictures whose data is not in the file were dropped.");
            return;
        }

        if (!_mediaParts.TryGetValue(src, out string? partName))
        {
            byte[]? data = DecodeBase64(bin);
            if (data is null)
            {
                Defect(DefectKind.Encoding, "wml2003.bad-base64", DefectSeverity.Dropped, "Picture data " + src + " is not valid base64.", bin);
                Lost("loss.pictures", "pictures", "Pictures whose data could not be decoded were dropped.");
                return;
            }

            // The picture's own bytes name its type; the name in the file is only a fallback (LibreOffice writes names with no extension).
            string ext = SniffImage(data) ?? (src.Contains('.', StringComparison.Ordinal) ? src[(src.LastIndexOf('.') + 1)..].ToLowerInvariant() : "bin");
            string contentType = ext switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "tif" or "tiff" => "image/tiff",
                "wmf" => "image/x-wmf",
                "emf" => "image/x-emf",
                _ => "application/octet-stream",
            };
            _mediaCount++;
            partName = "/word/media/image" + _mediaCount.ToString(CultureInfo.InvariantCulture) + "." + ext;
            _mediaParts[src] = partName;
            _mediaFiles.Add((partName, contentType, data));
        }

        long cx = StyleLength(Attr(shape, "style"), "width", 72 * 12700L);
        long cy = StyleLength(Attr(shape, "style"), "height", 72 * 12700L);
        string rid = s.AddRel(RelBase + "image", partName[5..]);
        string name = Attr(imageData, "title") ?? Attr(shape, "alt") ?? "Picture " + _drawingId.ToString(CultureInfo.InvariantCulture);
        string id = (_drawingId++).ToString(CultureInfo.InvariantCulture);
        string cxs = cx.ToString(CultureInfo.InvariantCulture);
        string cys = cy.ToString(CultureInfo.InvariantCulture);

        XmlWriter w = s.W;
        w.WriteStartElement("w:drawing");
        w.WriteStartElement("wp:inline");
        w.WriteStartElement("wp:extent");
        w.WriteAttribute("cx", cxs);
        w.WriteAttribute("cy", cys);
        w.WriteEndElement();
        w.WriteStartElement("wp:docPr");
        w.WriteAttribute("id", id);
        w.WriteAttribute("name", name);
        if (Attr(shape, "alt") is { Length: > 0 } alt)
        {
            w.WriteAttribute("descr", alt);
        }

        w.WriteEndElement();
        w.WriteStartElement("a:graphic");
        w.WriteStartElement("a:graphicData");
        w.WriteAttribute("uri", PicNs);
        w.WriteStartElement("pic:pic");
        w.WriteStartElement("pic:nvPicPr");
        w.WriteStartElement("pic:cNvPr");
        w.WriteAttribute("id", id);
        w.WriteAttribute("name", name);
        w.WriteEndElement();
        w.WriteStartElement("pic:cNvPicPr");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("pic:blipFill");
        w.WriteStartElement("a:blip");
        w.WriteAttribute("r:embed", rid);
        w.WriteEndElement();
        w.WriteStartElement("a:stretch");
        w.WriteStartElement("a:fillRect");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("pic:spPr");
        w.WriteStartElement("a:xfrm");
        w.WriteStartElement("a:off");
        w.WriteAttribute("x", "0");
        w.WriteAttribute("y", "0");
        w.WriteEndElement();
        w.WriteStartElement("a:ext");
        w.WriteAttribute("cx", cxs);
        w.WriteAttribute("cy", cys);
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("a:prstGeom");
        w.WriteAttribute("prst", "rect");
        w.WriteStartElement("a:avLst");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static string? SniffImage(byte[] d)
    {
        if (d.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }))
        {
            return "png";
        }

        if (d.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "jpg";
        }

        if (d.AsSpan().StartsWith("GIF8"u8))
        {
            return "gif";
        }

        if (d.AsSpan().StartsWith("BM"u8))
        {
            return "bmp";
        }

        if (d.AsSpan().StartsWith(new byte[] { 0x49, 0x49, 0x2A, 0x00 }) || d.AsSpan().StartsWith(new byte[] { 0x4D, 0x4D, 0x00, 0x2A }))
        {
            return "tiff";
        }

        if (d.AsSpan().StartsWith(new byte[] { 0xD7, 0xCD, 0xC6, 0x9A }))
        {
            return "wmf";
        }

        return d.Length > 44 && d.AsSpan(40, 4).SequenceEqual(" EMF"u8) ? "emf" : null;
    }

    private byte[]? DecodeBase64(MarkupNode bin)
    {
        string text = bin.InnerText;
        var clean = new char[text.Length];
        int n = 0;
        foreach (char ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                clean[n++] = ch;
            }
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), _ctx.Limits.MaxAllocationBytes, n / 4 * 3, DefectLocation.InPart("/", Math.Max(0, bin.SourceStart)));
        var buffer = new byte[(n / 4 * 3) + 3];
        return Convert_TryFromBase64(clean.AsSpan(0, n), buffer, out int written) ? buffer.AsSpan(0, written).ToArray() : null;
    }

    private static bool Convert_TryFromBase64(ReadOnlySpan<char> chars, Span<byte> destination, out int written) =>
        System.Convert.TryFromBase64Chars(chars, destination, out written);

    // A text box stays VML (valid in transitional WordprocessingML) so its position and frame survive; its text is converted.
    private void EmitTextBox(Story s, MarkupNode shape, MarkupNode content)
    {
        XmlWriter w = s.W;
        _shapeCount++;
        w.WriteStartElement("w:pict");
        w.WriteStartElement("v:shapetype");
        w.WriteAttribute("id", "_x0000_t202");
        w.WriteAttribute("coordsize", "21600,21600");
        w.WriteAttribute("o:spt", "202");
        w.WriteAttribute("path", "m,l,21600r21600,l21600,xe");
        w.WriteStartElement("v:stroke");
        w.WriteAttribute("joinstyle", "miter");
        w.WriteEndElement();
        w.WriteStartElement("v:path");
        w.WriteAttribute("gradientshapeok", "t");
        w.WriteAttribute("o:connecttype", "rect");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("v:shape");
        w.WriteAttribute("id", "TextBox" + _shapeCount.ToString(CultureInfo.InvariantCulture));
        w.WriteAttribute("type", "#_x0000_t202");
        foreach (MarkupAttributeRef a in shape.Attributes)
        {
            string local = Encoding.UTF8.GetString(a.LocalName);
            if (a.IsNamespaceDeclaration || a.Prefix.Length > 0 || local is "id" or "type" or "spid")
            {
                continue;
            }

            if (local is "style" or "fillcolor" or "strokecolor" or "stroked" or "filled" or "strokeweight")
            {
                w.WriteAttribute(local, a.Value);
            }
        }

        w.WriteStartElement("v:textbox");
        MarkupNode textbox = FindDescendant(shape, Ns2003.Vml, "textbox", 0);
        if (!textbox.IsNull && Attr(textbox, "inset") is { Length: > 0 } inset)
        {
            w.WriteAttribute("inset", inset);
        }

        w.WriteStartElement("w:txbxContent");
        bool saved = s.EndsWithParagraph;
        EmitBlocks(s, content, null, 8);
        if (!s.EndsWithParagraph)
        {
            w.WriteStartElement("w:p");
            w.WriteEndElement();
        }

        s.EndsWithParagraph = saved;
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }
}
