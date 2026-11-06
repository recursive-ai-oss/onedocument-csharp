using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.OfficeArt;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Cfb;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>One user edit of the file: a <c>UserEditAtom</c> ([MS-PPT] §2.3.3). PowerPoint appends a new edit on every incremental save.</summary>
/// <param name="Offset">The stream offset of the UserEditAtom record.</param>
/// <param name="OffsetLastEdit">The offset of the previous edit's UserEditAtom, or 0 for the first edit.</param>
/// <param name="OffsetPersistDirectory">The offset of this edit's PersistDirectoryAtom.</param>
/// <param name="DocumentPersistId">The persist identifier of the DocumentContainer (1 in every file written by PowerPoint).</param>
/// <param name="PersistIdSeed">The seed for new persist identifiers.</param>
/// <param name="LastSlideIdRef">The slide viewed last (meaningful in the newest edit only).</param>
/// <param name="EncryptSessionPersistId">The persist identifier of the CryptSession10Container, or 0 when the edit has none.</param>
public sealed record PptUserEdit(long Offset, long OffsetLastEdit, long OffsetPersistDirectory, uint DocumentPersistId, uint PersistIdSeed, uint LastSlideIdRef, uint EncryptSessionPersistId);

/// <summary>
/// A PowerPoint 97-2003 file at record level: the <c>Current User</c> and <c>PowerPoint Document</c> streams, the user-edit chain, the persist
/// object directory and the <c>Pictures</c> stream. Persist objects are parsed on demand and cached; every record keeps its bytes.
/// </summary>
/// <remarks>
/// [MS-PPT] §2.1.2: the document stream is a sequence of persist objects and, after each save, a PersistDirectoryAtom and a UserEditAtom.
/// The newest UserEditAtom is found through the Current User stream; each points back at the previous one. A persist object that several
/// edits define is read from the newest edit that defines it (the last edit wins), and the older copies stay in the stream untouched.
/// </remarks>
public sealed class PptFile
{
    internal const string DocumentStreamName = "PowerPoint Document";
    internal const string CurrentUserStreamName = "Current User";
    private const uint EncryptedHeaderToken = 0xF3D1C4DF;

    private readonly Dictionary<uint, ArtRecord?> _objects = [];
    private readonly Dictionary<uint, long> _offsets = [];
    private readonly ArtReadContext _read;

    private PptFile(ReadOnlyMemory<byte> document, ReadOnlyMemory<byte> pictures, OpenContext context)
    {
        DocumentStream = document;
        PicturesStream = pictures;
        Context = context;
        _read = new ArtReadContext(context.Limits, context.Defects, DocumentStreamName);
    }

    /// <summary>The open context (limits and defect sink).</summary>
    public OpenContext Context { get; }

    /// <summary>The bytes of the <c>PowerPoint Document</c> stream.</summary>
    public ReadOnlyMemory<byte> DocumentStream { get; }

    /// <summary>The bytes of the <c>Pictures</c> stream, or empty when the file has none.</summary>
    public ReadOnlyMemory<byte> PicturesStream { get; }

    /// <summary>The user edits, newest first.</summary>
    public IReadOnlyList<PptUserEdit> UserEdits { get; private set; } = [];

    /// <summary>The names of all streams of the compound file (full paths), including those this reader does not interpret (VBA storage, summary information, embedded OLE).</summary>
    public IReadOnlyList<string> StreamNames { get; private set; } = [];

    /// <summary>True when the compound file holds a VBA project storage (<c>_VBA_PROJECT_CUR</c>); it is carried as is and never run.</summary>
    public bool HasVbaProject { get; private set; }

    /// <summary>The defects-aware reading context for records of the document stream.</summary>
    public ArtReadContext ReadContext => _read;

    /// <summary>The persist object directory after all edits: persist identifier to stream offset (newest definition wins).</summary>
    public IReadOnlyDictionary<uint, long> PersistOffsets => _offsets;

    /// <summary>The document container, or null when it cannot be read (a fatal defect is raised first, so null is returned only for damaged-but-tolerated files).</summary>
    public ArtRecord? DocumentContainer { get; private set; }

