using System.Buffers.Binary;
using System.Linq;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Cfb;
using RecursiveAI.OneArchive.Zip;
using RecursiveAI.OneCompress;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Formats;

/// <summary>
/// Decides what format a file is from its content, never from its extension. It reads a bounded prefix of the source
/// (<see cref="PrefixLength"/> bytes) and, only when the prefix says container or Flat OPC, the least more it needs: for zip the
/// central directory plus the few small parts that name the main document; for a compound file its directory; for Flat OPC up to
/// <see cref="FlatOpcScanLength"/> bytes of text. Nothing is decompressed beyond a few kilobytes and no document is parsed.
/// </summary>
/// <remarks>
/// <para>Order of decision: container signatures first (zip, compound file), then binary signatures (PDF, images, BIFF2–5 streams),
/// RTF, XML by its root element, HTML, and last the delimited-text heuristic. A file that matches nothing is
/// <see cref="OfficeFormat.Unknown"/>; sniffing a damaged or hostile file also returns Unknown rather than throwing, except that a
/// resource limit being hit raises <see cref="OfficeLimitException"/> so a bomb is not mistaken for plain junk.</para>
/// <para>Decisions by container: a zip with an <c>mimetype</c> entry is OpenDocument and the entry's text names the format; otherwise a
/// zip with <c>[Content_Types].xml</c> is OOXML and the content type of the main part, found through the package relationship of
/// type <c>officeDocument</c> (the same spelling in Transitional and Strict), names the format. A compound file is classified by the
/// streams at its root: <c>EncryptedPackage</c> with <c>EncryptionInfo</c>, <c>WordDocument</c>, <c>Workbook</c>,
/// <c>PowerPoint Document</c>, or the older <c>Book</c>. XML is classified by its root element: <c>package</c> in the
/// <c>xmlPackage</c> namespace is Flat OPC (the main part's content type then tells the kind), <c>office:document</c> is flat ODF,
/// the 2003 formats by their processing instruction or namespace.</para>
/// <para>The delimited-text rule is a heuristic and the last resort: at least two lines of valid text with the same count of tabs
/// (Tsv), or commas or semicolons (Csv), outside double quotes, on every complete line of the prefix. Markdown and plain text are
/// never detected from content.</para>
/// </remarks>
public static class FormatSniffer
{
    /// <summary>The number of bytes read first: 4,096.</summary>
    public const int PrefixLength = 4096;

    /// <summary>The most bytes of a Flat OPC file scanned for the main part's content type: 1 MiB.</summary>
    public const int FlatOpcScanLength = 1 << 20;

    private const int RelsReadLimit = 64 * 1024;
    private const int ContentTypesReadLimit = 1 << 20;
    private const string FlatOpcNamespace = "http://schemas.microsoft.com/office/2006/xmlPackage";

