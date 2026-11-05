using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace RecursiveAI.OneDocument.Tools.RoundTrip;

/// <summary>
/// Compares two OPC packages (zip or Flat OPC) part by part and writes one JSON line per finding.
/// Usage: RoundTrip &lt;before&gt; &lt;after&gt; [--json file] [--quiet]. Exit 0: every part identical and every raw entry
/// preserved; 1: differences (or raw entries not preserved); 2: usage or unreadable input.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? json = null;
        bool quiet = false;
        var files = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--json" && i + 1 < args.Length) { json = args[++i]; }
            else if (args[i] == "--quiet") { quiet = true; }
            else { files.Add(args[i]); }
        }
        if (files.Count != 2)
        {
            Console.Error.WriteLine("usage: RoundTrip <before> <after> [--json file] [--quiet]");
            return 2;
        }
        try
        {
            Package a = Package.Load(files[0]);
            Package b = Package.Load(files[1]);
            var lines = new List<Dictionary<string, object?>>();
            Summary s = Comparer.Compare(a, b, lines);
            lines.Add(s.ToJson());
            if (json != null)
            {
                File.WriteAllLines(json, lines.Select(l => JsonSerializer.Serialize(l)));
            }
            if (!quiet)
            {
                foreach (var l in lines)
                {
                    Console.WriteLine(JsonSerializer.Serialize(l));
                }
            }
            Console.Error.WriteLine(s.Text());
            return s.Clean ? 0 : 1;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or XmlException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("RoundTrip: " + e.Message);
            return 2;
        }
    }
}

/// <summary>One stored part: its decoded bytes and, for zip packages, the raw entry as stored.</summary>
internal sealed class Part
{
    public required string Name;
    public required byte[] Data;
    public string? ContentType;
    public RawEntry? Raw;
}

/// <summary>A zip entry as it sits in the file: header fields and the still-compressed bytes.</summary>
internal sealed class RawEntry
{
    public ushort Method;
    public uint Crc;
    public long CompressedSize;
    public long Size;
    public byte[] Stored = [];
}

internal sealed class Package
{
    public Dictionary<string, Part> Parts = new(StringComparer.Ordinal);
    public bool Flat;

