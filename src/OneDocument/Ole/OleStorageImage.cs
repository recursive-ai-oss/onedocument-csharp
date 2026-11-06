using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Ole;

/// <summary>One stream of an <see cref="OleStorageImage"/>.</summary>
/// <param name="Path">The stream's path inside the storage, in the form <see cref="CfbPackage"/> names streams (<c>\u0001Ole</c>, <c>Sub/Stream</c>), with no leading slash.</param>
/// <param name="Data">The stream's bytes.</param>
public sealed record OleStreamImage(string Path, byte[] Data);

/// <summary>One sub-storage of an <see cref="OleStorageImage"/>.</summary>
/// <param name="Path">The sub-storage's path inside the storage, with no leading slash.</param>
/// <param name="ClassId">Its CLSID, or <see cref="Guid.Empty"/>.</param>
public sealed record OleSubStorageImage(string Path, Guid ClassId);

/// <summary>
/// A copy of one storage of a compound file, with everything below it: the OLE object stored in a document ([MS-OLEDS] §1.3.3). It holds
/// bytes and no file handle, so it outlives the compound file it came from, and it can be written out as a compound file of its own, which
/// is what an OOXML package stores for an embedded object (an <c>oleObject</c> part).
/// </summary>
/// <remarks>Immutable after capture. Nothing in it is ever executed or activated.</remarks>
public sealed class OleStorageImage
{
    private readonly Dictionary<string, OleStreamImage> _byPath = new(StringComparer.OrdinalIgnoreCase);

    internal OleStorageImage(string name, Guid classId, List<OleStreamImage> streams, List<OleSubStorageImage> storages)
    {
        Name = name;
        ClassId = classId;
        Streams = streams;
        Storages = storages;
        foreach (OleStreamImage s in streams)
        {
            _byPath[s.Path] = s;
        }
    }

    /// <summary>Gets the storage's name where it was found (<c>_1234</c> in a Word <c>ObjectPool</c>, <c>MBD00000001</c> in a workbook).</summary>
    public string Name { get; }

    /// <summary>Gets the storage's CLSID ([MS-CFB] §2.6.1): for an embedded object, the class of the application that made it; for a linked object, the standard link class.</summary>
    public Guid ClassId { get; }

    /// <summary>Gets the streams below the storage, in directory order.</summary>
    public IReadOnlyList<OleStreamImage> Streams { get; }

    /// <summary>Gets the sub-storages below the storage, in directory order.</summary>
    public IReadOnlyList<OleSubStorageImage> Storages { get; }

    /// <summary>Gets the total size of all streams, in bytes.</summary>
    public long TotalSize
    {
        get
        {
            long n = 0;
            foreach (OleStreamImage s in Streams)
            {
                n += s.Data.Length;
            }

            return n;
        }
    }

    /// <summary>Finds a stream by its path inside the storage, ignoring case ([MS-CFB] §2.6.4).</summary>
    /// <param name="path">The path, such as <c>\u0001CompObj</c>.</param>
    /// <returns>The bytes, or null.</returns>
    public byte[]? Find(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _byPath.TryGetValue(path, out OleStreamImage? s) ? s.Data : null;
    }

