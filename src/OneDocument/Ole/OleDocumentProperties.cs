using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Ole;

/// <summary>
/// Maps the property streams of the legacy formats (<c>\u0005SummaryInformation</c> and <c>\u0005DocumentSummaryInformation</c>, the
/// latter with its user-defined set) to and from the document properties of the hub ([MS-OSHARED] §2.3.3.2).
/// </summary>
/// <remarks>
/// <para><b>To the hub</b> (<see cref="Read"/>): title, subject, author, keywords, comments, last author, revision, the dates, the counts,
/// the application and the security flags of the summary stream become core and extended properties; the document summary stream's
/// category, manager, company, statistics, heading pairs and parts, application version and content status become extended and
/// core properties; every named user-defined property becomes a custom property, and the hyperlink base becomes the extended
/// <c>HyperlinkBase</c>. What has no counterpart (the thumbnail, the VBA signature, the hyperlink list, blobs, vectors and other
/// custom types the custom-property part has no element for, property identifiers this library does not know) is returned as a list of
/// descriptions, for the caller's loss report. The byte count, which Office recomputes on save and the package format has no element for,
/// is dropped without a report.</para>
/// <para><b>From the hub</b> (<see cref="Create"/>): the reverse, writing text as UTF-16 (code page 1200) so that any character survives.
/// Nothing reads the clock: a date is written only if the hub has it.</para>
/// </remarks>
public static class OleDocumentProperties
{
    private const string LinkBaseName = "_PID_LINKBASE";
    private const string HyperlinksName = "_PID_HLINKS";
    private const string GuidName = "_PID_GUID";
    private const long TicksPerMinute = 600_000_000L;

    /// <summary>Reads the two property streams into the hub's properties.</summary>
    /// <param name="summaryInformation">The bytes of <c>\u0005SummaryInformation</c>; empty when the file has none.</param>
    /// <param name="documentSummaryInformation">The bytes of <c>\u0005DocumentSummaryInformation</c>; empty when the file has none.</param>
    /// <param name="target">The properties to fill in; existing values of the same properties are replaced. The caller commits them.</param>
    /// <param name="context">The limits and the defect sink; null for the defaults. A stream that cannot be read is reported there (<c>ole.propset-header</c>) and skipped.</param>
    /// <returns>A description for each property that could not be carried, such as <c>SummaryInformation property 17 (thumbnail)</c>.</returns>
    public static IReadOnlyList<string> Read(ReadOnlyMemory<byte> summaryInformation, ReadOnlyMemory<byte> documentSummaryInformation, DocumentProperties target, OpenContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        context ??= new OpenContext();
        var lost = new List<string>();
        if (!summaryInformation.IsEmpty)
        {
            if (!OlePropertySets.TryRead(summaryInformation, out OlePropertySets? summary, context, "/" + OlePropertySets.SummaryInformationStreamName))
            {
                lost.Add("SummaryInformation: the stream could not be read");
            }
            else if (summary!.FindSet(OlePropertySets.SummaryInformationFormatId) is { } s)
            {
                ReadSummary(s, target, lost);
            }
        }

        if (!documentSummaryInformation.IsEmpty)
        {
            if (!OlePropertySets.TryRead(documentSummaryInformation, out OlePropertySets? doc, context, "/" + OlePropertySets.DocumentSummaryInformationStreamName))
            {
                lost.Add("DocumentSummaryInformation: the stream could not be read");
                return lost;
            }

            if (doc!.FindSet(OlePropertySets.DocumentSummaryInformationFormatId) is { } d)
            {
                ReadDocumentSummary(d, target, lost);
            }

            if (doc.FindSet(OlePropertySets.UserDefinedPropertiesFormatId) is { } u)
            {
                ReadUserDefined(u, target, lost);
            }
        }

        return lost;
    }