    public static Package Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return bytes.Length > 1 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K' ? LoadZip(bytes) : LoadFlat(bytes);
    }

    // ECMA-376 Part 2 §9 and APPNOTE: read the central directory ourselves so the stored (compressed) bytes can be compared.
    private static Package LoadZip(byte[] z)
    {
        var p = new Package();
        int eocd = -1;
        for (int i = z.Length - 22; i >= Math.Max(0, z.Length - 22 - 65535); i--)
        {
            if (BitConverter.ToUInt32(z, i) == 0x06054b50) { eocd = i; break; }
        }
        if (eocd < 0) { throw new InvalidDataException("no end-of-central-directory record"); }
        int count = BitConverter.ToUInt16(z, eocd + 10);
        long cd = BitConverter.ToUInt32(z, eocd + 16);
        for (int n = 0; n < count; n++)
        {
            if (BitConverter.ToUInt32(z, (int)cd) != 0x02014b50) { throw new InvalidDataException("bad central directory entry"); }
            ushort method = BitConverter.ToUInt16(z, (int)cd + 10);
            uint crc = BitConverter.ToUInt32(z, (int)cd + 16);
            long csize = BitConverter.ToUInt32(z, (int)cd + 20);
            long size = BitConverter.ToUInt32(z, (int)cd + 24);
            int nl = BitConverter.ToUInt16(z, (int)cd + 28), el = BitConverter.ToUInt16(z, (int)cd + 30), cl = BitConverter.ToUInt16(z, (int)cd + 32);
            long lho = BitConverter.ToUInt32(z, (int)cd + 42);
            string name = Encoding.UTF8.GetString(z, (int)cd + 46, nl);
            cd += 46 + nl + el + cl;
            if (name.EndsWith('/')) { continue; }
            int lnl = BitConverter.ToUInt16(z, (int)lho + 26), lel = BitConverter.ToUInt16(z, (int)lho + 28);
            long start = lho + 30 + lnl + lel;
            if (start + csize > z.Length) { throw new InvalidDataException("entry " + name + " runs past end of file"); }
            byte[] stored = z.AsSpan((int)start, (int)csize).ToArray();
            byte[] data = stored;
            if (method == 8)
            {
                using var ds = new DeflateStream(new MemoryStream(stored), CompressionMode.Decompress);
                using var ms = new MemoryStream();
                ds.CopyTo(ms);
                data = ms.ToArray();
            }
            else if (method != 0) { throw new InvalidDataException("entry " + name + ": unsupported method " + method); }
            p.Parts["/" + name] = new Part { Name = "/" + name, Data = data, Raw = new RawEntry { Method = method, Crc = crc, CompressedSize = csize, Size = size, Stored = stored } };
        }
        if (p.Parts.TryGetValue("/[Content_Types].xml", out var ct))
        {
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in XDocument.Load(new MemoryStream(ct.Data)).Root!.Elements())
            {
                string? ctype = (string?)e.Attribute("ContentType");
                if (ctype == null) { continue; }
                if (e.Name.LocalName == "Default") { defaults[(string?)e.Attribute("Extension") ?? ""] = ctype; }
                else if (e.Name.LocalName == "Override") { overrides[(string?)e.Attribute("PartName") ?? ""] = ctype; }
            }
            foreach (var part in p.Parts.Values)
            {
                string ext = Path.GetExtension(part.Name).TrimStart('.');
                part.ContentType = overrides.TryGetValue(part.Name, out var o) ? o : defaults.GetValueOrDefault(ext);
            }
        }
        return p;
    }

    // ECMA-376 Part 2 Annex "Flat OPC": pkg:part elements with pkg:xmlData or pkg:binaryData.
    private static Package LoadFlat(byte[] b)
    {
        var p = new Package { Flat = true };
        XNamespace pkg = "http://schemas.microsoft.com/office/2006/xmlPackage";
        XDocument d = XDocument.Load(new MemoryStream(b), LoadOptions.PreserveWhitespace);
        foreach (var e in d.Root!.Elements(pkg + "part"))
        {
            string name = (string)e.Attribute(pkg + "name")!;
            byte[] data;
            var xml = e.Element(pkg + "xmlData");
            if (xml != null)
            {
                var ms = new MemoryStream();
                var first = xml.Elements().FirstOrDefault();
                if (first != null) { using var w = XmlWriter.Create(ms, new XmlWriterSettings { OmitXmlDeclaration = true, Encoding = new UTF8Encoding(false) }); first.WriteTo(w); }
                data = ms.ToArray();
            }
            else { data = Convert.FromBase64String(((string?)e.Element(pkg + "binaryData") ?? "").Trim()); }
            p.Parts[name] = new Part { Name = name, Data = data, ContentType = (string?)e.Attribute(pkg + "contentType") };
        }
        return p;
    }
}

internal sealed class Summary
{
    public int Identical, Changed, Added, Removed, RawPreserved, RawNotPreserved, ContentTypeDiffs, RelationshipDiffs;
    public bool Clean => Changed == 0 && Added == 0 && Removed == 0 && ContentTypeDiffs == 0 && RelationshipDiffs == 0 && RawNotPreserved == 0;

    public Dictionary<string, object?> ToJson() => new()
    {
        ["kind"] = "summary", ["identical"] = Identical, ["changed"] = Changed, ["added"] = Added, ["removed"] = Removed,
        ["rawPreserved"] = RawPreserved, ["rawNotPreserved"] = RawNotPreserved,
        ["contentTypeDiffs"] = ContentTypeDiffs, ["relationshipDiffs"] = RelationshipDiffs,
    };

    public string Text() =>
        $"parts: {Identical} identical, {Changed} changed, {Added} added, {Removed} removed; raw entries: {RawPreserved} preserved, {RawNotPreserved} not preserved; content-type diffs {ContentTypeDiffs}; relationship diffs {RelationshipDiffs}";
}