    /// <summary>
    /// Opens a PowerPoint binary file.
    /// </summary>
    /// <param name="source">The bytes of the compound file. The reader disposes it.</param>
    /// <param name="context">Limits and defect sink.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The file.</returns>
    /// <exception cref="OfficeEncryptedException">The file is encrypted (header token 0xF3D1C4DF or a CryptSession10Container); decryption is not supported.</exception>
    /// <exception cref="OfficeFormatException">The compound file is damaged beyond reading, has no <c>PowerPoint Document</c> stream, or the document container cannot be found.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static PptFile Open(IByteSource source, OpenContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        using CfbPackage cfb = CfbPackage.Open(source, context);
        cancellationToken.ThrowIfCancellationRequested();
        if (cfb.FindPart("/" + DocumentStreamName) is not { } documentPart)
        {
            throw new OfficeFormatException(DefectKind.Structure, "ppt.no-document-stream", "The compound file has no 'PowerPoint Document' stream.", DefectLocation.InRecord(DocumentStreamName, 0));
        }

        ReadOnlyMemory<byte> document = documentPart.GetBytes();
        ReadOnlyMemory<byte> currentUser = cfb.FindPart("/" + CurrentUserStreamName)?.GetBytes() ?? default;
        ReadOnlyMemory<byte> pictures = cfb.FindPart("/Pictures")?.GetBytes() ?? default;
        var file = new PptFile(document, pictures, context);
        var names = new List<string>(cfb.Streams.Count);
        foreach (CfbStreamPart stream in cfb.Streams)
        {
            names.Add(stream.Name);
            if (stream.Name.StartsWith("/_VBA_PROJECT_CUR/", StringComparison.OrdinalIgnoreCase))
            {
                file.HasVbaProject = true;
            }
        }