    private static readonly byte[] s_pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] s_cfbSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    // Main-part content types: the part named by the package's officeDocument relationship has one of these.
    private static readonly Dictionary<string, OfficeFormat> s_mainContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"] = OfficeFormat.Docx,
        ["application/vnd.ms-word.document.macroEnabled.main+xml"] = OfficeFormat.Docm,
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml"] = OfficeFormat.Dotx,
        ["application/vnd.ms-word.template.macroEnabledTemplate.main+xml"] = OfficeFormat.Dotm,
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"] = OfficeFormat.Xlsx,
        ["application/vnd.ms-excel.sheet.macroEnabled.main+xml"] = OfficeFormat.Xlsm,
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml"] = OfficeFormat.Xltx,
        ["application/vnd.ms-excel.template.macroEnabled.main+xml"] = OfficeFormat.Xltm,
        ["application/vnd.ms-excel.addin.macroEnabled.main+xml"] = OfficeFormat.Xlam,
        ["application/vnd.ms-excel.sheet.binary.macroEnabled.main"] = OfficeFormat.Xlsb,
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"] = OfficeFormat.Pptx,
        ["application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml"] = OfficeFormat.Pptm,
        ["application/vnd.openxmlformats-officedocument.presentationml.template.main+xml"] = OfficeFormat.Potx,
        ["application/vnd.ms-powerpoint.template.macroEnabled.main+xml"] = OfficeFormat.Potm,
        ["application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml"] = OfficeFormat.Ppsx,
        ["application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml"] = OfficeFormat.Ppsm,
        ["application/vnd.ms-powerpoint.addin.macroEnabled.main+xml"] = OfficeFormat.Ppam,
    };

    private static readonly Dictionary<string, OfficeFormat> s_odfMimeTypes = new(StringComparer.Ordinal)
    {
        ["application/vnd.oasis.opendocument.text"] = OfficeFormat.Odt,
        ["application/vnd.oasis.opendocument.spreadsheet"] = OfficeFormat.Ods,
        ["application/vnd.oasis.opendocument.presentation"] = OfficeFormat.Odp,
        ["application/vnd.oasis.opendocument.text-template"] = OfficeFormat.Ott,
        ["application/vnd.oasis.opendocument.spreadsheet-template"] = OfficeFormat.Ots,
        ["application/vnd.oasis.opendocument.presentation-template"] = OfficeFormat.Otp,
    };

    /// <summary>Detects the format of a source.</summary>
    /// <param name="source">The source; it must support synchronous reads (<see cref="IByteSource.CanReadSync"/>) and is not disposed.</param>
    /// <param name="limits">The limits for the container reads; null for <see cref="OfficeLimits.Default"/>.</param>
    /// <returns>The format, or <see cref="OfficeFormat.Unknown"/>.</returns>
    /// <exception cref="OfficeLimitException">The container asks for more than <paramref name="limits"/> allow.</exception>
    /// <exception cref="NotSupportedException">The source cannot be read synchronously; use <see cref="DetectAsync"/>.</exception>
    public static OfficeFormat Detect(IByteSource source, OfficeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= OfficeLimits.Default;
        byte[] prefix = new byte[(int)Math.Min(PrefixLength, source.Length)];
        int n = source.Read(0, prefix);
        return Classify(prefix.AsSpan(0, n), source, source.Length > n, limits, out _);
    }

    /// <summary>Detects the format of bytes in memory.</summary>
    /// <param name="bytes">The content.</param>
    /// <param name="limits">The limits for the container reads; null for <see cref="OfficeLimits.Default"/>.</param>
    /// <returns>The format, or <see cref="OfficeFormat.Unknown"/>.</returns>
    /// <exception cref="OfficeLimitException">The container asks for more than <paramref name="limits"/> allow.</exception>
    public static OfficeFormat Detect(ReadOnlyMemory<byte> bytes, OfficeLimits? limits = null)
    {
        using IByteSource source = ByteSource.FromMemory(bytes);
        return Detect(source, limits);
    }

    /// <summary>Detects the format of a file.</summary>
    /// <param name="path">The path.</param>
    /// <param name="limits">The limits for the container reads; null for <see cref="OfficeLimits.Default"/>.</param>
    /// <returns>The format, or <see cref="OfficeFormat.Unknown"/>.</returns>
    /// <exception cref="IOException">The file cannot be read (including <see cref="FileNotFoundException"/>).</exception>
    /// <exception cref="OfficeLimitException">The container asks for more than <paramref name="limits"/> allow.</exception>
    public static OfficeFormat Detect(string path, OfficeLimits? limits = null)
    {
        using IByteSource source = ByteSource.FromFile(path);
        return Detect(source, limits);
    }

    /// <summary>
    /// Detects the format of a source that may be remote. A text or binary-signature format is decided from the first
    /// <see cref="PrefixLength"/> bytes alone. A container (zip, compound file) or Flat OPC file is read into memory first, bounded by
    /// <see cref="OfficeLimits.MaxPartSize"/>, because the stream-based container readers need synchronous access.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="limits">The limits; null for <see cref="OfficeLimits.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The format, or <see cref="OfficeFormat.Unknown"/>.</returns>
    /// <exception cref="OfficeLimitException">The container asks for more than <paramref name="limits"/> allow.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async ValueTask<OfficeFormat> DetectAsync(IByteSource source, OfficeLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= OfficeLimits.Default;
        byte[] prefix = new byte[(int)Math.Min(PrefixLength, source.Length)];
        int n = await source.ReadAsync(0, prefix, cancellationToken).ConfigureAwait(false);
        OfficeFormat format = Classify(prefix.AsSpan(0, n), null, source.Length > n, limits, out bool needsSource);
        if (!needsSource)
        {
            return format;
        }

        IByteSource memory = await ByteSource.ToMemoryAsync(source, limits.MaxPartSize, cancellationToken).ConfigureAwait(false);
        return Classify(prefix.AsSpan(0, n), memory, source.Length > n, limits, out _);
    }

    private static OfficeFormat Classify(ReadOnlySpan<byte> prefix, IByteSource? source, bool truncated, OfficeLimits limits, out bool needsSource)
    {
        needsSource = false;
        if (prefix.Length == 0)
        {
            return OfficeFormat.Unknown;
        }

        // Containers.
        if (prefix.Length >= 4 && prefix[0] == 'P' && prefix[1] == 'K' && prefix[2] is 3 or 5 or 7 && prefix[3] is 4 or 6 or 8)
        {
            if (source is null)
            {
                needsSource = true;
                return OfficeFormat.Unknown;
            }

            return ClassifyZip(source, limits);
        }

        if (prefix.StartsWith(s_cfbSignature))
        {
            if (source is null)
            {
                needsSource = true;
                return OfficeFormat.Unknown;
            }

            return ClassifyCompoundFile(source, limits);
        }

        // Binary signatures.
        if (prefix.StartsWith("%PDF-"u8))
        {
            return OfficeFormat.Pdf;
        }

        if (prefix.StartsWith(s_pngSignature))
        {
            return OfficeFormat.Png;
        }

        if (prefix.Length >= 3 && prefix[0] == 0xFF && prefix[1] == 0xD8 && prefix[2] == 0xFF)
        {
            return OfficeFormat.Jpeg;
        }

        if (prefix.StartsWith("II*\0"u8) || prefix.StartsWith("MM\0*"u8))
        {
            return OfficeFormat.Tiff;
        }

        if (prefix.Length >= 12 && prefix.StartsWith("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return OfficeFormat.WebP;
        }

        OfficeFormat biff = ClassifyBiff(prefix);
        if (biff != OfficeFormat.Unknown)
        {
            return biff;
        }

        // Text formats.
        if (prefix.StartsWith("{\\rtf"u8))
        {
            return OfficeFormat.Rtf;
        }

        string? text = SniffXml.DecodeText(prefix);
        if (text is null)
        {
            return OfficeFormat.Unknown;
        }

        string trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (trimmed.StartsWith('<'))
        {
            // Text that starts with a tag is markup, not delimited text, even when no markup format claims it.
            return ClassifyXml(text, source, truncated);
        }

        return ClassifyDelimited(text, truncated);
    }

    // [MS-XLS] BOF record: record type 0x0009/0x0209/0x0409/0x0809 (BIFF2/3/4/5-8), then its length, as the first record of a bare stream.
    private static OfficeFormat ClassifyBiff(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 4)
        {
            return OfficeFormat.Unknown;
        }

        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(prefix);
        ushort length = BinaryPrimitives.ReadUInt16LittleEndian(prefix[2..]);
        return (type, length) switch
        {
            (0x0009, 4) or (0x0209, 6) or (0x0409, 6) or (0x0809, 8) => OfficeFormat.XlsLegacy,
            (0x0809, 16) => OfficeFormat.Xls,
            _ => OfficeFormat.Unknown,
        };
    }

    private static OfficeFormat ClassifyZip(IByteSource source, OfficeLimits limits)
    {
        try
        {
            using Stream stream = ByteSource.AsStream(source);
            using var zip = new ZipReader(stream, new ZipReaderOptions { Limits = limits.ToArchiveLimits(), LazyDirectory = true }, leaveOpen: true);
            ZipEntry? mimetype = null;
            ZipEntry? contentTypes = null;
            ZipEntry? rels = null;
            while (zip.GetNextEntry() is { } entry)
            {
                string name = entry.Name;
                if (name == "mimetype")
                {
                    mimetype = entry;
                }
                else if (name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                {
                    contentTypes = entry;
                }
                else if (name.Equals("_rels/.rels", StringComparison.OrdinalIgnoreCase))
                {
                    rels = entry;
                }
            }

            if (mimetype is not null)
            {
                string? type = ReadEntryText(zip, mimetype, 256)?.Trim();
                return type is not null && s_odfMimeTypes.TryGetValue(type, out OfficeFormat odf) ? odf : OfficeFormat.Unknown;
            }

            if (contentTypes is null)
            {
                return OfficeFormat.Unknown;
            }

            string? typesXml = ReadEntryText(zip, contentTypes, ContentTypesReadLimit);
            if (typesXml is null)
            {
                return OfficeFormat.Unknown;
            }

            string? mainPart = rels is null ? null : FindMainPartName(ReadEntryText(zip, rels, RelsReadLimit));
            return MainFormat(typesXml, mainPart);
        }
        catch (CompressionLimitException ex)
        {
            throw ArchiveErrors.Translate(ex);
        }
        catch (CompressionException)
        {
            // A damaged or unreadable container is not a format we can name; opening it reports the damage with its location.
            return OfficeFormat.Unknown;
        }
    }

    // ECMA-376 Part 2 (packaging): the package's root relationships name the main part with a relationship whose type ends
    // in "/officeDocument" (Transitional and Strict differ only in the namespace before it).
    private static string? FindMainPartName(string? relsXml)
    {
        if (relsXml is null)
        {
            return null;
        }

        foreach (SniffXml.Tag tag in SniffXml.StartTags(relsXml))
        {
            if (tag.LocalName == "Relationship"
                && tag.Get("Type") is { } type
                && type.EndsWith("/officeDocument", StringComparison.Ordinal)
                && tag.Get("TargetMode") is null or "Internal"
                && tag.Get("Target") is { } target)
            {
                return PartUri.TryResolve(null, target, out PartUri uri) ? uri.Value : null;
            }
        }

        return null;
    }

    // ECMA-376 Part 2 §10.1.2: the content type of a part is its Override, else the Default for its extension (a package may give its
    // main part no Override at all). When the relationships are missing, the first main-part content type listed wins.
    private static OfficeFormat MainFormat(string typesXml, string? mainPart)
    {
        OfficeFormat firstMain = OfficeFormat.Unknown;
        OfficeFormat viaDefault = OfficeFormat.Unknown;
        string mainExtension = mainPart is null ? string.Empty : ContentTypes.ExtensionOf(mainPart);
        foreach (SniffXml.Tag tag in SniffXml.StartTags(typesXml))
        {
            if (tag.Get("ContentType") is not { } contentType || !s_mainContentTypes.TryGetValue(contentType, out OfficeFormat format))
            {
                continue;
            }

            if (tag.LocalName == "Override")
            {
                if (mainPart is not null && tag.Get("PartName") is { } partName && partName.Equals(mainPart, StringComparison.OrdinalIgnoreCase))
                {
                    return format;
                }
            }
            else if (tag.LocalName == "Default")
            {
                if (mainExtension.Length > 0 && viaDefault == OfficeFormat.Unknown
                    && tag.Get("Extension") is { } ext && ext.Equals(mainExtension, StringComparison.OrdinalIgnoreCase))
                {
                    viaDefault = format;
                }
            }
            else
            {
                continue;
            }

            if (firstMain == OfficeFormat.Unknown)
            {
                firstMain = format;
            }
        }

        // A later Override for the main part beats a Default listed earlier, so the Default only counts after the whole list was read.
        return mainPart is null ? firstMain : viaDefault;
    }

    private static string? ReadEntryText(ZipReader zip, ZipEntry entry, int maxBytes)
    {
        using Stream data = zip.OpenEntryStream(entry);
        byte[] buffer = new byte[(int)Math.Min(maxBytes, Math.Max(entry.Size, 0) + 1)];
        int read = data.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return SniffXml.DecodeText(buffer.AsSpan(0, read));
    }

    // [MS-CFB] directory: the root's stream names classify legacy files. [MS-OFFCRYPTO]: an encrypted OOXML file is a compound file
    // with an EncryptionInfo and an EncryptedPackage stream. [MS-DOC] WordDocument, [MS-XLS] Workbook, [MS-PPT] PowerPoint Document.
    private static OfficeFormat ClassifyCompoundFile(IByteSource source, OfficeLimits limits)
    {
        // A compound file with a damaged allocation table is still readable in recovery mode, as the package reader does it; classify what
        // it holds so that the format's reader can report the damage as defects instead of the file being called unknown.
        OfficeFormat strict = ClassifyCompoundFile(source, limits, recovery: false);
        return strict != OfficeFormat.Unknown ? strict : ClassifyCompoundFile(source, limits, recovery: true);
    }

    private static OfficeFormat ClassifyCompoundFile(IByteSource source, OfficeLimits limits, bool recovery)
    {
        try
        {
            var options = new CompoundFileReaderOptions { Limits = limits.ToArchiveLimits(), Recovery = recovery };
            using CompoundFileReader cfb = source.TryGetMemory(out ReadOnlyMemory<byte> memory)
                ? new CompoundFileReader(memory, options)
                : new CompoundFileReader(ByteSource.AsStream(source), options);
            if (HasStream(cfb, "EncryptedPackage") && HasStream(cfb, "EncryptionInfo"))
            {
                return OfficeFormat.EncryptedPackage;
            }

            if (HasStream(cfb, "WordDocument"))
            {
                return OfficeFormat.Doc;
            }

            if (HasStream(cfb, "Workbook"))
            {
                return OfficeFormat.Xls;
            }

            if (HasStream(cfb, "PowerPoint Document"))
            {
                return OfficeFormat.Ppt;
            }

            return HasStream(cfb, "Book") ? OfficeFormat.XlsLegacy : OfficeFormat.Unknown;
        }
        catch (CompressionLimitException ex)
        {
            throw ArchiveErrors.Translate(ex);
        }
        catch (CompressionException) when (!recovery)
        {
            // Some writers leave small violations in the directory (a stream name without its terminating null, from Excel on the Mac);
            // the reader's recovery mode still finds the streams, and the format readers report the damage as defects.
            return ClassifyCompoundFile(source, limits, recovery: true);
        }
        catch (CompressionException)
        {
            return OfficeFormat.Unknown;
        }
    }

    private static bool HasStream(CompoundFileReader cfb, string name)
    {
        if (cfb.Find(name) is { ObjectType: CfbObjectType.Stream })
        {
            return true;
        }

        if (!cfb.IsRecovered)
        {
            return false;
        }

        // A recovered directory can hold names the lookup does not match (a missing null); the package layer walks the tree instead.
        foreach (CfbEntry e in cfb.GetChildren(cfb.Root))
        {
            if (e.ObjectType == CfbObjectType.Stream && string.Equals(e.RawName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static OfficeFormat ClassifyXml(string text, IByteSource? source, bool truncated)
    {
        string? mso = SniffXml.ProcessingInstruction(text, "mso-application");
        SniffXml.Tag? root = null;
        foreach (SniffXml.Tag tag in SniffXml.StartTags(text))
        {
            root = tag;
            break;
        }

        if (root is not { } r)
        {
            // A root tag longer than the prefix (Word 2003 declares dozens of namespaces) is not seen whole, but the processing instruction
            // before it already names the application.
            if (mso is not null && mso.Contains("Word.Document", StringComparison.Ordinal))
            {
                return OfficeFormat.Word2003Xml;
            }

            return mso is not null && mso.Contains("Excel.Sheet", StringComparison.Ordinal) ? OfficeFormat.Excel2003Xml : OfficeFormat.Unknown;
        }

        string local = r.LocalName;
        bool HasNamespace(string uri) => r.Attributes.Any(a => a.Value == uri);

        if (local == "package" && HasNamespace(FlatOpcNamespace))
        {
            return ClassifyFlatOpc(text, source, truncated);
        }

        if (mso is not null && mso.Contains("Word.Document", StringComparison.Ordinal) || HasNamespace("http://schemas.microsoft.com/office/word/2003/wordml"))
        {
            return OfficeFormat.Word2003Xml;
        }

        if (mso is not null && mso.Contains("Excel.Sheet", StringComparison.Ordinal) || HasNamespace("urn:schemas-microsoft-com:office:spreadsheet"))
        {
            return OfficeFormat.Excel2003Xml;
        }

        if (local == "document" && r.GetByLocalName("mimetype") is { } odfType)
        {
            return odfType switch
            {
                "application/vnd.oasis.opendocument.text" => OfficeFormat.Fodt,
                "application/vnd.oasis.opendocument.spreadsheet" => OfficeFormat.Fods,
                "application/vnd.oasis.opendocument.presentation" => OfficeFormat.Fodp,
                _ => OfficeFormat.Unknown,
            };
        }

        if (local == "svg")
        {
            return OfficeFormat.Svg;
        }

        return local.ToLowerInvariant() is "html" or "head" or "body" ? OfficeFormat.Html : OfficeFormat.Unknown;
    }

    // Flat OPC holds every part inside <pkg:part pkg:name=... pkg:contentType=...>. The main part is the one the package
    // relationships name; scanning the prefix for the officeDocument relationship and then that part's content type mirrors the zip rule.
    private static OfficeFormat ClassifyFlatOpc(string head, IByteSource? source, bool truncated)
    {
        string text = head;
        if (truncated && source is not null)
        {
            byte[] big = new byte[(int)Math.Min(FlatOpcScanLength, source.Length)];
            int n = source.Read(0, big);
            text = SniffXml.DecodeText(big.AsSpan(0, n)) ?? head;
        }

        string? mainPart = null;
        var types = new List<(string Name, string ContentType)>();
        foreach (SniffXml.Tag tag in SniffXml.StartTags(text))
        {
            if (tag.LocalName == "Relationship" && tag.Get("Type") is { } t && t.EndsWith("/officeDocument", StringComparison.Ordinal) && tag.Get("Target") is { } target)
            {
                mainPart ??= PartUri.TryResolve(null, target, out PartUri uri) ? uri.Value : null;
            }
            else if (tag.LocalName == "part" && tag.GetByLocalName("name") is { } name && tag.GetByLocalName("contentType") is { } ct)
            {
                types.Add((name, ct));
            }
        }

        OfficeFormat found = OfficeFormat.Unknown;
        foreach ((string name, string ct) in types)
        {
            if (!s_mainContentTypes.TryGetValue(ct, out OfficeFormat f))
            {
                continue;
            }

            if (mainPart is null || name.Equals(mainPart, StringComparison.OrdinalIgnoreCase))
            {
                found = f;
                break;
            }
        }

        return FlatKindOf(found);
    }

    private static OfficeFormat FlatKindOf(OfficeFormat main) => main switch
    {
        OfficeFormat.Docx or OfficeFormat.Docm or OfficeFormat.Dotx or OfficeFormat.Dotm => OfficeFormat.FlatOpcDocument,
        OfficeFormat.Xlsx or OfficeFormat.Xlsm or OfficeFormat.Xltx or OfficeFormat.Xltm or OfficeFormat.Xlam or OfficeFormat.Xlsb => OfficeFormat.FlatOpcWorkbook,
        OfficeFormat.Unknown => OfficeFormat.Unknown,
        _ => OfficeFormat.FlatOpcPresentation,
    };

    // The last resort: consistent delimiter counts over at least two complete lines of text.
    private static OfficeFormat ClassifyDelimited(string text, bool truncated)
    {
        foreach (char c in text)
        {
            if (char.IsControl(c) && c is not ('\t' or '\r' or '\n' or '\uFEFF'))
            {
                return OfficeFormat.Unknown;
            }
        }

        List<string> lines = SplitRecords(text.TrimStart('\uFEFF'), out bool openQuote);
        int count = lines.Count;
        if (truncated || openQuote || (count > 0 && lines[^1].Length == 0))
        {
            // The last line is cut by the prefix, or is the empty remainder after the final newline.
            count--;
        }

        var complete = new List<string>();
        for (int i = 0; i < count; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length > 0)
            {
                complete.Add(line);
            }
        }

        if (complete.Count < 2)
        {
            return OfficeFormat.Unknown;
        }

        foreach ((char delimiter, OfficeFormat format) in new[] { ('\t', OfficeFormat.Tsv), (',', OfficeFormat.Csv), (';', OfficeFormat.Csv) })
        {
            int first = CountOutsideQuotes(complete[0], delimiter);
            if (first == 0)
            {
                continue;
            }

            bool consistent = true;
            for (int i = 1; i < complete.Count && consistent; i++)
            {
                consistent = CountOutsideQuotes(complete[i], delimiter) == first;
            }

            if (consistent)
            {
                return format;
            }
        }

        return OfficeFormat.Unknown;
    }

    // Splits at line feeds that are not inside double quotes, so a quoted field with line breaks stays in one record. The last element
    // is the text after the final line feed (empty when the text ends with one); openQuote says a quote was still open at the end.
    private static List<string> SplitRecords(string text, out bool openQuote)
    {
        var records = new List<string>();
        bool quoted = false;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                quoted = !quoted;
            }
            else if (text[i] == '\n' && !quoted)
            {
                records.Add(text[start..i]);
                start = i + 1;
            }
        }

        records.Add(text[start..]);
        openQuote = quoted;
        return records;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        int n = 0;
        bool quoted = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == delimiter && !quoted)
            {
                n++;
            }
        }

        return n;
    }
}
