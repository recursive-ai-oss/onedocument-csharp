using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Vba;

public sealed partial class VbaProject
{
    // [MS-OVBA] §2.3.4.2: record identifiers of the dir stream.
    private const ushort IdSysKind = 0x01, IdLcid = 0x02, IdCodePage = 0x03, IdName = 0x04, IdDocString = 0x05, IdHelpFile = 0x06, IdHelpContext = 0x07;
    private const ushort IdLibFlags = 0x08, IdVersion = 0x09, IdModules = 0x0F, IdConstants = 0x0C, IdReferenceRegistered = 0x0D, IdReferenceProject = 0x0E;
    private const ushort IdTerminator = 0x10, IdLcidInvoke = 0x14, IdModuleCookieProject = 0x13, IdReferenceName = 0x16, IdModuleName = 0x19, IdModuleStreamName = 0x1A;
    private const ushort IdModuleDocString = 0x1C, IdModuleHelpContext = 0x1E, IdModuleTypeProcedural = 0x21, IdModuleTypeOther = 0x22, IdModuleReadOnly = 0x25;
    private const ushort IdModulePrivate = 0x28, IdModuleTerminator = 0x2B, IdModuleCookie = 0x2C, IdReferenceControl = 0x2F, IdModuleOffset = 0x31;
    private const ushort IdReferenceOriginal = 0x33, IdCompatVersion = 0x4A, IdModuleNameUnicode = 0x47;

    /// <summary>Reads the project stored under <paramref name="root"/> of a compound file.</summary>
    /// <param name="cfb">The compound file.</param>
    /// <param name="root">The project root storage's path (<c>""</c> for <c>vbaProject.bin</c>, <c>"/Macros"</c>, <c>"/_VBA_PROJECT_CUR"</c>).</param>
    /// <param name="container">The name to put in locations and <see cref="Container"/>.</param>
    /// <param name="context">Limits and defect sink.</param>
    /// <param name="extraSignatures">Signature kinds found outside the compound file (OOXML signature parts).</param>
    /// <returns>The project; never null. A project whose <c>dir</c> stream is missing has no modules and a <c>vba.dir-missing</c> defect.</returns>
    internal static VbaProject Parse(CfbPackage cfb, string root, string container, OpenContext context, VbaSignatureKinds extraSignatures = VbaSignatureKinds.None)
    {
        var project = new VbaProject(container);
        var storage = new Storage(cfb, root);
        var reader = new Reader(project, storage, container, context);
        reader.Run();
        project.Signatures = extraSignatures | VbaSignatures.Detect(cfb);
        return project;
    }

    private sealed class Storage(CfbPackage cfb, string root)
    {
        public string Path(string relative) => root + "/" + relative;

        public CfbStreamPart? Find(string relative) => cfb.FindPart(Path(relative)) as CfbStreamPart;

        public byte[]? Read(string relative) => Find(relative)?.GetBytes().ToArray();

        public IEnumerable<string> Streams()
        {
            string prefix = root + "/";
            foreach (CfbStreamPart p in cfb.Streams)
            {
                if (p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return p.Name[prefix.Length..];
                }
            }
        }

        public IEnumerable<string> Storages()
        {
            string prefix = root + "/";
            foreach (CfbStorage s in cfb.Storages)
            {
                if (s.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return s.Path[prefix.Length..];
                }
            }
        }
    }

    private sealed class Reader(VbaProject project, Storage storage, string container, OpenContext ctx)
    {
        private Encoding _encoding = Encoding.Latin1;
        private readonly Dictionary<string, VbaModuleKind> _kinds = new(StringComparer.OrdinalIgnoreCase);

        public void Run()
        {
            ReadProjectStream();
            ReadDir();
            ReadVbaProjectStream();
            Finish();
        }