    /// <summary>Writes the storage as a compound file whose root is the storage: its CLSID is the root's, its streams and sub-storages are the root's children.</summary>
    /// <param name="limits">The limits; null for the defaults.</param>
    /// <returns>The bytes of the compound file.</returns>
    /// <exception cref="OfficeException">A limit was exceeded or a name is not valid for a compound file.</exception>
    public byte[] ToCompoundFile(OfficeLimits? limits = null)
    {
        using CfbPackage package = CfbPackage.Create(limits);
        package.RootClassId = ClassId;
        foreach (OleSubStorageImage sub in Storages)
        {
            package.AddStorage("/" + sub.Path, sub.ClassId);
        }

        foreach (OleStreamImage s in Streams)
        {
            package.AddPart("/" + s.Path, string.Empty, s.Data);
        }

        using var ms = new MemoryStream();
        package.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>Reads a compound file as one storage, the root being the storage (the reverse of <see cref="ToCompoundFile"/>). The payload of a PowerPoint <c>ExOleObjStg</c> record is such a file.</summary>
    /// <param name="compoundFile">The bytes of a compound file.</param>
    /// <param name="name">The name to give the storage.</param>
    /// <param name="context">The limits and the defect sink; null for the defaults.</param>
    /// <returns>The storage, or null when the bytes are not a compound file (reported as a defect).</returns>
    public static OleStorageImage? FromCompoundFile(ReadOnlyMemory<byte> compoundFile, string name, OpenContext? context = null)
    {
        OpenContext ctx = context ?? new OpenContext();
        if (!CfbPackage.TryOpen(ByteSource.FromMemory(compoundFile.ToArray()), out CfbPackage? cfb, ctx) || cfb is null)
        {
            return null;
        }

        using (cfb)
        {
            OleStorageSet set = OleStorageSet.CaptureRoot(cfb, name, ctx);
            return set.Count == 0 ? null : set[0];
        }
    }

    /// <summary>
    /// Describes the object the storage holds from its <c>\u0001Ole</c> and <c>\u0001CompObj</c> streams ([MS-OLEDS] §2.3.3, §2.3.8): whether it is
    /// embedded or linked, its class and display name, and the file a linked object points at. A damaged or missing stream gives less detail,
    /// never an exception.
    /// </summary>
    /// <returns>The description.</returns>
    public OleObjectInfo Describe() => OleObjectInfo.Describe(this);
}

/// <summary>The storages found below one storage of a compound file, each copied as an <see cref="OleStorageImage"/>.</summary>
public sealed class OleStorageSet : IReadOnlyList<OleStorageImage>
{
    private readonly List<OleStorageImage> _items = [];
    private readonly Dictionary<string, OleStorageImage> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets an empty set.</summary>
    public static OleStorageSet Empty { get; } = new();

    /// <summary>Gets the number of storages.</summary>
    public int Count => _items.Count;

    /// <summary>Gets a storage by position.</summary>
    /// <param name="index">The position.</param>
    public OleStorageImage this[int index] => _items[index];

    /// <summary>Finds a storage by its name, ignoring case.</summary>
    /// <param name="name">The name, such as <c>_1234</c>.</param>
    /// <returns>The storage, or null.</returns>
    public OleStorageImage? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }

    /// <inheritdoc />
    public IEnumerator<OleStorageImage> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Copies every storage directly below <paramref name="parentPath"/> (for example <c>/ObjectPool</c>) with its contents. Storages that do
    /// not fit the limits are left out and reported (<c>ole.storage-too-large</c>).
    /// </summary>
    /// <param name="cfb">The compound file.</param>
    /// <param name="parentPath">The path of the parent storage, <c>/</c> for the root.</param>
    /// <param name="context">The limits and the defect sink.</param>
    /// <returns>The storages, in directory order.</returns>
    public static OleStorageSet Capture(CfbPackage cfb, string parentPath, OpenContext context) =>
        CaptureCore(cfb, parentPath, null, context);

    /// <summary>Copies the storages of a compound file whose names start with <paramref name="prefix"/> (<c>MBD</c> in a workbook), directly below the root.</summary>
    /// <param name="cfb">The compound file.</param>
    /// <param name="prefix">The name prefix, compared ignoring case.</param>
    /// <param name="context">The limits and the defect sink.</param>
    /// <returns>The storages, in directory order.</returns>
    public static OleStorageSet CaptureByPrefix(CfbPackage cfb, string prefix, OpenContext context) =>
        CaptureCore(cfb, "/", prefix, context);