    /// <summary>Reports what <see cref="Read"/> could not carry to a loss sink as one item.</summary>
    /// <param name="unmapped">The descriptions <see cref="Read"/> returned.</param>
    /// <param name="loss">The sink.</param>
    /// <param name="code">The <c>loss.*</c> code of the converter.</param>
    public static void ReportLoss(IReadOnlyList<string> unmapped, ILossSink loss, string code)
    {
        ArgumentNullException.ThrowIfNull(unmapped);
        ArgumentNullException.ThrowIfNull(loss);
        if (unmapped.Count == 0)
        {
            return;
        }

        const int Shown = 6;
        string list = string.Join("; ", unmapped.Take(Shown)) + (unmapped.Count > Shown ? "; and " + (unmapped.Count - Shown).ToString(CultureInfo.InvariantCulture) + " more" : string.Empty);
        loss.Report(code, "document properties", unmapped.Count, "Properties with no counterpart in the package were left out: " + list + ".");
    }

    // ---- summary information ----

    private static void ReadSummary(OlePropertySet set, DocumentProperties p, List<string> lost)
    {
        foreach (OleProperty prop in set.Properties)
        {
            OlePropertyValue v = prop.Value;
            switch (prop.Id)
            {
                case 2: SetText(v, t => p.Core.Title = t, "title", prop, lost, "SummaryInformation"); break;
                case 3: SetText(v, t => p.Core.Subject = t, "subject", prop, lost, "SummaryInformation"); break;
                case 4: SetText(v, t => p.Core.Creator = t, "author", prop, lost, "SummaryInformation"); break;
                case 5: SetText(v, t => p.Core.Keywords = t, "keywords", prop, lost, "SummaryInformation"); break;
                case 6: SetText(v, t => p.Core.Description = t, "comments", prop, lost, "SummaryInformation"); break;
                case 7: SetText(v, t => p.Extended.Template = t, "template", prop, lost, "SummaryInformation"); break;
                case 8: SetText(v, t => p.Core.LastModifiedBy = t, "last author", prop, lost, "SummaryInformation"); break;
                case 9: SetText(v, t => p.Core.Revision = t, "revision", prop, lost, "SummaryInformation"); break;
                case 10:
                    // [MS-OSHARED] §2.3.3.2.1.1: the edit time is a duration in 100-nanosecond units stored in a FILETIME.
                    if (v.AsFileTime() is { } edit)
                    {
                        p.Extended.TotalTimeMinutes = (long)Math.Min(edit.Ticks / (ulong)TicksPerMinute, long.MaxValue);
                    }
                    else
                    {
                        Lose(lost, "SummaryInformation", prop, "edit time", "it is not a FILETIME");
                    }

                    break;
                case 11: SetDate(v, d => p.Core.LastPrinted = d, "last printed", prop, lost); break;
                case 12: SetDate(v, d => p.Core.Created = d, "creation date", prop, lost); break;
                case 13: SetDate(v, d => p.Core.Modified = d, "last saved date", prop, lost); break;
                case 14: SetInt(v, n => p.Extended.Pages = n, "page count", prop, lost, "SummaryInformation"); break;
                case 15: SetInt(v, n => p.Extended.Words = n, "word count", prop, lost, "SummaryInformation"); break;
                case 16: SetInt(v, n => p.Extended.Characters = n, "character count", prop, lost, "SummaryInformation"); break;
                case 17: Lose(lost, "SummaryInformation", prop, "thumbnail", "the package format keeps a thumbnail as a separate picture part"); break;
                case 18: SetText(v, t => p.Extended.Application = t, "application", prop, lost, "SummaryInformation"); break;
                case 19: SetInt(v, n => p.Extended.DocumentSecurity = n, "security", prop, lost, "SummaryInformation"); break;
                default: Lose(lost, "SummaryInformation", prop, null, "the property is not one of the standard summary properties"); break;
            }
        }
    }

    // ---- document summary information ----