        file.StreamNames = names;
        file.ReadStructure(currentUser, cancellationToken);
        return file;
    }

    private void ReadStructure(ReadOnlyMemory<byte> currentUser, CancellationToken cancellationToken)
    {
        long editOffset = -1;
        if (currentUser.Length >= ArtRecordHeader.Size + 12 && ArtRecordHeader.TryRead(currentUser.Span, out ArtRecordHeader cuHeader) && cuHeader.Type == PptRecordType.CurrentUserAtom)
        {
            // [MS-PPT] §2.3.2: size (0x14), headerToken, offsetToCurrentEdit follow the record header.
            uint token = BinaryPrimitives.ReadUInt32LittleEndian(currentUser.Span[12..]);
            if (token == EncryptedHeaderToken)
            {
                throw new OfficeEncryptedException(EncryptionProblem.PasswordRequired, DefectLocation.InRecord(CurrentUserStreamName, 12));
            }

            editOffset = BinaryPrimitives.ReadUInt32LittleEndian(currentUser.Span[16..]);
        }
        else
        {
            Context.Defects.Report(new Defect(DefectKind.Record, "ppt.current-user-missing", DefectSeverity.Repaired, "The 'Current User' stream is missing or damaged; the newest edit is found by scanning the document stream.", DefectLocation.InRecord(CurrentUserStreamName, 0)));
        }

        List<PptUserEdit> edits = editOffset >= 0 ? WalkChain(editOffset, cancellationToken) : [];
        if (edits.Count == 0)
        {
            edits = RecoverChain(cancellationToken);
        }

        if (edits.Count == 0)
        {
            throw new OfficeFormatException(DefectKind.Structure, "ppt.no-user-edit", "The document stream holds no readable user edit, so the persist object directory cannot be built.", DefectLocation.InRecord(DocumentStreamName, 0));
        }

        UserEdits = edits;
        if (edits.Exists(e => e.EncryptSessionPersistId != 0))
        {
            throw new OfficeEncryptedException(EncryptionProblem.PasswordRequired, DefectLocation.InRecord(DocumentStreamName, edits[0].Offset));
        }

        // Newest first: the first definition of a persist identifier wins.
        foreach (PptUserEdit edit in edits)
        {
            ReadPersistDirectory(edit);
        }

        DocumentContainer = GetPersistObject(edits[0].DocumentPersistId);
        if (DocumentContainer is null || DocumentContainer.Type != PptRecordType.Document)
        {
            // Some writers leave the document persist id out of the directory; the container is the first top-level record of that type.
            ArtRecord? found = FindTopLevel(PptRecordType.Document);
            if (found is null)
            {
                throw new OfficeFormatException(DefectKind.Structure, "ppt.no-document-container", "The persist object directory does not lead to a document container.", DefectLocation.InRecord(DocumentStreamName, 0));
            }

            Context.Defects.Report(new Defect(DefectKind.Record, "ppt.document-container-recovered", DefectSeverity.Repaired, "The document container was found by scanning, not through the persist object directory.", DefectLocation.InRecord(DocumentStreamName, found.Offset)));
            DocumentContainer = found;
        }
    }

    // [MS-PPT] §2.1.2: UserEditAtom.offsetLastEdit must be smaller than the atom's own offset; a chain that does not strictly decrease ends.
    private List<PptUserEdit> WalkChain(long firstOffset, CancellationToken cancellationToken)
    {
        var edits = new List<PptUserEdit>();
        long offset = firstOffset;
        long previous = long.MaxValue;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadUserEdit(offset, out PptUserEdit? edit))
            {
                Context.Defects.Report(new Defect(DefectKind.Record, "ppt.edit-chain-broken", DefectSeverity.Repaired, "A user edit offset does not lead to a UserEditAtom; older edits are ignored.", DefectLocation.InRecord(DocumentStreamName, offset)));
                break;
            }

            if (offset >= previous)
            {
                Context.Defects.Report(new Defect(DefectKind.Record, "ppt.edit-chain-loop", DefectSeverity.Repaired, "The user edit chain does not move backwards; the walk stops here.", DefectLocation.InRecord(DocumentStreamName, offset)));
                break;
            }

            edits.Add(edit!);
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), Context.Limits.MaxRecordCount, edits.Count, DefectLocation.InRecord(DocumentStreamName, offset));
            previous = offset;
            if (edit!.OffsetLastEdit == 0)
            {
                break;
            }

            offset = edit.OffsetLastEdit;
        }

        return edits;
    }

    // With no usable Current User stream, the UserEditAtoms are the top-level records of that type; the last one is the newest edit.
    private List<PptUserEdit> RecoverChain(CancellationToken cancellationToken)
    {
        long newest = -1;
        long position = 0;
        ReadOnlySpan<byte> data = DocumentStream.Span;
        while (position + ArtRecordHeader.Size <= data.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArtRecordHeader.TryRead(data[(int)position..], out ArtRecordHeader h);
            if (h.Type == PptRecordType.UserEditAtom && TryReadUserEdit(position, out _))
            {
                newest = position;
            }

            long next = position + ArtRecordHeader.Size + h.Length;
            if (next <= position || next > data.Length)
            {
                break;
            }

            position = next;
        }

        return newest < 0 ? [] : WalkChain(newest, cancellationToken);
    }

    private bool TryReadUserEdit(long offset, out PptUserEdit? edit)
    {
        edit = null;
        ReadOnlySpan<byte> data = DocumentStream.Span;
        if (offset < 0 || offset + ArtRecordHeader.Size > data.Length || !ArtRecordHeader.TryRead(data[(int)offset..], out ArtRecordHeader h) || h.Type != PptRecordType.UserEditAtom)
        {
            return false;
        }

        // [MS-PPT] §2.3.3: recLen is 0x1C, or 0x20 when encryptSessionPersistIdRef is present.
        if (h.Length < 0x1C || offset + ArtRecordHeader.Size + h.Length > data.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> p = data[(int)(offset + ArtRecordHeader.Size)..];
        uint encrypt = h.Length >= 0x20 ? BinaryPrimitives.ReadUInt32LittleEndian(p[28..]) : 0;
        edit = new PptUserEdit(
            offset,
            BinaryPrimitives.ReadUInt32LittleEndian(p[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(p[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(p[16..]),
            BinaryPrimitives.ReadUInt32LittleEndian(p[20..]),
            BinaryPrimitives.ReadUInt32LittleEndian(p[0..]),
            encrypt);
        return true;
    }

    // [MS-PPT] §2.3.4 and §2.3.5: entries of (persistId:20, cPersist:12) followed by cPersist offsets.
    private void ReadPersistDirectory(PptUserEdit edit)
    {
        ReadOnlySpan<byte> data = DocumentStream.Span;
        long at = edit.OffsetPersistDirectory;
        if (at < 0 || at + ArtRecordHeader.Size > data.Length || !ArtRecordHeader.TryRead(data[(int)at..], out ArtRecordHeader h) || h.Type != PptRecordType.PersistDirectoryAtom)
        {
            Context.Defects.Report(new Defect(DefectKind.Record, "ppt.persist-directory-missing", DefectSeverity.Dropped, "A user edit points at a persist directory that is not there; the objects it defines are ignored.", DefectLocation.InRecord(DocumentStreamName, edit.Offset)));
            return;
        }

        long end = Math.Min(data.Length, at + ArtRecordHeader.Size + h.Length);
        long position = at + ArtRecordHeader.Size;
        while (position + 4 <= end)
        {
            uint head = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)position..]);
            uint first = head & 0xFFFFF;
            int count = (int)(head >> 20);
            position += 4;
            for (int i = 0; i < count; i++)
            {
                if (position + 4 > end)
                {
                    Context.Defects.Report(new Defect(DefectKind.Record, "ppt.persist-directory-truncated", DefectSeverity.Repaired, "A persist directory entry lists more offsets than the record holds.", DefectLocation.InRecord(DocumentStreamName, at)));
                    return;
                }

                uint id = first + (uint)i;
                long target = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)position..]);
                position += 4;
                _offsets.TryAdd(id, target);
            }
        }
    }

    private ArtRecord? FindTopLevel(int type)
    {
        long position = 0;
        ReadOnlySpan<byte> data = DocumentStream.Span;
        while (position + ArtRecordHeader.Size <= data.Length)
        {
            ArtRecordHeader.TryRead(data[(int)position..], out ArtRecordHeader h);
            if (h.Type == type)
            {
                return ReadRecordAt(position);
            }

            long next = position + ArtRecordHeader.Size + h.Length;
            if (next <= position || next > data.Length)
            {
                break;
            }

            position = next;
        }

        return null;
    }

    /// <summary>
    /// Reads the persist object with the given identifier. The record at its offset is parsed with its children; the result is cached. A missing
    /// identifier or an offset outside the stream yields null and a defect (<c>ppt.persist-object-missing</c>).
    /// </summary>
    /// <param name="persistId">A persist identifier (a <c>PersistIdRef</c> from the document).</param>
    /// <returns>The record, or null.</returns>
    public ArtRecord? GetPersistObject(uint persistId)
    {
        if (_objects.TryGetValue(persistId, out ArtRecord? cached))
        {
            return cached;
        }

        ArtRecord? record = null;
        if (_offsets.TryGetValue(persistId, out long offset))
        {
            record = ReadRecordAt(offset);
            if (record is null)
            {
                Context.Defects.Report(new Defect(DefectKind.Record, "ppt.persist-object-missing", DefectSeverity.Dropped, "Persist object " + persistId + " points outside the document stream.", DefectLocation.InRecord(DocumentStreamName, offset)));
            }
        }
        else
        {
            Context.Defects.Report(new Defect(DefectKind.Reference, "ppt.persist-object-missing", DefectSeverity.Dropped, "Persist object " + persistId + " is not in the persist object directory.", DefectLocation.InRecord(DocumentStreamName, -1)));
        }

        _objects[persistId] = record;
        return record;
    }

    /// <summary>Parses the single record (with its children) that starts at <paramref name="offset"/> of the document stream, or returns null when no header fits there.</summary>
    /// <param name="offset">The stream offset.</param>
    public ArtRecord? ReadRecordAt(long offset)
    {
        if (offset < 0 || offset + ArtRecordHeader.Size > DocumentStream.Length)
        {
            return null;
        }

        ReadOnlyMemory<byte> rest = DocumentStream[(int)offset..];
        ArtRecordHeader.TryRead(rest.Span, out ArtRecordHeader h);
        long total = Math.Min(rest.Length, ArtRecordHeader.Size + h.Length);
        IReadOnlyList<ArtRecord> parsed = ArtRecords.Parse(rest[..(int)total], offset, _read);
        return parsed.Count > 0 ? parsed[0] : null;
    }
}
