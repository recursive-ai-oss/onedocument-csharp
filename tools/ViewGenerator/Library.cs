using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RecursiveAI.OneDocument.Tools.ViewGenerator;

/// <summary>One line of views.manifest: a schema file and how its vocabulary is named in C#.</summary>
internal sealed record ManifestEntry(string Path, string EnumName, string Prefix, string Role)
{
    public bool Root => Role == "root";
    public bool Merge => Role == "merge";
}

/// <summary>A loaded XSD file: its target namespace and the defaults that decide how local names are qualified.</summary>
internal sealed class SchemaDoc
{
    public required ManifestEntry Entry { get; init; }
    public required string Ns { get; init; }
    public string? StrictNs { get; set; }
    public XElement Root { get; init; } = null!;
    public bool ElementsQualified { get; init; }
    public bool AttributesQualified { get; init; }
    public string EnumName => Entry.EnumName;
}

/// <summary>A reference to a named schema component: namespace URI plus local name.</summary>
internal readonly record struct QName(string Ns, string Local)
{
    public override string ToString() => "{" + Ns + "}" + Local;
}

/// <summary>The XSD files, indexed by what they declare. The generator never looks at schemaLocation; names resolve by namespace.</summary>
internal sealed class Library
{
    public const string XsNs = "http://www.w3.org/2001/XMLSchema";
    public const string XmlNs = "http://www.w3.org/XML/1998/namespace";
    public static readonly XNamespace Xs = XsNs;

    public List<SchemaDoc> Docs { get; } = new();
    public Dictionary<string, SchemaDoc> ByNs { get; } = new(StringComparer.Ordinal);
    public Dictionary<QName, XElement> SimpleTypes { get; } = new();
    public Dictionary<QName, XElement> ComplexTypes { get; } = new();
    public Dictionary<QName, XElement> Elements { get; } = new();
    public Dictionary<QName, XElement> Attributes { get; } = new();
    public Dictionary<QName, XElement> Groups { get; } = new();
    public Dictionary<QName, XElement> AttributeGroups { get; } = new();
    /// <summary>Strict-namespace simple types, keyed by the Transitional namespace they correspond to.</summary>
    public Dictionary<QName, XElement> StrictSimpleTypes { get; } = new();
    public Dictionary<QName, XElement> StrictComplexTypes { get; } = new();
    public Dictionary<string, string> StrictToTransitional { get; } = new(StringComparer.Ordinal);

    public static List<ManifestEntry> ReadManifest(string path)
    {
        var list = new List<ManifestEntry>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split('|');
            if (p.Length != 4) throw new InvalidDataException("views.manifest: bad line: " + line);
            if (p[3] is not ("root" or "support" or "merge")) throw new InvalidDataException("views.manifest: bad role: " + line);
            list.Add(new ManifestEntry(p[0], p[1], p[2], p[3]));
        }
        return list;
    }

    public void Load(string schemasDir, IEnumerable<ManifestEntry> entries)
    {
        foreach (var e in entries)
        {
            var doc = Parse(Path.Combine(schemasDir, e.Path));
            var ns = doc.Root!.Attribute("targetNamespace")?.Value ?? throw new InvalidDataException(e.Path + ": no targetNamespace");
            var sd = new SchemaDoc
            {
                Entry = e,
                Ns = ns,
                Root = doc.Root!,
                ElementsQualified = doc.Root!.Attribute("elementFormDefault")?.Value == "qualified",
                AttributesQualified = doc.Root!.Attribute("attributeFormDefault")?.Value == "qualified",
            };
            if (e.Merge)
            {
                // More declarations for a namespace another file already provides; the first file wins on a clash.
                if (!ByNs.ContainsKey(ns)) throw new InvalidDataException(e.Path + ": merge target namespace not loaded yet");
                Index(sd, true);
                continue;
            }
            Docs.Add(sd);
            ByNs[ns] = sd;
            Index(sd, false);
            if (e.Path.StartsWith("ecma376/transitional/", StringComparison.Ordinal))
            {
                var strictPath = Path.Combine(schemasDir, "ecma376/strict", Path.GetFileName(e.Path));
                if (File.Exists(strictPath)) LoadStrict(sd, strictPath);
            }
        }
    }

    private static XDocument Parse(string path)
    {
        // DTD processing stays prohibited: the pinned schemas have none.
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit };
        using var r = System.Xml.XmlReader.Create(path, settings);
        return XDocument.Load(r, LoadOptions.SetLineInfo);
    }

    private void Index(SchemaDoc sd, bool merging)
    {
        foreach (var c in sd.Root.Elements())
        {
            var name = c.Attribute("name")?.Value;
            if (name is null) continue;
            var q = new QName(sd.Ns, name);
            var dict = c.Name.LocalName switch
            {
                "simpleType" => SimpleTypes,
                "complexType" => ComplexTypes,
                "element" => Elements,
                "attribute" => Attributes,
                "group" => Groups,
                "attributeGroup" => AttributeGroups,
                _ => null,
            };
            if (dict is null) continue;
            if (!dict.TryAdd(q, c) && !merging) throw new InvalidDataException("duplicate declaration " + q + " in " + sd.Entry.Path);
        }
    }

    private void LoadStrict(SchemaDoc t, string strictPath)
    {
        var doc = Parse(strictPath);
        var sns = doc.Root!.Attribute("targetNamespace")!.Value;
        t.StrictNs = sns;
        StrictToTransitional[sns] = t.Ns;
        foreach (var c in doc.Root!.Elements())
        {
            var name = c.Attribute("name")?.Value;
            if (name is null) continue;
            if (c.Name.LocalName == "simpleType") StrictSimpleTypes[new QName(t.Ns, name)] = c;
            else if (c.Name.LocalName == "complexType") StrictComplexTypes[new QName(t.Ns, name)] = c;
        }
    }

    /// <summary>Resolves a prefixed name used in an attribute value, in the scope of <paramref name="ctx"/>.</summary>
    public static QName Resolve(XElement ctx, string qname)
    {
        var q = ResolveRaw(ctx, qname);
        // [MS-ODRAWXML] schemas name the main WordprocessingML namespace by its 2006 non-ECMA URI in a few references; documents use the main one.
        return q.Ns == "http://schemas.microsoft.com/office/word/2006/wordml" ? q with { Ns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main" } : q;
    }

    private static QName ResolveRaw(XElement ctx, string qname)
    {
        var i = qname.IndexOf(':');
        if (i < 0)
        {
            var d = ctx.GetDefaultNamespace().NamespaceName;
            return new QName(d, qname);
        }
        var prefix = qname[..i];
        var ns = prefix == "xml" ? XmlNs : ctx.GetNamespaceOfPrefix(prefix)?.NamespaceName
            ?? throw new InvalidDataException("unbound prefix in " + qname);
        return new QName(ns, qname[(i + 1)..]);
    }

    public SchemaDoc? DocFor(string ns) => ByNs.TryGetValue(ns, out var d) ? d : null;

    public static XElement? Child(XElement e, string local) => e.Element(Xs + local);
    public static IEnumerable<XElement> Children(XElement e, string local) => e.Elements(Xs + local);
}