        private void Report(string code, DefectSeverity severity, string message, string stream, long offset = -1, DefectKind kind = DefectKind.Record) =>
            ctx.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InRecord(container + "!" + stream, offset)));

        // ---- the PROJECT stream ------------------------------------------------------------------------------------------------

        private void ReadProjectStream()
        {
            byte[]? raw = storage.Read("PROJECT");
            if (raw is null)
            {
                Report("vba.project-stream-missing", DefectSeverity.Repaired, "The PROJECT stream is missing; module kinds are guessed from the dir stream and protection is reported as none.", "/PROJECT");
                return;
            }

            // [MS-OVBA] §2.3.1: MBCS text in the project code page. The code page is in dir, which is read next, so decode as Latin-1 first
            // (lossless for the ASCII keys) and keep the raw bytes for a second decode once the code page is known.
            string text = Encoding.Latin1.GetString(raw);
            project.ProjectText = text;
            _rawProject = raw;
            foreach (string line in text.Split(["\r\n", "\n\r", "\n", "\r"], StringSplitOptions.None))
            {
                if (line.StartsWith('['))
                {
                    break; // [Host Extender Info] and [Workspace] follow the project properties.
                }

                int eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                {
                    continue;
                }

                string key = line[..eq];
                string value = line[(eq + 1)..];
                switch (key)
                {
                    case "ID":
                        project.ProjectId = Unquote(value);
                        break;
                    case "Document":
                        _kinds[ModuleNameOf(value, '/')] = VbaModuleKind.Document;
                        break;
                    case "Module":
                        _kinds[value.Trim()] = VbaModuleKind.Standard;
                        break;
                    case "Class":
                        _kinds[value.Trim()] = VbaModuleKind.Class;
                        break;
                    case "BaseClass":
                        _kinds[value.Trim()] = VbaModuleKind.Designer;
                        break;
                    case "CMG":
                        _cmg = Unquote(value);
                        break;
                    case "DPB":
                        _dpb = Unquote(value);
                        break;
                    case "GC":
                        _gc = Unquote(value);
                        break;
                }
            }

            project.Protection = VbaDataEncryption.Interpret(_cmg, _dpb, _gc, out bool unreadable);
            if (unreadable)
            {
                Report("vba.protection-unreadable", DefectSeverity.Repaired, "One of CMG, DPB or GC cannot be undone; the project is reported as locked.", "/PROJECT");
            }
        }

        private byte[]? _rawProject;
        private string? _cmg, _dpb, _gc;

        private static string Unquote(string v) => v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v.Trim('"');

        private static string ModuleNameOf(string value, char sep)
        {
            int i = value.IndexOf(sep, StringComparison.Ordinal);
            return (i < 0 ? value : value[..i]).Trim();
        }

        // ---- the dir stream ---------------------------------------------------------------------------------------------------

        private void ReadDir()
        {
            byte[]? compressed = storage.Read("VBA/dir");
            if (compressed is null)
            {
                Report("vba.dir-missing", DefectSeverity.Dropped, "The VBA storage has no dir stream, so the project's modules cannot be listed.", "/VBA/dir");
                return;
            }

            long cap = Math.Min(ctx.Limits.MaxPartSize, ctx.Limits.MaxAllocationBytes);
            if (!VbaCompression.TryDecompress(compressed, cap, out byte[] dir, out string? problem))
            {
                Report("vba.dir-damaged", DefectSeverity.Repaired, "The dir stream is damaged; the records before the damage are used. " + problem, "/VBA/dir");
            }

            try
            {
                ParseDir(dir);
            }
            catch (FormatException ex)
            {
                Report("vba.dir-record", DefectSeverity.Repaired, "The dir stream stops making sense: " + ex.Message, "/VBA/dir", _pos);
            }
        }

        private byte[] _dir = [];
        private int _pos;

        private ushort U16()
        {
            Need(2);
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_dir.AsSpan(_pos));
            _pos += 2;
            return v;
        }

        private uint U32()
        {
            Need(4);
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(_dir.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        private void Need(long n)
        {
            if (n < 0 || _pos + n > _dir.Length)
            {
                throw new FormatException("a record is cut short");
            }
        }

        private byte[] Bytes(uint n)
        {
            Need(n);
            byte[] b = _dir.AsSpan(_pos, (int)n).ToArray();
            _pos += (int)n;
            return b;
        }

        private string Mbcs(byte[] b) => _encoding.GetString(b);

        // A string record: the MBCS bytes, and where the record has them, a reserved word and the UTF-16 bytes. Prefer the UTF-16 form.
        private string StringWithUnicode(uint size, ushort reservedId)
        {
            string mbcs = Mbcs(Bytes(size));
            if (_pos + 6 <= _dir.Length && BinaryPrimitives.ReadUInt16LittleEndian(_dir.AsSpan(_pos)) == reservedId)
            {
                _pos += 2;
                uint u = U32();
                byte[] uni = Bytes(u);
                return Encoding.Unicode.GetString(uni);
            }

            return mbcs;
        }

        private void ParseDir(byte[] dir)
        {
            _dir = dir;
            _pos = 0;
            string? pendingName = null;

            // PROJECTINFORMATION: records until PROJECTREFERENCES / PROJECTMODULES begin.
            while (_pos + 2 <= _dir.Length)
            {
                int recordStart = _pos;
                ushort id = U16();
                switch (id)
                {
                    case IdSysKind:
                        Expect(U32(), 4);
                        uint sk = U32();
                        project.SysKind = sk <= 3 ? (VbaSysKind)sk : VbaSysKind.Unknown;
                        break;
                    case IdCompatVersion:
                        Expect(U32(), 4);
                        project.CompatVersion = U32();
                        break;
                    case IdLcid:
                        Expect(U32(), 4);
                        project.Lcid = (int)U32();
                        break;
                    case IdLcidInvoke:
                        Expect(U32(), 4);
                        U32();
                        break;
                    case IdCodePage:
                        Expect(U32(), 2);
                        project.CodePage = U16();
                        SetEncoding(project.CodePage);
                        break;
                    case IdName:
                        project.Name = Mbcs(Bytes(U32()));
                        break;
                    case IdDocString:
                        project.Description = StringWithUnicode(U32(), 0x40);
                        break;
                    case IdHelpFile:
                        {
                            uint s1 = U32();
                            string help = Mbcs(Bytes(s1));
                            if (_pos + 6 <= _dir.Length && BinaryPrimitives.ReadUInt16LittleEndian(_dir.AsSpan(_pos)) == 0x3D)
                            {
                                _pos += 2;
                                Bytes(U32());
                            }

                            project.HelpFile = help.Length == 0 ? null : help;
                            break;
                        }

                    case IdHelpContext:
                        Expect(U32(), 4);
                        U32();
                        break;
                    case IdLibFlags:
                        Expect(U32(), 4);
                        U32();
                        break;
                    case IdVersion:
                        // [MS-OVBA] §2.3.4.2.1.11: Reserved (4, always 4), VersionMajor (4), VersionMinor (2).
                        U32();
                        project.VersionMajor = U32();
                        project.VersionMinor = U16();
                        break;
                    case IdConstants:
                        project.Constants = StringWithUnicode(U32(), 0x3C);
                        break;

                    // PROJECTREFERENCES
                    case IdReferenceName:
                        pendingName = StringWithUnicode(U32(), 0x3E);
                        break;
                    case IdReferenceRegistered:
                        {
                            U32(); // Size
                            string libid = Mbcs(Bytes(U32()));
                            U32();
                            U16(); // Reserved1, Reserved2
                            project.Add(new VbaReference(VbaReferenceKind.Registered, pendingName, libid, null, 0, 0));
                            pendingName = null;
                            break;
                        }

                    case IdReferenceProject:
                        {
                            U32(); // Size
                            string absolute = Mbcs(Bytes(U32()));
                            string relative = Mbcs(Bytes(U32()));
                            uint major = U32();
                            ushort minor = U16();
                            project.Add(new VbaReference(VbaReferenceKind.Project, pendingName, absolute, relative, major, minor));
                            pendingName = null;
                            break;
                        }

                    case IdReferenceControl:
                        {
                            U32(); // SizeTwiddled
                            string twiddled = Mbcs(Bytes(U32()));
                            U32();
                            U16(); // Reserved1, Reserved2
                            string? name = pendingName;
                            if (_pos + 2 <= _dir.Length && BinaryPrimitives.ReadUInt16LittleEndian(_dir.AsSpan(_pos)) == IdReferenceName)
                            {
                                _pos += 2;
                                name = StringWithUnicode(U32(), 0x3E);
                            }

                            U16(); // Reserved3 (0x0030)
                            U32(); // SizeExtended
                            string extended = Mbcs(Bytes(U32()));
                            U32();
                            U16(); // Reserved4, Reserved5
                            Bytes(16); // OriginalTypeLib
                            U32(); // Cookie
                            project.Add(new VbaReference(VbaReferenceKind.Control, name, extended.Length > 0 ? extended : twiddled, twiddled.Length > 0 && extended.Length > 0 ? twiddled : null, 0, 0));
                            pendingName = null;
                            break;
                        }

                    case IdReferenceOriginal:
                        {
                            string libid = Mbcs(Bytes(U32()));
                            project.Add(new VbaReference(VbaReferenceKind.Original, pendingName, libid, null, 0, 0));
                            pendingName = null;
                            break;
                        }

                    case IdModules:
                        Expect(U32(), 2);
                        ushort count = U16();
                        ParseModules(count);
                        return;
                    case IdTerminator:
                        return; // the terminator with no modules record: a project of references only
                    default:
                        // [MS-OVBA] §2.3.4.2.1: unknown records are skipped by their size, which every defined record carries first.
                        {
                            uint size = U32();
                            Need(size);
                            _pos += (int)size;
                            Report("vba.dir-record-unknown", DefectSeverity.Repaired, "The dir stream has a record with the unknown identifier 0x" + id.ToString("X4", CultureInfo.InvariantCulture) + "; it is skipped.", "/VBA/dir", recordStart);
                            break;
                        }
                }

            }
        }

        private static void Expect(uint actual, uint expected)
        {
            if (actual != expected)
            {
                throw new FormatException("a size field is " + actual.ToString(CultureInfo.InvariantCulture) + " where " + expected.ToString(CultureInfo.InvariantCulture) + " is required");
            }
        }

        private void SetEncoding(int codePage)
        {
            if (!VbaCodePages.Supported(codePage))
            {
                Report("vba.codepage-unsupported", DefectSeverity.Repaired, "The project code page " + codePage.ToString(CultureInfo.InvariantCulture) + " is not available; Latin-1 is used for byte strings.", "/VBA/dir", _pos);
            }

            _encoding = VbaCodePages.Get(codePage);
            if (_rawProject is not null)
            {
                project.ProjectText = _encoding.GetString(_rawProject);
            }
        }

        private void ParseModules(ushort count)
        {
            if (count > ctx.Limits.MaxRecordCount)
            {
                throw new OfficeLimitException(nameof(OfficeLimits.MaxRecordCount), ctx.Limits.MaxRecordCount, count, DefectLocation.InRecord(container + "!/VBA/dir", _pos));
            }

            if (_pos + 2 <= _dir.Length && BinaryPrimitives.ReadUInt16LittleEndian(_dir.AsSpan(_pos)) == IdModuleCookieProject)
            {
                _pos += 2;
                Expect(U32(), 2);
                U16();
            }

            for (int i = 0; i < count; i++)
            {
                // [MS-OVBA] §2.3.4.2: the dir stream ends with the 0x0010 terminator, so meeting it early means fewer modules than announced.
                if (_pos + 2 > _dir.Length || BinaryPrimitives.ReadUInt16LittleEndian(_dir.AsSpan(_pos)) == IdTerminator)
                {
                    Report("vba.module-list-short", DefectSeverity.Repaired, "The dir stream announces " + count.ToString(CultureInfo.InvariantCulture) + " modules and holds " + i.ToString(CultureInfo.InvariantCulture) + ".", "/VBA/dir", _pos);
                    return;
                }

                ParseModule();
            }
        }

        private void ParseModule()
        {
            string? name = null, nameUnicode = null, stream = null, doc = null;
            uint offset = 0;
            bool procedural = false, readOnly = false, isPrivate = false, typeSeen = false;
            while (true)
            {
                ushort id = U16();
                switch (id)
                {
                    case IdModuleName:
                        name = Mbcs(Bytes(U32()));
                        break;
                    case IdModuleNameUnicode:
                        nameUnicode = Encoding.Unicode.GetString(Bytes(U32()));
                        break;
                    case IdModuleStreamName:
                        stream = StringWithUnicode(U32(), 0x32);
                        break;
                    case IdModuleDocString:
                        doc = StringWithUnicode(U32(), 0x48);
                        break;
                    case IdModuleOffset:
                        Expect(U32(), 4);
                        offset = U32();
                        break;
                    case IdModuleHelpContext:
                        Expect(U32(), 4);
                        U32();
                        break;
                    case IdModuleCookie:
                        Expect(U32(), 2);
                        U16();
                        break;
                    case IdModuleTypeProcedural:
                    case IdModuleTypeOther:
                        U32();
                        procedural = id == IdModuleTypeProcedural;
                        typeSeen = true;
                        break;
                    case IdModuleReadOnly:
                        U32();
                        readOnly = true;
                        break;
                    case IdModulePrivate:
                        U32();
                        isPrivate = true;
                        break;
                    case IdModuleTerminator:
                        U32();
                        AddModule(name, nameUnicode, stream, doc, offset, procedural, typeSeen, readOnly, isPrivate);
                        return;
                    default:
                        {
                            uint size = U32();
                            Need(size);
                            _pos += (int)size;
                            Report("vba.dir-record-unknown", DefectSeverity.Repaired, "A MODULE record has the unknown identifier 0x" + id.ToString("X4", CultureInfo.InvariantCulture) + "; it is skipped.", "/VBA/dir", _pos);
                            break;
                        }
                }
            }
        }

        private readonly HashSet<string> _listedStreams = new(StringComparer.OrdinalIgnoreCase);

        private void AddModule(string? name, string? nameUnicode, string? stream, string? doc, uint offset, bool procedural, bool typeSeen, bool readOnly, bool isPrivate)
        {
            string moduleName = nameUnicode ?? name ?? stream ?? "(unnamed)";
            string streamName = stream ?? name ?? moduleName;
            VbaModuleKind kind = _kinds.TryGetValue(name ?? moduleName, out VbaModuleKind k) ? k
                : _kinds.TryGetValue(moduleName, out k) ? k
                : typeSeen && !procedural ? VbaModuleKind.Class : VbaModuleKind.Standard;
            var module = new VbaModule(moduleName, streamName, kind, _encoding)
            {
                IsReadOnly = readOnly,
                IsPrivate = isPrivate,
                DocString = string.IsNullOrEmpty(doc) ? null : doc,
                TextOffset = offset,
            };
            _listedStreams.Add(streamName);
            ReadSource(module);
            if (kind == VbaModuleKind.Designer)
            {
                module.DesignerStorage = streamName;
            }

            project.Add(module);
        }

        private void ReadSource(VbaModule module)
        {
            string path = "VBA/" + module.StreamName;
            byte[]? data = storage.Read(path);
            if (data is null)
            {
                Report("vba.module-stream-missing", DefectSeverity.Dropped, "The module '" + module.Name + "' has no stream '" + module.StreamName + "' in the VBA storage; it has no source.", "/" + path, kind: DefectKind.Reference);
                return;
            }

            module.StreamLength = data.Length;
            if (module.TextOffset > data.Length)
            {
                Report("vba.module-offset-invalid", DefectSeverity.Dropped, "The source offset of module '" + module.Name + "' (" + module.TextOffset.ToString(CultureInfo.InvariantCulture) + ") is past the end of its stream (" + data.Length.ToString(CultureInfo.InvariantCulture) + " bytes); it has no source.", "/" + path, module.TextOffset, DefectKind.Reference);
                return;
            }

            long cap = Math.Min(ctx.Limits.MaxPartSize, ctx.Limits.MaxAllocationBytes);
            ReadOnlySpan<byte> container = data.AsSpan((int)module.TextOffset);
            if (container.IsEmpty)
            {
                return; // p-code only: a cache and no source text
            }

            bool ok = VbaCompression.TryDecompress(container, cap, out byte[] source, out string? problem);
            module.SourceBytes = source;
            module.SourceAvailable = ok;
            module.SourceDamaged = !ok;
            if (!ok)
            {
                Report("vba.module-source-damaged", DefectSeverity.Repaired, "The source of module '" + module.Name + "' is damaged; the text before the damage is kept. " + problem, "/" + path, module.TextOffset);
            }
        }

        // ---- the _VBA_PROJECT stream and the cross-checks --------------------------------------------------------------------

        private void ReadVbaProjectStream()
        {
            CfbStreamPart? part = storage.Find("VBA/_VBA_PROJECT");
            if (part is null)
            {
                Report("vba.version-stream-missing", DefectSeverity.Repaired, "The VBA storage has no _VBA_PROJECT stream.", "/VBA/_VBA_PROJECT");
                return;
            }

            // [MS-OVBA] §2.3.4.1: Reserved1 (2), Version (2), Reserved2 (1), Reserved3 (2), then the performance cache.
            ReadOnlySpan<byte> head = part.GetBytes().Span;
            if (head.Length < 7)
            {
                Report("vba.version-stream-short", DefectSeverity.Repaired, "The _VBA_PROJECT stream is shorter than its seven fixed bytes.", "/VBA/_VBA_PROJECT", head.Length);
                return;
            }

            project.PerformanceCacheVersion = BinaryPrimitives.ReadUInt16LittleEndian(head[2..]);
            if (head.Length > 7)
            {
                project.HasPCode = true;
            }
        }

        private void Finish()
        {
            foreach (string name in storage.Streams())
            {
                if (!name.StartsWith("VBA/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string leaf = name[4..];
                if (leaf.Equals("dir", StringComparison.OrdinalIgnoreCase) || leaf.Equals("_VBA_PROJECT", StringComparison.OrdinalIgnoreCase)
                    || leaf.StartsWith("__SRP_", StringComparison.OrdinalIgnoreCase) || _listedStreams.Contains(leaf))
                {
                    continue;
                }

                project.AddUnlisted(leaf);
            }

            foreach (string s in storage.Storages())
            {
                if (!s.Contains('/', StringComparison.Ordinal) && !s.Equals("VBA", StringComparison.OrdinalIgnoreCase))
                {
                    project.AddDesigner(s);
                }
            }

            bool cacheLive = project.PerformanceCacheVersion is { } v && v != 0xFFFF;
            bool anyModuleCache = false;
            bool cachedWithoutSource = false;
            foreach (VbaModule m in project.Modules)
            {
                if (m.HasPerformanceCache)
                {
                    anyModuleCache = true;
                    if (!m.SourceAvailable || m.SourceBytes.Length == 0)
                    {
                        cachedWithoutSource = true;
                    }
                }
            }

            if (anyModuleCache)
            {
                project.HasPCode = true;
            }

            // The version-dependent caches are used by an Office that matches the version; with a cache present and the source gone, the
            // project runs from the cache and the source shown or extracted says nothing about it.
            project.SourceAndPCodeMayDisagree = cacheLive && (cachedWithoutSource || project.UnlistedStreams.Count > 0);
        }
    }
}