internal static class Comparer
{
    public static Summary Compare(Package a, Package b, List<Dictionary<string, object?>> o)
    {
        var s = new Summary();
        foreach (var name in a.Parts.Keys.Union(b.Parts.Keys).OrderBy(n => n, StringComparer.Ordinal))
        {
            a.Parts.TryGetValue(name, out var pa);
            b.Parts.TryGetValue(name, out var pb);
            if (pa == null) { s.Added++; o.Add(new() { ["kind"] = "part", ["uri"] = name, ["status"] = "added", ["size"] = pb!.Data.Length }); continue; }
            if (pb == null) { s.Removed++; o.Add(new() { ["kind"] = "part", ["uri"] = name, ["status"] = "removed", ["size"] = pa.Data.Length }); continue; }
            bool same = pa.Data.AsSpan().SequenceEqual(pb.Data);
            var row = new Dictionary<string, object?>
            {
                ["kind"] = "part", ["uri"] = name, ["status"] = same ? "identical" : "changed",
                ["sha256Before"] = Hex(pa.Data), ["sha256After"] = Hex(pb.Data),
            };
            if (same) { s.Identical++; } else { s.Changed++; Describe(pa, pb, row); }
            if (pa.Raw != null && pb.Raw != null)
            {
                // "Preserved" means the stored entry is the same bytes with the same method, CRC and sizes: not recompressed.
                bool raw = pa.Raw.Method == pb.Raw.Method && pa.Raw.Crc == pb.Raw.Crc && pa.Raw.Stored.AsSpan().SequenceEqual(pb.Raw.Stored);
                row["rawPreserved"] = raw;
                if (raw) { s.RawPreserved++; } else if (same) { s.RawNotPreserved++; }
            }
            if (pa.ContentType != pb.ContentType)
            {
                s.ContentTypeDiffs++;
                row["contentType"] = new[] { pa.ContentType, pb.ContentType };
            }
            if (!same && name.EndsWith(".rels", StringComparison.Ordinal))
            {
                var rd = RelDiff(pa.Data, pb.Data);
                if (rd.Count > 0) { s.RelationshipDiffs++; row["relationships"] = rd; }
            }
            o.Add(row);
        }
        return s;
    }

    private static string Hex(byte[] d) => Convert.ToHexString(SHA256.HashData(d)).ToLowerInvariant();

    private static void Describe(Part a, Part b, Dictionary<string, object?> row)
    {
        int n = Math.Min(a.Data.Length, b.Data.Length), i = 0;
        while (i < n && a.Data[i] == b.Data[i]) { i++; }
        row["firstDiffOffset"] = i;
        row["sizeBefore"] = a.Data.Length;
        row["sizeAfter"] = b.Data.Length;
        row["before"] = Excerpt(a.Data, i);
        row["after"] = Excerpt(b.Data, i);
        try
        {
            // XML-aware: same infoset with different bytes (attribute quoting, whitespace between tags, prefixes kept) is flagged.
            var xa = XDocument.Load(new MemoryStream(a.Data));
            var xb = XDocument.Load(new MemoryStream(b.Data));
            bool eq = XNode.DeepEquals(xa.Root!, xb.Root!);
            row["xmlEquivalent"] = eq;
            if (!eq) { row["firstDifferingElement"] = FirstDiff(xa.Root!, xb.Root!, "/" + xa.Root!.Name.LocalName); }
        }
        catch (XmlException) { row["xml"] = false; }
    }

    private static string Excerpt(byte[] d, int at)
    {
        int s = Math.Max(0, at - 30), e = Math.Min(d.Length, at + 50);
        return Encoding.UTF8.GetString(d, s, e - s).Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static string FirstDiff(XElement a, XElement b, string path)
    {
        if (a.Name != b.Name) { return path + ": element " + a.Name.LocalName + " vs " + b.Name.LocalName; }
        string aa = string.Join(" ", a.Attributes().Select(x => x.Name.LocalName + "=" + x.Value));
        string ba = string.Join(" ", b.Attributes().Select(x => x.Name.LocalName + "=" + x.Value));
        if (aa != ba) { return path + ": attributes [" + aa + "] vs [" + ba + "]"; }
        var ca = a.Nodes().ToList();
        var cb = b.Nodes().ToList();
        for (int i = 0; i < Math.Min(ca.Count, cb.Count); i++)
        {
            if (XNode.DeepEquals(ca[i], cb[i])) { continue; }
            if (ca[i] is XElement ea && cb[i] is XElement eb) { return FirstDiff(ea, eb, path + "/" + ea.Name.LocalName + "[" + i + "]"); }
            return path + ": child " + i + " differs";
        }
        return ca.Count != cb.Count ? path + ": " + ca.Count + " vs " + cb.Count + " children" : path;
    }

    private static List<string> RelDiff(byte[] a, byte[] b)
    {
        static HashSet<string> Read(byte[] d)
        {
            var set = new HashSet<string>();
            try
            {
                foreach (var e in XDocument.Load(new MemoryStream(d)).Root!.Elements())
                {
                    set.Add($"{(string?)e.Attribute("Id")}|{(string?)e.Attribute("Type")}|{(string?)e.Attribute("Target")}|{(string?)e.Attribute("TargetMode") ?? "Internal"}");
                }
            }
            catch (XmlException) { set.Add("unreadable"); }
            return set;
        }
        var ra = Read(a);
        var rb = Read(b);
        return ra.Except(rb).Select(x => "-" + x).Concat(rb.Except(ra).Select(x => "+" + x)).ToList();
    }
}
