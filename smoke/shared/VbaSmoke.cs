using System.Text;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Reads a tiny VBA project, decompresses a specification example, and removes a project from a small Excel 97-2003 file, under NativeAOT and WASM.</summary>
internal static class VbaSmoke
{
    // A container of literal tokens (flag byte 0 before each group of eight bytes).
    private static byte[] Literal(byte[] data)
    {
        var o = new List<byte> { 0x01 };
        var chunk = new List<byte>();
        for (int i = 0; i < data.Length; i += 8)
        {
            chunk.Add(0);
            for (int j = i; j < Math.Min(i + 8, data.Length); j++)
            {
                chunk.Add(data[j]);
            }
        }

        int header = ((chunk.Count + 2 - 3) & 0x0FFF) | 0x3000 | 0x8000;
        o.Add((byte)header);
        o.Add((byte)(header >> 8));
        o.AddRange(chunk);
        return [.. o];
    }

    private static void Id(List<byte> o, int id, byte[] payload)
    {
        o.AddRange([(byte)id, (byte)(id >> 8), (byte)payload.Length, (byte)(payload.Length >> 8), 0, 0]);
        o.AddRange(payload);
    }

    private static byte[] Project()
    {
        var dir = new List<byte>();
        Id(dir, 1, [1, 0, 0, 0]);
        Id(dir, 2, [9, 4, 0, 0]);
        Id(dir, 0x14, [9, 4, 0, 0]);
        Id(dir, 3, [0xE4, 0x04]);
        Id(dir, 4, Encoding.ASCII.GetBytes("Smoke"));
        Id(dir, 0x0F, [1, 0]);
        Id(dir, 0x13, [0xFF, 0xFF]);
        Id(dir, 0x19, Encoding.ASCII.GetBytes("Module1"));
        Id(dir, 0x1A, Encoding.ASCII.GetBytes("Module1"));
        Id(dir, 0x31, [0, 0, 0, 0]);
        Id(dir, 0x21, []);
        Id(dir, 0x2B, []);
        dir.AddRange([0x10, 0, 0, 0, 0, 0]);
        var cfb = CfbPackage.Create();
        cfb.AddPart("/VBA/dir", string.Empty, Literal([.. dir]));
        cfb.AddPart("/VBA/Module1", string.Empty, Literal(Encoding.ASCII.GetBytes("Attribute VB_Name = \"Module1\"\r\nSub A()\r\nEnd Sub\r\n")));
        cfb.AddPart("/PROJECT", string.Empty, Encoding.ASCII.GetBytes("ID=\"{00000000-0000-0000-0000-000000000000}\"\r\nModule=Module1\r\n"));
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    private static byte[] Rec(int type, params byte[] data) => [(byte)type, (byte)(type >> 8), (byte)data.Length, (byte)(data.Length >> 8), .. data];

    public static List<string> Run()
    {
        var failures = new List<string>();
        VbaProject p = VbaProject.FromProjectStorage(Project());
        if (p.Name != "Smoke" || p.Modules.Count != 1 || !p.Modules[0].Source.Contains("Sub A()", StringComparison.Ordinal)) failures.Add("vba project");
        if (p.Modules[0].ExportText().Length == 0 || p.IsSigned || p.IsLocked) failures.Add("vba export");

        // [MS-OVBA] section 3.2.3: 73 letters a in seven bytes.
        if (!VbaCompression.TryDecompress([0x01, 0x03, 0xB0, 0x02, 0x61, 0x45, 0x00], 1 << 20, out byte[] a, out _) || a.Length != 73) failures.Add("vba compression");

        var xls = CfbPackage.Create();
        xls.AddPart("/Workbook", string.Empty, (byte[])[.. Rec(0x0809, new byte[16]), .. Rec(0x00D3), .. Rec(0x000A)]);
        using CfbPackage inner = CfbPackage.Open(ByteSource.FromMemory(Project()));
        foreach (CfbStreamPart s in inner.Streams)
        {
            xls.AddPart("/_VBA_PROJECT_CUR" + s.Name, string.Empty, s.GetBytes());
        }

        using var file = new MemoryStream();
        xls.WriteTo(file);
        using var output = new MemoryStream();
        VbaRemovalReport r = VbaRemoval.Remove(new MemoryStream(file.ToArray()), output);
        if (!r.HadProject || r.ModulesRemoved != 1 || r.Removed.Count < 2) failures.Add("vba removal");
        using CfbPackage back = CfbPackage.Open(ByteSource.FromMemory(output.ToArray()));
        if (back.Streams.Count != 1 || back.FindPart("/Workbook")!.Length != 24) failures.Add("vba removed result");
        return failures;
    }
}