    private static void ReadDocumentSummary(OlePropertySet set, DocumentProperties p, List<string> lost)
    {
        const string Stream = "DocumentSummaryInformation";
        foreach (OleProperty prop in set.Properties)
        {
            OlePropertyValue v = prop.Value;
            switch (prop.Id)
            {
                case 2: SetText(v, t => p.Core.Category = t, "category", prop, lost, Stream); break;
                case 3: SetText(v, t => p.Extended.SetValue("PresentationFormat", t), "presentation format", prop, lost, Stream); break;
                case 4: break; // the byte count: Office recomputes it and the package format has no element for it
                case 5: SetInt(v, n => p.Extended.Lines = n, "line count", prop, lost, Stream); break;
                case 6: SetInt(v, n => p.Extended.Paragraphs = n, "paragraph count", prop, lost, Stream); break;
                case 7: SetInt(v, n => p.Extended.Slides = n, "slide count", prop, lost, Stream); break;
                case 8: SetInt(v, n => p.Extended.SetValue("Notes", Num(n)), "note count", prop, lost, Stream); break;
                case 9: SetInt(v, n => p.Extended.SetValue("HiddenSlides", Num(n)), "hidden slide count", prop, lost, Stream); break;
                case 10: SetInt(v, n => p.Extended.SetValue("MMClips", Num(n)), "multimedia clip count", prop, lost, Stream); break;
                case 11: SetBool(v, b => p.Extended.SetValue("ScaleCrop", b ? "true" : "false"), "scale", prop, lost, Stream); break;
                case 12: ReadHeadingPairs(v, p, prop, lost); break;
                case 13: ReadDocumentParts(v, p, prop, lost); break;
                case 14: SetText(v, t => p.Extended.Manager = t, "manager", prop, lost, Stream); break;
                case 15: SetText(v, t => p.Extended.Company = t, "company", prop, lost, Stream); break;
                case 16: SetBool(v, b => p.Extended.SetValue("LinksUpToDate", b ? "false" : "true"), "links dirty", prop, lost, Stream); break;
                case 17: SetInt(v, n => p.Extended.CharactersWithSpaces = n, "character count with spaces", prop, lost, Stream); break;
                case 19: SetBool(v, b => p.Extended.SetValue("SharedDoc", b ? "true" : "false"), "shared document", prop, lost, Stream); break;
                case 22: SetBool(v, b => p.Extended.SetValue("HyperlinksChanged", b ? "true" : "false"), "hyperlinks changed", prop, lost, Stream); break;
                case 23:
                    // [MS-OSHARED] §2.3.3.2.2.1: the high 16 bits are the major version and the low 16 the minor, written as MM.mmmm.
                    if (v.AsInt64() is { } ver && ver >= 0)
                    {
                        p.Extended.ApplicationVersion = ((ver >> 16) & 0xFFFF).ToString("00", CultureInfo.InvariantCulture) + "." + (ver & 0xFFFF).ToString("0000", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        Lose(lost, Stream, prop, "application version", "it is not a number");
                    }

                    break;
                case 24: Lose(lost, Stream, prop, "VBA signature", "the project's signature is not carried over"); break;
                case 26: Lose(lost, Stream, prop, "content type", "the package has no such property"); break;
                case 27: SetText(v, t => p.Core.ContentStatus = t, "content status", prop, lost, Stream); break;
                case 28: SetText(v, t => p.Core.Language = t, "language", prop, lost, Stream); break;
                case 29: SetText(v, t => p.Core.Version = t, "document version", prop, lost, Stream); break;
                default: Lose(lost, Stream, prop, null, "the property is not one of the standard document summary properties"); break;
            }
        }
    }

    // [MS-OSHARED] §2.3.3.2.2.1 GKPIDDSI_HEADINGPAIR: a vector of variants, alternately a heading and the number of parts under it.
    private static void ReadHeadingPairs(OlePropertyValue v, DocumentProperties p, OleProperty prop, List<string> lost)
    {
        if (v.Value is not IReadOnlyList<object?> items || v.Type != (OleVarType.VtVector | OleVarType.VtVariant))
        {
            Lose(lost, "DocumentSummaryInformation", prop, "heading pairs", "it is not a vector of variants");
            return;
        }

        var xml = new StringBuilder();
        xml.Append("<{vt}:vector size=\"").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append("\" baseType=\"variant\">");
        foreach (object? item in items)
        {
            if (item is not OlePropertyValue e)
            {
                continue;
            }

            if (e.AsString() is { } text)
            {
                xml.Append("<{vt}:variant><{vt}:lpstr>").Append(PropertyPart.Escape(Clean(text))).Append("</{vt}:lpstr></{vt}:variant>");
            }
            else if (e.AsInt64() is { } n)
            {
                xml.Append("<{vt}:variant><{vt}:i4>").Append(n.ToString(CultureInfo.InvariantCulture)).Append("</{vt}:i4></{vt}:variant>");
            }
        }

        xml.Append("</{vt}:vector>");
        p.Extended.SetStructured("HeadingPairs", xml.ToString());
    }

    private static void ReadDocumentParts(OlePropertyValue v, DocumentProperties p, OleProperty prop, List<string> lost)
    {
        if (v.Value is not IReadOnlyList<object?> items || v.Type is not ((ushort)(OleVarType.VtVector | OleVarType.VtLpstr) or (ushort)(OleVarType.VtVector | OleVarType.VtLpwstr) or (ushort)(OleVarType.VtVector | OleVarType.VtBstr)))
        {
            Lose(lost, "DocumentSummaryInformation", prop, "document parts", "it is not a vector of strings");
            return;
        }

        var xml = new StringBuilder();
        xml.Append("<{vt}:vector size=\"").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append("\" baseType=\"lpstr\">");
        foreach (object? item in items)
        {
            xml.Append("<{vt}:lpstr>").Append(PropertyPart.Escape(Clean(item as string ?? string.Empty))).Append("</{vt}:lpstr>");
        }

        xml.Append("</{vt}:vector>");
        p.Extended.SetStructured("TitlesOfParts", xml.ToString());
    }

    // ---- user-defined properties ----

    private static void ReadUserDefined(OlePropertySet set, DocumentProperties p, List<string> lost)
    {
        const string Stream = "DocumentSummaryInformation";
        foreach (OleProperty prop in set.Properties)
        {
            if (!set.Names.TryGetValue(prop.Id, out string? name) || name.Length == 0)
            {
                Lose(lost, Stream + " user-defined", prop, null, "the property has no name");
                continue;
            }

            name = Clean(name);
            if (name.Length == 0)
            {
                Lose(lost, Stream + " user-defined", prop, null, "its name holds only characters XML cannot carry");
                continue;
            }

            OlePropertyValue v = prop.Value;
            if (name == LinkBaseName)
            {
                // [MS-OSHARED] §2.3.3.2.3.2: a blob holding a null-terminated UTF-16 string.
                if (v.Value is byte[] b)
                {
                    p.Extended.HyperlinkBase = Clean(Encoding.Unicode.GetString(b, 0, b.Length & ~1).TrimEnd('\0'));
                }
                else
                {
                    Lose(lost, Stream + " user-defined", prop, name, "it is not a blob");
                }

                continue;
            }

            if (name == GuidName)
            {
                // The document's identity GUID, which Office regenerates; the package has no property for it.
                continue;
            }

            if (name == HyperlinksName)
            {
                // An empty list is four bytes (a count of zero); a list with entries has no counterpart.
                if (v.Value is not byte[] { Length: <= 4 })
                {
                    Lose(lost, Stream + " user-defined", prop, name, "the list of hyperlinks is not carried over");
                }

                continue;
            }

            if (v.AsString() is { } text)
            {
                p.Custom.SetString(name, Clean(text));
                continue;
            }

            switch (v.Type)
            {
                case OleVarType.VtI2 or OleVarType.VtI4 or OleVarType.VtInt or OleVarType.VtI1 or OleVarType.VtUI1 or OleVarType.VtUI2:
                    p.Custom.SetInt32(name, (int)v.AsInt64()!.Value);
                    break;
                case OleVarType.VtUI4 or OleVarType.VtUInt:
                    {
                        long n = v.AsInt64()!.Value;
                        if (n <= int.MaxValue)
                        {
                            p.Custom.SetInt32(name, (int)n);
                        }
                        else
                        {
                            p.Custom.SetInt64(name, n);
                        }

                        break;
                    }

                case OleVarType.VtI8:
                    p.Custom.SetInt64(name, (long)v.Value!);
                    break;
                case OleVarType.VtUI8 when (ulong)v.Value! <= long.MaxValue:
                    p.Custom.SetInt64(name, (long)(ulong)v.Value!);
                    break;
                case OleVarType.VtR4 or OleVarType.VtR8 when v.AsDouble() is { } r && double.IsFinite(r):
                    p.Custom.SetDouble(name, r);
                    break;
                case OleVarType.VtBool:
                    p.Custom.SetBoolean(name, (bool)v.Value!);
                    break;
                case OleVarType.VtFileTime when v.AsFileTime()!.Value.ToDateTimeOffset() is { } when:
                    p.Custom.SetDateTime(name, when);
                    break;
                case OleVarType.VtDate when v.Value is OleDate od && double.IsFinite(od.Value) && od.Value is > -657000 and < 2958000:
                    p.Custom.SetDateTime(name, new DateTimeOffset(DateTime.SpecifyKind(DateTime.FromOADate(od.Value), DateTimeKind.Utc)));
                    break;
                default:
                    Lose(lost, Stream + " user-defined", prop, name, "its type (0x" + v.Type.ToString("X4", CultureInfo.InvariantCulture) + ") has no custom property equivalent");
                    break;
            }
        }
    }

    // ---- hub to streams ----

    /// <summary>Makes the two property streams from the hub's properties.</summary>
    /// <param name="properties">The properties to write.</param>
    /// <returns>
    /// The bytes of <c>\u0005SummaryInformation</c> and of <c>\u0005DocumentSummaryInformation</c>; each is null when the hub has no property
    /// that belongs in it. Custom properties of a type with no OLE form (none today) are left out. A hub that holds a malformed part
    /// raises the part's own error, as reading the properties does.
    /// </returns>
    public static (byte[]? SummaryInformation, byte[]? DocumentSummaryInformation) Create(DocumentProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        CoreProperties c = properties.Core;
        ExtendedProperties e = properties.Extended;

        var summary = new OlePropertySet(OlePropertySets.SummaryInformationFormatId);
        AddText(summary, 2, c.Title);
        AddText(summary, 3, c.Subject);
        AddText(summary, 4, c.Creator);
        AddText(summary, 5, c.Keywords);
        AddText(summary, 6, c.Description);
        AddText(summary, 7, e.Template);
        AddText(summary, 8, c.LastModifiedBy);
        AddText(summary, 9, c.Revision);
        if (e.TotalTimeMinutes is { } minutes and >= 0 && minutes <= (long)(ulong.MaxValue / (ulong)TicksPerMinute))
        {
            summary.Set(10, OlePropertyValue.FromFileTime((ulong)minutes * (ulong)TicksPerMinute));
        }

        AddDate(summary, 11, c.LastPrinted);
        AddDate(summary, 12, c.Created);
        AddDate(summary, 13, c.Modified);
        AddInt(summary, 14, e.Pages);
        AddInt(summary, 15, e.Words);
        AddInt(summary, 16, e.Characters);
        AddText(summary, 18, e.Application);
        AddInt(summary, 19, e.DocumentSecurity);

        var doc = new OlePropertySet(OlePropertySets.DocumentSummaryInformationFormatId);
        AddText(doc, 2, c.Category);
        AddText(doc, 3, e.GetValue("PresentationFormat"));
        AddInt(doc, 5, e.Lines);
        AddInt(doc, 6, e.Paragraphs);
        AddInt(doc, 7, e.Slides);
        AddInt(doc, 8, Long(e.GetValue("Notes")));
        AddInt(doc, 9, Long(e.GetValue("HiddenSlides")));
        AddInt(doc, 10, Long(e.GetValue("MMClips")));
        if (Bool(e.GetValue("ScaleCrop")) is { } scale)
        {
            doc.Set(11, OlePropertyValue.FromBoolean(scale));
        }

        AddHeadingPairs(doc, e);
        AddDocumentParts(doc, e);
        AddText(doc, 14, e.Manager);
        AddText(doc, 15, e.Company);
        if (Bool(e.GetValue("LinksUpToDate")) is { } upToDate)
        {
            doc.Set(16, OlePropertyValue.FromBoolean(!upToDate));
        }

        AddInt(doc, 17, e.CharactersWithSpaces);
        if (Bool(e.GetValue("SharedDoc")) is { } shared)
        {
            doc.Set(19, OlePropertyValue.FromBoolean(shared));
        }

        if (Bool(e.GetValue("HyperlinksChanged")) is { } changed)
        {
            doc.Set(22, OlePropertyValue.FromBoolean(changed));
        }

        if (e.ApplicationVersion is { } av && ParseVersion(av) is { } version)
        {
            doc.Set(23, OlePropertyValue.FromInt32(version));
        }

        AddText(doc, 27, c.ContentStatus);

        var user = new OlePropertySet(OlePropertySets.UserDefinedPropertiesFormatId);
        if (e.HyperlinkBase is { } baseUrl)
        {
            user.SetNamed(LinkBaseName, OlePropertyValue.FromBlob(Encoding.Unicode.GetBytes(baseUrl + "\0")));
        }

        foreach (CustomProperty cp in properties.Custom.List())
        {
            OlePropertyValue? value = cp.Type switch
            {
                CustomPropertyType.Text => OlePropertyValue.FromString(cp.Text),
                CustomPropertyType.SmallInteger when cp.AsInt32 is { } i => OlePropertyValue.FromInt32(i),
                CustomPropertyType.LongInteger when cp.AsInt64 is { } l => OlePropertyValue.FromInt64(l),
                CustomPropertyType.Real when cp.AsDouble is { } d && double.IsFinite(d) => OlePropertyValue.FromDouble(d),
                CustomPropertyType.Boolean when cp.AsBoolean is { } b => OlePropertyValue.FromBoolean(b),
                CustomPropertyType.DateTime when cp.AsDateTime is { } t => OlePropertyValue.FromDate(t),
                _ => null,
            };
            if (value is not null && cp.Name.Length > 0)
            {
                user.SetNamed(cp.Name, value);
            }
        }

        byte[]? summaryBytes = null;
        if (summary.Properties.Count > 0)
        {
            var s = new OlePropertySets();
            s.Add(summary);
            summaryBytes = s.ToArray();
        }

        byte[]? docBytes = null;
        if (doc.Properties.Count > 0 || user.Properties.Count > 0)
        {
            var d = new OlePropertySets();
            d.Add(doc);
            if (user.Properties.Count > 0)
            {
                d.Add(user);
            }

            docBytes = d.ToArray();
        }

        return (summaryBytes, docBytes);
    }

    private static void AddText(OlePropertySet set, uint id, string? value)
    {
        if (value is not null)
        {
            set.Set(id, OlePropertyValue.FromString(value));
        }
    }

    private static void AddDate(OlePropertySet set, uint id, DateTimeOffset? value)
    {
        if (value is { } d && d.Year >= 1601)
        {
            set.Set(id, OlePropertyValue.FromDate(d));
        }
    }

    private static void AddInt(OlePropertySet set, uint id, long? value)
    {
        if (value is { } n && n >= int.MinValue && n <= int.MaxValue)
        {
            set.Set(id, OlePropertyValue.FromInt32((int)n));
        }
    }

    private static long? Long(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;

    private static bool? Bool(string? text) => text?.Trim() switch { "true" or "1" => true, "false" or "0" => false, _ => null };

    private static int? ParseVersion(string text)
    {
        // ECMA-376 Part 1 §22.2.2.2: AppVersion is XX.YYYY.
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && int.TryParse(text.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out int major) && major <= 0xFFFF
            && int.TryParse(text.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int minor) && minor <= 0xFFFF)
        {
            return (major << 16) | minor;
        }

        return null;
    }

    private static void AddHeadingPairs(OlePropertySet doc, ExtendedProperties e)
    {
        MarkupNode vector = FirstVector(e.FindElement("HeadingPairs"));
        if (vector.IsNull)
        {
            return;
        }

        var items = new List<OlePropertyValue>();
        for (MarkupNode variant = vector.FirstChild; !variant.IsNull; variant = variant.NextSibling)
        {
            if (variant.Kind != MarkupNodeKind.Element || variant.LocalNameString != "variant")
            {
                continue;
            }

            for (MarkupNode v = variant.FirstChild; !v.IsNull; v = v.NextSibling)
            {
                if (v.Kind != MarkupNodeKind.Element)
                {
                    continue;
                }

                if (v.LocalNameString is "lpstr" or "lpwstr")
                {
                    items.Add(OlePropertyValue.FromString(v.InnerText));
                }
                else if (v.LocalNameString is "i4" or "int" && int.TryParse(v.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                {
                    items.Add(OlePropertyValue.FromInt32(n));
                }

                break;
            }
        }

        if (items.Count > 0)
        {
            doc.Set(12, OlePropertyValue.FromVariantVector(items));
        }
    }

    private static void AddDocumentParts(OlePropertySet doc, ExtendedProperties e)
    {
        MarkupNode vector = FirstVector(e.FindElement("TitlesOfParts"));
        if (vector.IsNull)
        {
            return;
        }

        var items = new List<string>();
        for (MarkupNode v = vector.FirstChild; !v.IsNull; v = v.NextSibling)
        {
            if (v.Kind == MarkupNodeKind.Element && v.LocalNameString is "lpstr" or "lpwstr")
            {
                items.Add(v.InnerText);
            }
        }

        if (items.Count > 0)
        {
            doc.Set(13, OlePropertyValue.FromStringVector(items));
        }
    }

    private static MarkupNode FirstVector(MarkupNode parent)
    {
        if (parent.IsNull)
        {
            return default;
        }

        for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == "vector")
            {
                return n;
            }
        }

        return default;
    }

    // ---- small helpers ----

    private static string Num(long n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>Removes the characters XML 1.0 cannot carry, which legacy text can hold.</summary>
    private static string Clean(string text)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            bool bad = (ch < 0x20 && ch is not ('\t' or '\n' or '\r')) || ch is '\uFFFE' or '\uFFFF'
                || (char.IsHighSurrogate(ch) && !(i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])))
                || (char.IsLowSurrogate(ch) && !(i > 0 && char.IsHighSurrogate(text[i - 1])));
            if (bad && sb is null)
            {
                sb = new StringBuilder(text, 0, i, text.Length);
            }

