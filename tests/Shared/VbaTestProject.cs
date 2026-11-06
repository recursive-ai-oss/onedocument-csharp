using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Cfb;

namespace RecursiveAI.OneDocument.Tests;

/// <summary>
/// Builds VBA projects as compound files from the layout in [MS-OVBA] §2.2 and §2.3, for tests. Source is compressed with literal tokens only
/// (a valid container the decompressor must read), so nothing depends on the production decompressor's own choices.
/// </summary>
internal sealed class VbaTestProject
{
    internal sealed record Module(string Name, string Kind, string Source, byte[]? Cache = null, string? StreamName = null, bool Raw = false);

    public string ProjectName { get; set; } = "VBAProject";

    public int CodePage { get; set; } = 1252;

    public List<Module> Modules { get; } = [];

    public List<(string Name, string Libid)> RegisteredReferences { get; } = [];

    public ushort VbaVersion { get; set; } = 0xFFFF;

    public byte[]? VbaProjectCache { get; set; }

    public bool Cmg { get; set; }

    public string? Password { get; set; }

    public bool Hidden { get; set; }

    public bool CorruptDpb { get; set; }

    public bool Signature { get; set; }

    public bool OmitDir { get; set; }

    public List<string> ExtraVbaStreams { get; } = [];

    /// <summary>Written as the module count when set, whatever the list holds.</summary>
    public int? DeclaredModuleCount { get; set; }

    /// <summary>Applied to the decompressed dir stream before it is compressed.</summary>
    public Func<byte[], byte[]>? DirTransform { get; set; }

    /// <summary>[MS-OVBA] §2.4.1: a container of literal-only chunks.</summary>
    internal static byte[] Compress(ReadOnlySpan<byte> data)
    {
        var o = new List<byte> { 0x01 };
        int pos = 0;
        do
        {
            int take = Math.Min(3000, data.Length - pos); // 3000 literals and their flag bytes fit the 12-bit chunk size field
            var chunk = new List<byte>();
            for (int i = 0; i < take; i += 8)
            {
                int n = Math.Min(8, take - i);
                chunk.Add(0x00);
                for (int j = 0; j < n; j++)
                {
                    chunk.Add(data[pos + i + j]);
                }
            }

            int size = chunk.Count + 2;
            ushort header = (ushort)(((size - 3) & 0x0FFF) | 0x3000 | 0x8000);
            o.Add((byte)header);
            o.Add((byte)(header >> 8));
            o.AddRange(chunk);
            pos += take;
        }
        while (pos < data.Length);
        return [.. o];
    }

    /// <summary>[MS-OVBA] §2.4.1.3.10: a container of raw chunks (flag bit clear), each exactly 4096 bytes.</summary>
    internal static byte[] CompressRaw(ReadOnlySpan<byte> data)
    {
        var o = new List<byte> { 0x01 };
        for (int pos = 0; pos < data.Length; pos += 4096)
        {
            o.Add(0xFF);
            o.Add(0x3F);
            for (int i = 0; i < 4096; i++)
            {
                o.Add(pos + i < data.Length ? data[pos + i] : (byte)0);
            }
        }

        return [.. o];
    }

    static VbaTestProject() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>[MS-OVBA] §2.4.3.2 Data Encryption.</summary>
    internal static string Encrypt(byte[] data, byte seed, string projectClsid)
    {
        byte projKey = 0;
        foreach (char c in projectClsid)
        {
            projKey = (byte)(projKey + (byte)c);
        }

        byte versionEnc = (byte)(seed ^ 2);
        byte projKeyEnc = (byte)(seed ^ projKey);
        byte unenc1 = projKey, enc1 = projKeyEnc, enc2 = versionEnc;
        var o = new List<byte> { seed, versionEnc, projKeyEnc };
        void Put(byte b)
        {
            byte e = (byte)(b ^ (byte)(enc2 + unenc1));
            o.Add(e);
            enc2 = enc1;
            enc1 = e;
            unenc1 = b;
        }

        for (int i = 0; i < (seed & 6) / 2; i++)
        {
            Put(0x5A);
        }

        for (int i = 0; i < 4; i++)
        {
            Put((byte)(data.Length >> (8 * i)));
        }

        foreach (byte b in data)
        {
            Put(b);
        }

        return Convert.ToHexString([.. o]);
    }

