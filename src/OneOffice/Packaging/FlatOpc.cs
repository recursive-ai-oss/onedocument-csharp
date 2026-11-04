using System.Text;
using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Packaging;

/// <summary>
/// Reads and writes Flat OPC: a package as one XML document (<c>pkg:package</c> holding one <c>pkg:part</c> per part, with
/// <c>pkg:xmlData</c> or base64 <c>pkg:binaryData</c>). The form is the one Word and PowerPoint save as "XML document" and
/// "Flat OPC"; the vocabulary is the <c>http://schemas.microsoft.com/office/2006/xmlPackage</c> namespace, taken from files those
/// applications write and from the package schemas in ECMA-376 Part 2 (the packaging model each <c>pkg:part</c> stands for).
/// </summary>
internal static class FlatOpc
{
    public const string Namespace = "http://schemas.microsoft.com/office/2006/xmlPackage";

    /// <summary>One part as found in the flat form.</summary>
    internal readonly record struct FlatPart(string Name, string ContentType, ReadOnlyMemory<byte> Content);

    /// <summary>Tells whether the bytes look like XML rather than a zip: the first non-blank, non-BOM byte is '&lt;'.</summary>
    public static bool LooksFlat(ReadOnlySpan<byte> prefix)
    {
        int i = 0;
        if (prefix.Length >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
        {
            i = 3;
        }

        while (i < prefix.Length && prefix[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            i++;
        }

        return i < prefix.Length && prefix[i] == (byte)'<';
    }

    /// <summary>Reads every part. Damaged parts are dropped with a defect; a document that is not Flat OPC is fatal.</summary>
    public static List<FlatPart> Read(ReadOnlyMemory<byte> xml, OfficeLimits limits, IDefectSink defects, string label)
    {
        var parts = new List<FlatPart>();
        var reader = new OpcXmlReader(xml, limits, label);
        try
        {
            if (!reader.Read() || reader.Token != OpcXmlToken.StartElement || reader.LocalName != "package")
            {
                throw new OfficeFormatException(DefectKind.Packaging, "packaging.bad-flat-opc", "The document is not a Flat OPC package (no pkg:package root).", DefectLocation.InPart(label, 0));
            }

            int count = 0;
            while (reader.Read())
            {
                if (reader.Token == OpcXmlToken.EndElement && reader.Depth == 1)
                {
                    break;
                }

                if (reader.Token != OpcXmlToken.StartElement || reader.Depth != 2)
                {
                    continue;
                }

                if (reader.LocalName != "part")
                {
                    reader.SkipToEnd();
                    continue;
                }

                string? name = reader.GetAttribute("name");
                string? type = reader.GetAttribute("contentType");
                int partStart = reader.TokenStart;
                ReadOnlyMemory<byte> content = default;
                bool haveContent = false;
                bool damaged = false;
                if (!reader.IsEmptyElement)
                {
                    while (reader.Read() && !(reader.Token == OpcXmlToken.EndElement && reader.Depth == 2))
                    {
                        if (reader.Token != OpcXmlToken.StartElement || reader.Depth != 3)
                        {
                            continue;
                        }

                        if (reader.LocalName == "xmlData")
                        {
                            int innerStart = reader.TokenEnd;
                            int innerEnd = reader.SkipToEnd();
                            content = Trim(reader.Data, innerStart, innerEnd);
                            haveContent = true;
                        }
                        else if (reader.LocalName == "binaryData")
                        {
                            var text = new StringBuilder();
                            if (!reader.IsEmptyElement)
                            {
                                while (reader.Read() && !(reader.Token == OpcXmlToken.EndElement && reader.Depth == 3))
                                {
                                    if (reader.Token == OpcXmlToken.Text)
                                    {
                                        text.Append(reader.Text);
                                        if (text.Length / 4 * 3 > limits.MaxPartSize)
                                        {
                                            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), limits.MaxPartSize, text.Length / 4 * 3, DefectLocation.InPart(name ?? label, partStart));
                                        }
                                    }
                                    else if (reader.Token == OpcXmlToken.StartElement)
                                    {
                                        reader.SkipToEnd();
                                    }
                                }
                            }

                            try
                            {
                                content = Convert.FromBase64String(text.ToString());
                                haveContent = true;
                            }
                            catch (FormatException)
                            {
                                damaged = true;
                            }
                        }
                        else
                        {
                            reader.SkipToEnd();
                        }
                    }
                }

                if (damaged || !PartUri.IsValid(name) || type is null)
                {
                    ContentTypes.Report(defects, "packaging.bad-flat-part", DefectSeverity.Dropped, "A pkg:part has no valid name, content type or data; it is dropped.", name ?? label, partStart);
                    continue;
                }

                if (!haveContent)
                {
                    content = ReadOnlyMemory<byte>.Empty;
                }

                count++;
                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), limits.MaxPartCount, count, DefectLocation.InPart(name!, partStart));
                parts.Add(new FlatPart(name!, type, content));
            }
        }
        catch (OpcXmlException ex)
        {
            throw new OfficeFormatException(
                DefectKind.Packaging,
                ex.IsDtd ? "packaging.dtd-refused" : "packaging.bad-flat-opc",
                ex.Message,
                DefectLocation.InPart(label, ex.Offset),
                ex);
        }

        return parts;
    }

    // The part's text is the xmlData content without the white space the wrapper adds around it.
    private static ReadOnlyMemory<byte> Trim(ReadOnlyMemory<byte> data, int start, int end)
    {
        ReadOnlySpan<byte> s = data.Span;
        while (start < end && s[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            start++;
        }

        while (end > start && s[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            end--;
        }

        return data.Slice(start, end - start);
    }

    /// <summary>
    /// Writes the flat form. XML parts go in as <c>pkg:xmlData</c> (without their XML declaration, which the wrapper cannot hold) and
    /// everything else as base64 <c>pkg:binaryData</c>, in the order given.
    /// </summary>
    /// <param name="destination">The stream to write UTF-8 to.</param>
    /// <param name="parts">The parts, in order, with their bytes loaded on demand.</param>
    /// <param name="applicationProgId">The program id for the <c>mso-application</c> instruction, or null for none.</param>
    /// <param name="limits">The limits (XML depth for the check that a part is embeddable).</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static void Write(Stream destination, IEnumerable<(string Name, string ContentType, Func<ReadOnlyMemory<byte>> Content)> parts, string? applicationProgId, OfficeLimits limits, CancellationToken cancellationToken)
    {
        using var w = new StreamWriter(destination, new UTF8Encoding(false), 1 << 16, leaveOpen: true) { NewLine = "\n" };
        w.Write("<?xml version=\"1.0\" standalone=\"yes\"?>\n");
        if (applicationProgId is not null)
        {
            w.Write("<?mso-application progid=\"" + applicationProgId + "\"?>\n");
        }

        w.Write("<pkg:package xmlns:pkg=\"" + Namespace + "\">");
        foreach ((string name, string type, Func<ReadOnlyMemory<byte>> load) in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyMemory<byte> content = load();
            w.Write("<pkg:part pkg:name=\"" + OpcXmlWriter.Escape(name) + "\" pkg:contentType=\"" + OpcXmlWriter.Escape(type) + "\"");
            if (TryEmbeddable(content, type, limits, out ReadOnlyMemory<byte> body))
            {
                w.Write("><pkg:xmlData>");
                w.Flush();
                destination.Write(body.Span);
                w.Write("</pkg:xmlData></pkg:part>");
            }
            else
            {
                w.Write(" pkg:compression=\"store\"><pkg:binaryData>");
                WriteBase64(w, content.Span);
                w.Write("</pkg:binaryData></pkg:part>");
            }
        }

        w.Write("</pkg:package>");
        w.Flush();
    }

    // A part is embedded as XML only when it is an XML media type and the text really is a well-formed UTF-8 document: an
    // XML declaration (the only thing removed) cannot appear inside the wrapper, and a malformed part would damage the whole file.
    private static bool TryEmbeddable(ReadOnlyMemory<byte> content, string type, OfficeLimits limits, out ReadOnlyMemory<byte> body)
    {
        body = default;
        bool xmlType = type.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || type.Equals("text/xml", StringComparison.OrdinalIgnoreCase);
        if (!xmlType || content.Length == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> s = content.Span;
        int start = 0;
        if (s.Length >= 3 && s[0] == 0xEF && s[1] == 0xBB && s[2] == 0xBF)
        {
            start = 3;
        }
        else if (s.Length >= 2 && ((s[0] == 0xFF && s[1] == 0xFE) || (s[0] == 0xFE && s[1] == 0xFF)))
        {
            return false;
        }

        if (s[start..].StartsWith("<?xml"u8) && s.Length > start + 5 && s[start + 5] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            int end = s[start..].IndexOf("?>"u8);
            if (end < 0)
            {
                return false;
            }

            start += end + 2;
            while (start < s.Length && s[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                start++;
            }
        }

        if (start >= s.Length || s[start] != (byte)'<')
        {
            return false;
        }

        ReadOnlyMemory<byte> candidate = content[start..];
        try
        {
            var r = new OpcXmlReader(candidate, limits, "flat");
            while (r.Read())
            {
            }
        }
        catch (OpcXmlException)
        {
            return false;
        }
        catch (OfficeLimitException)
        {
            return false;
        }

        body = candidate;
        return true;
    }

    private static void WriteBase64(StreamWriter w, ReadOnlySpan<byte> data)
    {
        // Word wraps base64 at 76 columns; 57 input bytes make one such line.
        const int Chunk = 57;
        char[] line = new char[76];
        for (int i = 0; i < data.Length; i += Chunk)
        {
            ReadOnlySpan<byte> piece = data.Slice(i, Math.Min(Chunk, data.Length - i));
            Convert.TryToBase64Chars(piece, line, out int n);
            w.Write(line, 0, n);
            if (i + Chunk < data.Length)
            {
                w.Write('\n');
            }
        }
    }
}