            if (!bad)
            {
                sb?.Append(ch);
            }
        }

        return sb is null ? text : sb.ToString();
    }

    private static void SetText(OlePropertyValue v, Action<string> set, string what, OleProperty prop, List<string> lost, string stream)
    {
        if (v.AsString() is { } t)
        {
            set(Clean(t));
        }
        else
        {
            Lose(lost, stream, prop, what, "it is not a string");
        }
    }

    private static void SetInt(OlePropertyValue v, Action<long> set, string what, OleProperty prop, List<string> lost, string stream)
    {
        if (v.AsInt64() is { } n)
        {
            set(n);
        }
        else
        {
            Lose(lost, stream, prop, what, "it is not a whole number");
        }
    }

    private static void SetBool(OlePropertyValue v, Action<bool> set, string what, OleProperty prop, List<string> lost, string stream)
    {
        if (v.AsBoolean() is { } b)
        {
            set(b);
        }
        else
        {
            Lose(lost, stream, prop, what, "it is not a Boolean");
        }
    }

    private static void SetDate(OlePropertyValue v, Action<DateTimeOffset> set, string what, OleProperty prop, List<string> lost)
    {
        if (v.AsFileTime() is { } ft)
        {
            // A zero FILETIME means the date was never set.
            if (ft.ToDateTimeOffset() is { } d)
            {
                set(d);
            }
            else if (ft.Ticks != 0)
            {
                Lose(lost, "SummaryInformation", prop, what, "the date is out of range");
            }
        }
        else
        {
            Lose(lost, "SummaryInformation", prop, what, "it is not a FILETIME");
        }
    }

    private static void Lose(List<string> lost, string stream, OleProperty prop, string? what, string why) =>
        lost.Add(stream + " property " + prop.Id.ToString(CultureInfo.InvariantCulture) + (what is null ? string.Empty : " (" + what + ")") + ": " + why);
}