    private sealed class Rec
    {
        private readonly List<byte> _bytes = [];

        public void Id(ushort id) => _bytes.AddRange(BitConverter.GetBytes(id));

        public void U32(uint v) => _bytes.AddRange(BitConverter.GetBytes(v));

        public void U16(ushort v) => _bytes.AddRange(BitConverter.GetBytes(v));

        public void Raw(byte[] b) => _bytes.AddRange(b);

        public void Sized(ushort id, byte[] b)
        {
            Id(id);
            U32((uint)b.Length);
            Raw(b);
        }

        public byte[] ToArray() => [.. _bytes];
    }

    internal byte[] BuildDir(Encoding mbcs)
    {
        var r = new Rec();
        r.Id(1); r.U32(4); r.U32(1);
        r.Id(2); r.U32(4); r.U32(0x409);
        r.Id(0x14); r.U32(4); r.U32(0x409);
        r.Id(3); r.U32(2); r.U16((ushort)CodePage);
        r.Sized(4, mbcs.GetBytes(ProjectName));
        r.Id(5); r.U32(0); r.U16(0x40); r.U32(0);
        r.Id(6); r.U32(0); r.U16(0x3D); r.U32(0);
        r.Id(7); r.U32(4); r.U32(0);
        r.Id(8); r.U32(4); r.U32(0);
        r.Id(9); r.U32(4); r.U32(1355); r.U16(7);
        foreach ((string name, string libid) in RegisteredReferences)
        {
            r.Sized(0x16, mbcs.GetBytes(name));
            r.U16(0x3E); r.U32((uint)name.Length * 2); r.Raw(Encoding.Unicode.GetBytes(name));
            byte[] lb = mbcs.GetBytes(libid);
            r.Id(0x0D); r.U32((uint)(4 + lb.Length + 6)); r.U32((uint)lb.Length); r.Raw(lb); r.U32(0); r.U16(0);
        }

        r.Id(0x0F); r.U32(2); r.U16((ushort)(DeclaredModuleCount ?? Modules.Count));
        r.Id(0x13); r.U32(2); r.U16(0xFFFF);
        foreach (Module m in Modules)
        {
            string stream = m.StreamName ?? m.Name;
            r.Sized(0x19, mbcs.GetBytes(m.Name));
            r.Sized(0x47, Encoding.Unicode.GetBytes(m.Name));
            r.Sized(0x1A, mbcs.GetBytes(stream));
            r.U16(0x32); r.U32((uint)stream.Length * 2); r.Raw(Encoding.Unicode.GetBytes(stream));
            r.Id(0x1C); r.U32(0); r.U16(0x48); r.U32(0);
            r.Id(0x31); r.U32(4); r.U32((uint)(m.Cache?.Length ?? 0));
            r.Id(0x1E); r.U32(4); r.U32(0);
            r.Id(0x2C); r.U32(2); r.U16(0xFFFF);
            r.Id(m.Kind == "Module" ? (ushort)0x21 : (ushort)0x22); r.U32(0);
            r.Id(0x2B); r.U32(0);
        }

        r.Id(0x10); r.U32(0);
        return r.ToArray();
    }

    internal string BuildProjectText()
    {
        const string clsid = "{00000000-0000-0000-0000-000000000000}";
        var sb = new StringBuilder();
        sb.Append("ID=\"").Append(clsid).Append("\"\r\n");
        foreach (Module m in Modules)
        {
            sb.Append(m.Kind switch
            {
                "Document" => "Document=" + m.Name + "/&H00000000",
                "Class" => "Class=" + m.Name,
                "Designer" => "BaseClass=" + m.Name,
                _ => "Module=" + m.Name,
            }).Append("\r\n");
        }

        sb.Append("Name=\"").Append(ProjectName).Append("\"\r\nHelpContextID=\"0\"\r\nVersionCompatible32=\"393222000\"\r\n");
        sb.Append("CMG=\"").Append(Encrypt(Cmg ? [3, 0, 0, 0] : [0, 0, 0, 0], 0x07, clsid)).Append("\"\r\n");
        string dpb = CorruptDpb ? "ZZ" : Encrypt(Password is null ? [0] : [.. Encoding.ASCII.GetBytes(Password), 0], 0x0E, clsid);
        sb.Append("DPB=\"").Append(dpb).Append("\"\r\n");
        sb.Append("GC=\"").Append(Encrypt([Hidden ? (byte)0 : (byte)0xFF], 0x15, clsid)).Append("\"\r\n\r\n");
        sb.Append("[Host Extender Info]\r\n&H00000001={3832D640-CF90-11CF-8E43-00A0C911005A};VBE;&H00000000\r\n\r\n[Workspace]\r\n");
        foreach (Module m in Modules)
        {
            sb.Append(m.Name).Append("=0, 0, 0, 0, C\r\n");
        }

        return sb.ToString();
    }