    internal static OleStorageSet CaptureRoot(CfbPackage cfb, string name, OpenContext context)
    {
        var set = new OleStorageSet();
        var streams = new List<OleStreamImage>();
        var subs = new List<OleSubStorageImage>();
        long total = 0;
        foreach (CfbStorage st in cfb.Storages)
        {
            subs.Add(new OleSubStorageImage(st.Path[1..], st.ClassId));
        }

        foreach (CfbStreamPart p in cfb.Streams)
        {
            if (!Take(p, context, ref total, name))
            {
                return set;
            }

            streams.Add(new OleStreamImage(p.Name[1..], p.GetBytes().ToArray()));
        }

        set.Add(new OleStorageImage(name, cfb.RootClassId, streams, subs));
        return set;
    }

    private static OleStorageSet CaptureCore(CfbPackage cfb, string parentPath, string? prefix, OpenContext context)
    {
        ArgumentNullException.ThrowIfNull(cfb);
        ArgumentNullException.ThrowIfNull(parentPath);
        var set = new OleStorageSet();
        string parent = parentPath == "/" ? string.Empty : parentPath;
        long total = 0;
        foreach (CfbStorage st in cfb.Storages)
        {
            if (!st.Path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string childName = st.Path[(parent.Length + 1)..];
            if (childName.Contains('/', StringComparison.Ordinal) || (prefix is not null && !childName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string root = st.Path + "/";
            var streams = new List<OleStreamImage>();
            var subs = new List<OleSubStorageImage>();
            foreach (CfbStorage sub in cfb.Storages)
            {
                if (sub.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    subs.Add(new OleSubStorageImage(sub.Path[root.Length..], sub.ClassId));
                }
            }

            bool fits = true;
            foreach (CfbStreamPart p in cfb.Streams)
            {
                if (!p.Name.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!Take(p, context, ref total, st.Path))
                {
                    fits = false;
                    break;
                }

                streams.Add(new OleStreamImage(p.Name[root.Length..], p.GetBytes().ToArray()));
            }

            if (fits)
            {
                set.Add(new OleStorageImage(childName, st.ClassId, streams, subs));
            }
        }

        return set;
    }

    private static bool Take(CfbStreamPart p, OpenContext context, ref long total, string where)
    {
        total += p.Length;
        if (p.Length > context.Limits.MaxPartSize || total > context.Limits.MaxAllocationBytes)
        {
            context.Defects.Report(new Defect(DefectKind.Limit, "ole.storage-too-large", DefectSeverity.Dropped,
                "The storage holds more than the limits allow to copy (" + total.ToString(CultureInfo.InvariantCulture) + " bytes); it is left out.", DefectLocation.InRecord(where)));
            return false;
        }

        return true;
    }

    private void Add(OleStorageImage image)
    {
        if (_byName.TryAdd(image.Name, image))
        {
            _items.Add(image);
        }
    }
}

/// <summary>The kind of an OLE object ([MS-OLEDS] §1.3.3, §2.3.3).</summary>
public enum OleObjectKind
{
    /// <summary>The storage is an object whose data lives in the document (the <c>\u0001Ole</c> flags say embedded, or the stream is absent).</summary>
    Embedded = 0,

    /// <summary>The object is a link to a file: its <c>\u0001Ole</c> stream has the link flag and a moniker.</summary>
    Linked = 1,

    /// <summary>An embedded file or other packaged object (Object Packager: the <c>\u0001Ole10Native</c> stream holds the file).</summary>
    Package = 2,
}

/// <summary>What an <see cref="OleStorageImage"/> says about the object it holds.</summary>
/// <param name="Kind">Embedded, linked or a package.</param>
/// <param name="ClassId">The storage's CLSID.</param>
/// <param name="UserType">The object's display name, such as <c>Microsoft Excel Worksheet</c>, from the <c>\u0001CompObj</c> stream; null when it has none.</param>
/// <param name="ProgId">The object's programmatic identifier, such as <c>Excel.Sheet.8</c>: the one the <c>\u0001CompObj</c> stream holds, else the one a well-known class gives; null when unknown.</param>
/// <param name="LinkTarget">For a linked object, the path or URL the moniker names; null when it names none or the moniker is not a file moniker.</param>
public sealed record OleObjectInfo(OleObjectKind Kind, Guid ClassId, string? UserType, string? ProgId, string? LinkTarget)
{
    /// <summary>The CLSID of the standard link object, <c>CLSID_StdOleLink</c> ([MS-OLEDS] §2.3.3).</summary>
    public static readonly Guid StandardLinkClassId = new("00000300-0000-0000-C000-000000000046");

    /// <summary>The CLSID of the Object Packager's package object.</summary>
    public static readonly Guid PackageClassId = new("0003000C-0000-0000-C000-000000000046");

    // Classes whose programmatic identifier is stable and widely documented; the CompObj stream is preferred when it has one.
    private static readonly (Guid Id, string ProgId)[] Known =
    [
        (new Guid("00020906-0000-0000-C000-000000000046"), "Word.Document.8"),
        (new Guid("00020900-0000-0000-C000-000000000046"), "Word.Document.6"),
        (new Guid("00020820-0000-0000-C000-000000000046"), "Excel.Sheet.8"),
        (new Guid("00020810-0000-0000-C000-000000000046"), "Excel.Sheet.5"),
        (new Guid("00020821-0000-0000-C000-000000000046"), "Excel.Chart.8"),
        (new Guid("64818D10-4F9B-11CF-86EA-00AA00B929E8"), "PowerPoint.Show.8"),
        (new Guid("64818D11-4F9B-11CF-86EA-00AA00B929E8"), "PowerPoint.Slide.8"),
        (new Guid("0002CE02-0000-0000-C000-000000000046"), "Equation.3"),
        (new Guid("0003000C-0000-0000-C000-000000000046"), "Package"),
        (new Guid("0003000A-0000-0000-C000-000000000046"), "PBrush"),
    ];

    /// <summary>Gets a value indicating whether the object is a package: an embedded file such as a PDF or an archive.</summary>
    public bool IsPackage => Kind == OleObjectKind.Package;

    internal static OleObjectInfo Describe(OleStorageImage image)
    {
        string? userType = null;
        string? progId = null;
        if (image.Find("\u0001CompObj") is { } comp)
        {
            (userType, progId) = ReadCompObj(comp);
        }

        OleObjectKind kind = OleObjectKind.Embedded;
        string? link = null;
        if (image.Find("\u0001Ole") is { } ole && ReadOleStream(ole, out string? target))
        {
            kind = OleObjectKind.Linked;
            link = target;
        }
        else if (image.ClassId == StandardLinkClassId)
        {
            kind = OleObjectKind.Linked;
        }
        else if (image.Find("\u0001Ole10Native") is not null && (image.ClassId == PackageClassId || progId is "Package"))
        {
            kind = OleObjectKind.Package;
        }

        if (progId is null)
        {
            foreach ((Guid id, string known) in Known)
            {
                if (id == image.ClassId)
                {
                    progId = known;
                    break;
                }
            }
        }

        if (kind == OleObjectKind.Embedded && image.ClassId == PackageClassId)
        {
            kind = OleObjectKind.Package;
        }

        return new OleObjectInfo(kind, image.ClassId, userType, progId, link);
    }

    // [MS-OLEDS] §2.3.8: a 28-byte header, an ANSI user type, an ANSI clipboard format, and a string the implementations fill with the
    // programmatic identifier; then, after the marker 0x71B239F4, the same three as UTF-16.
    private static (string? UserType, string? ProgId) ReadCompObj(byte[] d)
    {
        try
        {
            int pos = 28;
            string? userType = ReadAnsi(d, ref pos);
            // ClipboardFormatOrAnsiString (§2.3.1).
            if (pos + 4 > d.Length)
            {
                return (userType, null);
            }

            uint marker = BitConverter.ToUInt32(d, pos);
            pos += 4;
            if (marker is 0xFFFFFFFF or 0xFFFFFFFE)
            {
                pos += 4;
            }
            else if (marker != 0)
            {
                if (marker > 0x190 || pos + marker > d.Length)
                {
                    return (userType, null);
                }

                pos += (int)marker;
            }

            string? progId = ReadAnsi(d, ref pos);
            if (progId is not null && (progId.Length == 0 || progId.Length > 128 || progId.Any(ch => ch < 0x20)))
            {
                progId = null;
            }

            if (pos + 8 <= d.Length && BitConverter.ToUInt32(d, pos) == 0x71B239F4)
            {
                pos += 4;
                if (ReadWide(d, ref pos) is { Length: > 0 } wide)
                {
                    userType = wide;
                }
            }

            return (userType, progId);
        }
        catch (ArgumentException)
        {
            return (null, null);
        }
    }

    private static string? ReadAnsi(byte[] d, ref int pos)
    {
        if (pos + 4 > d.Length)
        {
            return null;
        }

        uint len = BitConverter.ToUInt32(d, pos);
        pos += 4;
        if (len == 0)
        {
            return string.Empty;
        }

        if (len > 4096 || pos + len > d.Length)
        {
            pos = d.Length;
            return null;
        }

        string s = Encoding.Latin1.GetString(d, pos, (int)len).TrimEnd('\0');
        pos += (int)len;
        return s;
    }

    private static string? ReadWide(byte[] d, ref int pos)
    {
        if (pos + 4 > d.Length)
        {
            return null;
        }

        // LengthPrefixedUnicodeString (§2.1.5): the length is in bytes, with the terminator.
        uint len = BitConverter.ToUInt32(d, pos);
        pos += 4;
        if (len == 0)
        {
            return string.Empty;
        }

        if (len > 8192 || pos + len > d.Length)
        {
            return null;
        }

        string s = Encoding.Unicode.GetString(d, pos, (int)(len & ~1u)).TrimEnd('\0');
        pos += (int)len;
        return s;
    }

    // [MS-OLEDS] §2.3.3: Version, Flags (bit 0: linked), LinkUpdateOption, Reserved1, the reserved moniker stream, then for a linked object
    // the relative and absolute source monikers.
    private static bool ReadOleStream(byte[] d, out string? target)
    {
        target = null;
        if (d.Length < 20 || BitConverter.ToUInt32(d, 0) != 0x02000001 || (BitConverter.ToUInt32(d, 4) & 1) == 0)
        {
            return false;
        }

        int pos = 16;
        uint reserved = BitConverter.ToUInt32(d, pos);
        pos += 4;
        if (reserved != 0)
        {
            if (reserved < 4 || reserved - 4 > d.Length - pos)
            {
                return true;
            }

            pos += (int)reserved - 4;
        }

        string? relative = ReadMoniker(d, ref pos);
        string? absolute = ReadMoniker(d, ref pos);
        // [MS-OLEDS] §2.3.3: the relative moniker, when present, is used instead of the absolute one.
        target = relative ?? absolute;

        return true;
    }

    // A MONIKERSTREAM is a size that counts itself, a CLSID and the moniker's data; a file moniker's data is cAnti, an ANSI path length and the path ([MS-OSHARED] §2.3.7.8).
    private static string? ReadMoniker(byte[] d, ref int pos)
    {
        if (pos + 4 > d.Length)
        {
            return null;
        }

        uint size = BitConverter.ToUInt32(d, pos);
        if (size == 0)
        {
            pos += 4;
            return null;
        }

        if (size < 4 + 16 || size - 4 > d.Length - pos - 4)
        {
            pos = d.Length;
            return null;
        }

        int start = pos + 4;
        int end = pos + (int)size;
        pos = end;
        if (new Guid(d.AsSpan(start, 16)) != new Guid("00000303-0000-0000-C000-000000000046") || end - start < 16 + 6)
        {
            return null;
        }

        int p = start + 16 + 2;
        uint chars = BitConverter.ToUInt32(d, p);
        p += 4;
        if (chars == 0 || chars > 32767 || p + chars > end)
        {
            return null;
        }

        return Encoding.Latin1.GetString(d, p, (int)chars).TrimEnd('\0');
    }
}
