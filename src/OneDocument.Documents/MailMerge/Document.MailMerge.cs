using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Templating;
using RecursiveAI.OneDocument.Documents.MailMerge;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    /// <summary>
    /// Merges records into this document as Word's mail merge does. <c>MERGEFIELD</c> (with <c>\b</c>, <c>\f</c>, <c>\#</c>,
    /// <c>\@</c> and <c>\*</c> case formats), <c>IF</c>, <c>NEXT</c>, <c>NEXTIF</c>, <c>SKIPIF</c>, <c>MERGEREC</c>, <c>MERGESEQ</c>,
    /// <c>SET</c>, <c>ASK</c>, <c>FILLIN</c> and <c>REF</c> to a <c>SET</c>/<c>ASK</c> variable are replaced by their results,
    /// whether simple or complex, split over runs, nested in each other's instructions or in a header, footer or text box.
    /// Other fields are left as they are. The <c>mailMerge</c> settings and the connection they name are removed from every
    /// output; nothing is ever opened from them.
    /// </summary>
    /// <param name="data">
    /// The records: any data the template engine reads (a list of POCOs with <c>[TemplateModel]</c>, <c>JsonDocument</c>,
    /// dictionaries, <c>DataTable</c>, <c>IDataReader</c>). A single record (not a sequence) is a merge of one. Field names are matched
    /// ignoring case, and an underscore in a field name matches a space in the data name.
    /// </param>
    /// <param name="options">The mode and answers; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the merge between passes.</param>
    /// <returns>The merged documents and the warnings. The template is not changed.</returns>
    /// <exception cref="OfficeLimitException">The data holds more records than <see cref="MailMergeOptions.MaxRecords"/>.</exception>
    /// <exception cref="OfficeUnsupportedException">The data is an object the template engine cannot read without reflection.</exception>
    public MailMergeResult MailMerge(object? data, MailMergeOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new MailMergeOptions();
        ITemplateData root = TemplateData.From(data, options.MaxRecords + 1);
        IReadOnlyList<ITemplateData> records = root.Kind switch
        {
            TemplateDataKind.Sequence => root.Items,
            TemplateDataKind.Null => [],
            _ => [root],
        };
        if (records.Count > options.MaxRecords)
        {
            throw new OfficeLimitException("MaxRecords", options.MaxRecords, records.Count);
        }

        CommitChanges();
        OfficeFormat format = Format is OfficeFormat.Docm or OfficeFormat.Dotm ? OfficeFormat.Docm : OfficeFormat.Docx;
        using var template = new MemoryStream();
        SaveAs(template, format, cancellationToken);
        byte[] bytes = template.ToArray();

        var state = new MergeState(records, options);
        var documents = new List<Document>();
        var single = options.Mode == MailMergeMode.SingleDocument;
        Document? baseDocument = null;
        Document? previous = null;
        var combined = new StringBuilder();
        int index = 0;
        try
        {
            while (state.Position < records.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.Skip = false;
                state.Exhausted = false;
                Document copy = Open(new MemoryStream(bytes), new OpenContext(Limits), leaveOpen: false, cancellationToken: cancellationToken);
                bool keep = false;
                try
                {
                    var pass = new MergePass(copy, state);
                    foreach (string header in copy.HeaderFooterPartNames())
                    {
                        if (copy.Parts.Load(header, required: false) is { } part && !part.Root.IsNull)
                        {
                            pass.Run(new MarkupRef(part.Root), header);
                        }
                    }

                    pass.Run(copy.Body.Container, copy.MainPartName);
                    if (!state.Skip)
                    {
                        copy.RemoveMailMergeSettings();
                        state.MergedCount++;
                        keep = true;
                    }
                }
                finally
                {
                    if (!keep)
                    {
                        copy.Dispose();
                    }
                }

                state.Position++;
                if (!keep)
                {
                    continue;
                }

                if (!single)
                {
                    documents.Add(copy);
                    continue;
                }

                baseDocument ??= copy;
                if (previous is not null)
                {
                    previous.AppendMergedBody(combined, index - 1, followedByAnother: true);
                    if (previous != baseDocument)
                    {
                        previous.Dispose();
                    }
                }

                previous = copy;
                index++;
            }

            if (single && previous is not null && baseDocument is not null)
            {
                previous.AppendMergedBody(combined, index - 1, followedByAnother: false);
                if (previous != baseDocument)
                {
                    previous.Dispose();
                }

                if (index > 1)
                {
                    baseDocument.ReplaceBodyContent(combined.ToString());
                }

                documents.Add(baseDocument);
            }
        }
        catch
        {
            foreach (Document d in documents)
            {
                d.Dispose();
            }

            if (single)
            {
                if (previous is not null && previous != baseDocument)
                {
                    previous.Dispose();
                }

                baseDocument?.Dispose();
            }

            throw;
        }

        return new MailMergeResult(documents, state.Warnings);
    }

    internal IEnumerable<string> HeaderFooterPartNames()
    {
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (r.TargetMode == RelationshipTargetMode.Internal && (RelationshipTypes.Is(r.Type, "header") || RelationshipTypes.Is(r.Type, "footer"))
                && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri))
            {
                yield return uri.Value;
            }
        }
    }

    // ECMA-376 Part 1 §17.15.1.5 (mailMerge): the settings element names the data source and header source by relationship, both
    // external connections. A merged document is no longer a main document, so the element and the relationships go.
    internal void RemoveMailMergeSettings()
    {
        MarkupDocument? settings = Parts.LoadRelated(MainPartName, "settings", out string? name);
        if (settings is null || name is null || settings.Root.IsNull)
        {
            return;
        }

        MarkupNode merge = HubXml.FirstChildElement(settings.Root, "mailMerge");
        if (merge.IsNull)
        {
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkupNode n in merge.ChildElements)
        {
            if (n.GetAttribute(RelationshipNamespace, "id") is { } id)
            {
                ids.Add(id);
            }
        }

        merge.Remove();
        if (ids.Count > 0)
        {
            IReadOnlyList<Relationship> all = Package.GetRelationships(name);
            var kept = new List<Relationship>();
            foreach (Relationship r in all)
            {
                if (!ids.Contains(r.Id))
                {
                    kept.Add(r);
                }
            }

            if (kept.Count != all.Count)
            {
                Package.SetRelationships(name, kept);
            }
        }
    }

    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly Regex IdAttribute = new(
        @"(<(?:\w+:)?(?:bookmarkStart|bookmarkEnd|ins|del|moveFrom|moveTo|docPr)\b[^>]*?[\s:]id="")(\d+)("")",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BookmarkName = new(
        @"(<(?:\w+:)?bookmarkStart\b[^>]*?[\s:]name="")([^""]*)("")",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private MarkupNode FinalSectionProperties()
    {
        MarkupNode last = default;
        foreach (MarkupNode n in Body.Container.Node.ChildElements)
        {
            if (n.LocalNameString == "sectPr" && HubXml.IsWordNamespace(n.NamespaceUri))
            {
                last = n;
            }
        }

        return last;
    }

    // Appends this pass's body to a combined body. A record that is followed by another ends with a next-page section break (Word's
    // "merge to new document"): the break is the final section's properties, carried by the record's last paragraph.
    internal void AppendMergedBody(StringBuilder into, int recordIndex, bool followedByAnother)
    {
        MarkupNode body = Body.Container.Node;
        MarkupNode sect = FinalSectionProperties();
        if (followedByAnother && !sect.IsNull)
        {
            AddSectionBreak(body, sect);
        }

        var xml = new StringBuilder();
        foreach (MarkupNode n in body.ChildElements)
        {
            if (n == sect)
            {
                continue;
            }

            xml.Append(n.OuterXml);
        }

        string text = xml.ToString();
        if (recordIndex > 0)
        {
            // Ids that must be unique in a part are shifted per record so the copies do not collide.
            int shift = recordIndex * 10_000;
            text = IdAttribute.Replace(text, m => m.Groups[1].Value + (long.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) + shift).ToString(System.Globalization.CultureInfo.InvariantCulture) + m.Groups[3].Value);
            text = BookmarkName.Replace(text, m =>
            {
                string renamed = m.Groups[2].Value + "_" + recordIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (renamed.Length > 40)
                {
                    renamed = renamed[..(40 - 1 - recordIndex.ToString(System.Globalization.CultureInfo.InvariantCulture).Length)] + "_" + recordIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return m.Groups[1].Value + renamed + m.Groups[3].Value;
            });
        }

        into.Append(text);
    }

    private static void AddSectionBreak(MarkupNode body, MarkupNode sect)
    {
        string prefix = Encoding.UTF8.GetString(sect.Prefix);
        string p = prefix.Length == 0 ? string.Empty : prefix + ":";
        string sectXml = ForceNextPage(sect.OuterXml, p);
        MarkupNode lastBlock = default;
        foreach (MarkupNode n in body.ChildElements)
        {
            if (n != sect)
            {
                lastBlock = n;
            }
        }

        if (!lastBlock.IsNull && lastBlock.LocalNameString == "p")
        {
            MarkupNode pPr = HubXml.FirstChildElement(lastBlock, "pPr");
            if (pPr.IsNull)
            {
                lastBlock.PrependXml(HubXml.Bytes("<" + p + "pPr>" + sectXml + "</" + p + "pPr>"));
            }
            else if (HubXml.FirstChildElement(pPr, "sectPr").IsNull)
            {
                // ECMA-376 Part 1 §17.3.1.26 (pPr): sectPr follows rPr and precedes pPrChange.
                MarkupNode change = HubXml.FirstChildElement(pPr, "pPrChange");
                if (change.IsNull)
                {
                    pPr.AppendXml(HubXml.Bytes(sectXml));
                }
                else
                {
                    change.InsertXmlBefore(HubXml.Bytes(sectXml));
                }
            }

            return;
        }

        string paragraph = "<" + p + "p><" + p + "pPr>" + sectXml + "</" + p + "pPr></" + p + "p>";
        if (lastBlock.IsNull)
        {
            sect.InsertXmlBefore(HubXml.Bytes(paragraph));
        }
        else
        {
            lastBlock.InsertXmlAfter(HubXml.Bytes(paragraph));
        }
    }

    // ECMA-376 Part 1 §17.6.22 (type): the section break type; it comes before pgSz in sectPr.
    private static string ForceNextPage(string sectXml, string p)
    {
        int type = sectXml.IndexOf("<" + p + "type", StringComparison.Ordinal);
        if (type >= 0)
        {
            int end = sectXml.IndexOf('>', type);
            return sectXml[..type] + "<" + p + "type " + p + "val=\"nextPage\"/>" + sectXml[(end + 1)..];
        }

        int pgSz = sectXml.IndexOf("<" + p + "pgSz", StringComparison.Ordinal);
        string insert = "<" + p + "type " + p + "val=\"nextPage\"/>";
        if (pgSz >= 0)
        {
            return sectXml[..pgSz] + insert + sectXml[pgSz..];
        }

        int close = sectXml.LastIndexOf("</" + p + "sectPr>", StringComparison.Ordinal);
        return close >= 0 ? sectXml[..close] + insert + sectXml[close..] : sectXml;
    }

    internal void ReplaceBodyContent(string xml)
    {
        MarkupNode body = Body.Container.Node;
        MarkupNode sect = FinalSectionProperties();
        var doomed = new List<MarkupNode>();
        foreach (MarkupNode n in body.Children)
        {
            if (n != sect)
            {
                doomed.Add(n);
            }
        }

        foreach (MarkupNode n in doomed)
        {
            n.Remove();
        }

        if (sect.IsNull)
        {
            body.AppendXml(HubXml.Bytes(xml));
            return;
        }

        sect.InsertXmlBefore(HubXml.Bytes(xml));
        // Every record starts on a new page, the last included.
        MarkupNode type = HubXml.FirstChildElement(sect, "type");
        if (!type.IsNull)
        {
            type.SetAttribute(type.NamespaceUri, "val", "nextPage", "w");
        }
        else
        {
            string p = Encoding.UTF8.GetString(sect.Prefix);
            p = p.Length == 0 ? string.Empty : p + ":";
            MarkupNode pgSz = HubXml.FirstChildElement(sect, "pgSz");
            string element = "<" + p + "type " + p + "val=\"nextPage\"/>";
            if (pgSz.IsNull)
            {
                sect.AppendXml(HubXml.Bytes(element));
            }
            else
            {
                pgSz.InsertXmlBefore(HubXml.Bytes(element));
            }
        }
    }
}