    /// <summary>Builds the project as a compound file (the content of a <c>vbaProject.bin</c>); put it under a storage with <paramref name="root"/>.</summary>
    internal byte[] Build(string root = "")
    {
        Encoding mbcs = Encoding.GetEncoding(CodePage);
        var pkg = CfbPackage.Create();
        string P(string s) => root + "/" + s;
        pkg.AddPart(P("PROJECT"), string.Empty, mbcs.GetBytes(BuildProjectText()));
        pkg.AddPart(P("PROJECTwm"), string.Empty, new byte[] { 0, 0 });
        byte[] ver = [0xCC, 0x61, (byte)VbaVersion, (byte)(VbaVersion >> 8), 0, 0, 0];
        pkg.AddPart(P("VBA/_VBA_PROJECT"), string.Empty, (byte[])[.. ver, .. VbaProjectCache ?? []]);
        if (!OmitDir)
        {
            byte[] dir = BuildDir(mbcs);
            pkg.AddPart(P("VBA/dir"), string.Empty, Compress(DirTransform is null ? dir : DirTransform(dir)));
        }

        foreach (Module m in Modules)
        {
            byte[] src = mbcs.GetBytes(m.Source);
            byte[] body = m.Raw ? src : Compress(src);
            pkg.AddPart(P("VBA/" + (m.StreamName ?? m.Name)), string.Empty, (byte[])[.. m.Cache ?? [], .. body]);
        }

        foreach (string extra in ExtraVbaStreams)
        {
            pkg.AddPart(P("VBA/" + extra), string.Empty, new byte[] { 1, 2, 3 });
        }

        foreach (Module m in Modules)
        {
            if (m.Kind == "Designer")
            {
                pkg.AddPart(P(m.Name + "/\u0003VBFrame"), string.Empty, mbcs.GetBytes("VersionCompatible32=\"393222000\"\r\nCaption=\"Form\"\r\n"));
                pkg.AddPart(P(m.Name + "/o"), string.Empty, new byte[] { 9, 9, 9, 9 });
            }
        }

        if (Signature)
        {
            pkg.AddPart(P("\u0005DigitalSignature"), string.Empty, new byte[] { 0x30, 0x03 });
        }

        using var ms = new MemoryStream();
        pkg.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>A small project with a module of each kind.</summary>
    internal static VbaTestProject Typical()
    {
        var p = new VbaTestProject();
        p.RegisteredReferences.Add(("stdole", "*\\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\\Windows\\system32\\stdole2.tlb#OLE Automation"));
        p.Modules.Add(new Module("ThisDocument", "Document", "Attribute VB_Name = \"ThisDocument\"\r\nAttribute VB_Base = \"1Normal.ThisDocument\"\r\nOption Explicit\r\nPrivate Sub Document_Open()\r\n  MsgBox \"hello\"\r\nEnd Sub\r\n"));
        p.Modules.Add(new Module("Module1", "Module", "Attribute VB_Name = \"Module1\"\r\nSub Run()\r\n  Debug.Print \"é€\"\r\nEnd Sub\r\n"));
        p.Modules.Add(new Module("Class1", "Class", "Attribute VB_Name = \"Class1\"\r\nPublic Value As Long\r\n"));
        p.Modules.Add(new Module("UserForm1", "Designer", "Attribute VB_Name = \"UserForm1\"\r\nPrivate Sub CommandButton1_Click()\r\nEnd Sub\r\n"));
        return p;
    }

    internal static byte[] U32(uint v)
    {
        byte[] b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }
}
