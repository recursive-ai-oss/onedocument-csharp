using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RecursiveAI.OneOffice.Tools.ViewGenerator;

internal enum ValueKind { String, Bool, Int, Double, Enum, NumericUnion }

/// <summary>What an attribute or text value looks like in C#, resolved from an XSD simple type.</summary>
internal sealed class SimpleInfo
{
    public required ValueKind Kind { get; init; }
    /// <summary>Identity of the enumeration, "namespace#name"; null unless Kind is Enum.</summary>
    public string? Key { get; init; }
    public List<string> Values { get; init; } = new();
    public string? DisplayName { get; init; }
    public SchemaDoc? Doc { get; init; }
}

internal sealed class AttrDef
{
    public required string Ns { get; init; }          // "" for no namespace
    public required string Local { get; init; }
    public required SimpleInfo Info { get; init; }
    public required string TypeName { get; init; }
    public bool Required { get; init; }
    public string? Default { get; init; }
    public string Member { get; set; } = "";
    public AttrDef Clone() => (AttrDef)MemberwiseClone();
}

internal sealed class ChildSlot
{
    public required string Ns { get; init; }
    public required string Local { get; init; }
    public TypeDef? Type { get; init; }
    /// <summary>True when the element has a simple type: it is reached as a raw element reference.</summary>
    public bool IsSimple { get; init; }
    public bool Many { get; set; }
    public int Rank { get; init; }
    public string Member { get; set; } = "";
    public string MethodSuffix { get; set; } = "";
    public ChildSlot Clone() => (ChildSlot)MemberwiseClone();
}

internal sealed class TypeDef
{
    public required QName Name { get; init; }
    public required SchemaDoc Doc { get; init; }
    public string ViewName { get; set; } = "";
    public bool Anonymous { get; init; }
    public TypeDef? Base { get; set; }
    public List<AttrDef> Attrs { get; } = new();
    public List<ChildSlot> Slots { get; } = new();
    public bool HasText { get; set; }
    public bool HasWildcard { get; set; }
    public bool HasWildcardAttributes { get; set; }
    public HashSet<(string Ns, string Local)> ElementNames { get; } = new();
    public int KindId { get; set; }
    public string CsKindName => Doc.EnumName + ViewName;
    public bool Built { get; set; }
}

/// <summary>Turns XSD components into the C# shapes the emitter writes.</summary>
internal sealed class Semantics
{
    private readonly Library _lib;
    private readonly Dictionary<QName, TypeDef> _types = new();
    private readonly Dictionary<string, SimpleInfo> _simple = new();
    private readonly Dictionary<QName, SimpleInfo> _simpleByQName = new();
    public List<string> Notes { get; } = new();
    public Dictionary<string, SimpleInfo> Enums { get; } = new(StringComparer.Ordinal);
    public List<TypeDef> TypeList { get; } = new();
    public List<(string Ns, string Local, TypeDef? Type)> RootElements { get; } = new();

    public Semantics(Library lib) { _lib = lib; }

    public SchemaDoc DocOf(XElement e)
    {
        var ns = e.Document!.Root!.Attribute("targetNamespace")!.Value;
        return _lib.ByNs[ns];
    }

    // ---- simple types ----

    public SimpleInfo Simple(QName q)
    {
        if (_simpleByQName.TryGetValue(q, out var hit)) return hit;
        SimpleInfo info;
        if (q.Ns == Library.XsNs)
        {
            info = new SimpleInfo
            {
                Kind = q.Local switch
                {
                    "boolean" => ValueKind.Bool,
                    "integer" or "int" or "long" or "short" or "byte" or "nonNegativeInteger" or "positiveInteger"
                        or "negativeInteger" or "nonPositiveInteger" or "unsignedInt" or "unsignedLong" or "unsignedShort"
                        or "unsignedByte" => ValueKind.Int,
                    "double" or "float" or "decimal" => ValueKind.Double,
                    _ => ValueKind.String,
                },
                DisplayName = "xsd:" + q.Local,
            };
        }
        else if (_lib.SimpleTypes.TryGetValue(q, out var st))
        {
            _simpleByQName[q] = new SimpleInfo { Kind = ValueKind.String }; // recursion guard
            info = FromSimpleType(st, q, DocOf(st));
        }
        else
        {
            Notes.Add("unresolved simple type " + q);
            info = new SimpleInfo { Kind = ValueKind.String, DisplayName = q.Local };
        }
        _simpleByQName[q] = info;
        return info;
    }

    private SimpleInfo FromSimpleType(XElement st, QName? name, SchemaDoc doc, string? synthKey = null)
    {
        var key = name is { } n ? n.Ns + "#" + n.Local : synthKey!;
        var display = name?.Local ?? synthKey;
        var restriction = Library.Child(st, "restriction");
        if (restriction is not null)
        {
            var values = Library.Children(restriction, "enumeration").Select(e => e.Attribute("value")!.Value).ToList();
            SimpleInfo baseInfo;
            var baseAttr = restriction.Attribute("base");
            if (baseAttr is not null) baseInfo = Simple(Library.Resolve(restriction, baseAttr.Value));
            else
            {
                var inline = Library.Child(restriction, "simpleType");
                baseInfo = inline is null ? new SimpleInfo { Kind = ValueKind.String } : FromSimpleType(inline, null, doc, key + ".base");
            }
            if (values.Count > 0 && baseInfo.Kind is ValueKind.String or ValueKind.Enum)
                return MakeEnum(key, display, values, doc, name);
            return baseInfo;
        }
        var union = Library.Child(st, "union");
        if (union is not null)
        {
            var members = new List<SimpleInfo>();
            var mt = union.Attribute("memberTypes")?.Value;
            if (mt is not null)
                foreach (var m in mt.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    members.Add(Simple(Library.Resolve(union, m)));
            foreach (var inl in Library.Children(union, "simpleType"))
                members.Add(FromSimpleType(inl, null, doc, key + ".member" + members.Count.ToString(CultureInfo.InvariantCulture)));
            if (members.Any(m => m.Kind == ValueKind.Bool)) return new SimpleInfo { Kind = ValueKind.Bool, DisplayName = display };
            if (members.Count > 0 && members.All(m => m.Kind == ValueKind.Enum))
                return MakeEnum(key, display, members.SelectMany(m => m.Values).Distinct().ToList(), doc, name);
            if (members.Count > 0 && members[0].Kind is ValueKind.Int or ValueKind.Double)
                return new SimpleInfo { Kind = ValueKind.NumericUnion, DisplayName = display };
            return new SimpleInfo { Kind = ValueKind.String, DisplayName = display };
        }
        return new SimpleInfo { Kind = ValueKind.String, DisplayName = display };
    }

    private SimpleInfo MakeEnum(string key, string? display, List<string> values, SchemaDoc doc, QName? name)
    {
        // Strict documents spell some values differently (start/end for left/right); the enumeration is the union so either file reads.
        if (name is { } n && _lib.StrictSimpleTypes.TryGetValue(n, out var strictType))
        {
            var sr = Library.Child(strictType, "restriction");
            if (sr is not null)
                foreach (var v in Library.Children(sr, "enumeration").Select(e => e.Attribute("value")!.Value))
                    if (!values.Contains(v)) values.Add(v);
        }
        if (Enums.TryGetValue(key, out var existing)) return existing;
        var info = new SimpleInfo { Kind = ValueKind.Enum, Key = key, Values = values, DisplayName = display, Doc = doc };
        Enums[key] = info;
        return info;
    }

    // ---- complex types ----

    public TypeDef BuildNamed(QName q)
    {
        if (_types.TryGetValue(q, out var t)) return t;
        var xml = _lib.ComplexTypes[q];
        t = new TypeDef { Name = q, Doc = DocOf(xml), ViewName = q.Local };
        _types[q] = t;
        TypeList.Add(t);
        Fill(t, xml);
        return t;
    }

    private TypeDef BuildAnonymous(XElement complexType, string owner, string elementLocal)
    {
        var doc = DocOf(complexType);
        var q = new QName(doc.Ns, owner + "." + elementLocal);
        if (_types.TryGetValue(q, out var existing)) return existing;
        var t = new TypeDef { Name = q, Doc = doc, ViewName = q.Local, Anonymous = true };
        _types[q] = t;
        TypeList.Add(t);
        Fill(t, complexType);
        return t;
    }

    private void Fill(TypeDef t, XElement ct)
    {
        t.Built = true;
        int next = 0;
        if (ct.Attribute("mixed")?.Value == "true") t.HasText = true;
        var cc = Library.Child(ct, "complexContent");
        var sc = Library.Child(ct, "simpleContent");
        XElement? particleHost = ct;
        XElement attrHost = ct;
        if (cc is not null)
        {
            if (cc.Attribute("mixed")?.Value == "true") t.HasText = true;
            var deriv = Library.Child(cc, "extension") ?? Library.Child(cc, "restriction")!;
            var baseAttr = deriv.Attribute("base")!.Value;
            var bq = Library.Resolve(deriv, baseAttr);
            if (_lib.ComplexTypes.ContainsKey(bq))
            {
                var b = BuildNamed(bq);
                t.Base = b;
                if (!b.Built) Notes.Add("recursive base " + bq);
                Inherit(t, b, ref next);
            }
            particleHost = deriv;
            attrHost = deriv;
        }
        else if (sc is not null)
        {
            var deriv = Library.Child(sc, "extension") ?? Library.Child(sc, "restriction")!;
            t.HasText = true;
            var bq = Library.Resolve(deriv, deriv.Attribute("base")!.Value);
            if (_lib.ComplexTypes.ContainsKey(bq))
            {
                var b = BuildNamed(bq);
                t.Base = b;
                Inherit(t, b, ref next);
            }
            particleHost = null;
            attrHost = deriv;
        }
        if (particleHost is not null)
        {
            foreach (var p in particleHost.Elements())
            {
                var ln = p.Name.LocalName;
                if (ln is "sequence" or "choice" or "all" or "group")
                    Walk(t, p, false, null, ref next, t.Name.Local.Contains("PrBase", StringComparison.Ordinal));
            }
        }
        CollectAttributes(t, attrHost);
    }

    private static void Inherit(TypeDef t, TypeDef b, ref int next)
    {
        foreach (var a in b.Attrs) t.Attrs.Add(a.Clone());
        foreach (var s in b.Slots) t.Slots.Add(s.Clone());
        if (b.HasText) t.HasText = true;
        if (b.HasWildcard) t.HasWildcard = true;
        if (b.HasWildcardAttributes) t.HasWildcardAttributes = true;
        next = b.Slots.Count == 0 ? 0 : b.Slots.Max(s => s.Rank) + 1;
    }

    private static int Max(XElement e)
    {
        var v = e.Attribute("maxOccurs")?.Value;
        if (v is null) return 1;
        return v == "unbounded" ? int.MaxValue : int.Parse(v, CultureInfo.InvariantCulture);
    }

    private void Walk(TypeDef t, XElement p, bool many, int? fixedRank, ref int next, bool bag)
    {
        switch (p.Name.LocalName)
        {
            case "sequence":
                if (fixedRank is null && (many || Max(p) > 1)) fixedRank = next++;
                many |= Max(p) > 1;
                foreach (var c in p.Elements()) Walk(t, c, many, fixedRank, ref next, bag);
                break;
            case "choice":
            case "all":
                fixedRank ??= next++;
                many |= Max(p) > 1;
                foreach (var c in p.Elements()) Walk(t, c, many, fixedRank, ref next, bag);
                break;
            case "group":
            {
                var gq = Library.Resolve(p, p.Attribute("ref")!.Value);
                if (!_lib.Groups.TryGetValue(gq, out var g)) { Notes.Add("unresolved group " + gq); break; }
                many |= Max(p) > 1;
                foreach (var c in g.Elements())
                    if (c.Name.LocalName is "sequence" or "choice" or "all") Walk(t, c, many, fixedRank, ref next, bag || gq.Local.Contains("PrBase", StringComparison.Ordinal));
                break;
            }
            case "any":
                t.HasWildcard = true;
                break;
            case "element":
                // A property bag (rPr, trPr) is an unbounded choice in the schema, but each property occurs once.
                AddSlot(t, p, bag ? Max(p) > 1 : many || Max(p) > 1, fixedRank ?? next++);
                break;
        }
    }

    private void AddSlot(TypeDef t, XElement e, bool many, int rank)
    {
        string ns, local;
        TypeDef? type = null;
        bool simple = false;
        var refAttr = e.Attribute("ref");
        XElement decl = e;
        if (refAttr is not null)
        {
            var rq = Library.Resolve(e, refAttr.Value);
            ns = rq.Ns; local = rq.Local;
            if (!_lib.Elements.TryGetValue(rq, out var g)) { Notes.Add("unresolved element ref " + rq + " in " + t.Name); return; }
            decl = g;
        }
        else
        {
            local = e.Attribute("name")!.Value;
            var doc = DocOf(e);
            var qualified = e.Attribute("form") is { } f ? f.Value == "qualified" : doc.ElementsQualified;
            ns = qualified ? doc.Ns : "";
        }
        var typeAttr = decl.Attribute("type");
        if (typeAttr is not null)
        {
            var tq = Library.Resolve(decl, typeAttr.Value);
            if (_lib.ComplexTypes.ContainsKey(tq)) type = BuildNamed(tq);
            else if (tq.Ns == Library.XsNs && tq.Local == "anyType") { }
            else simple = true;
        }
        else if (Library.Child(decl, "complexType") is { } inlineCt) type = BuildAnonymous(inlineCt, t.Name.Local.Replace('.', '_'), local);
        else if (Library.Child(decl, "simpleType") is not null) simple = true;
        type?.ElementNames.Add((ns, local));
        var existing = t.Slots.FirstOrDefault(s => s.Ns == ns && s.Local == local);
        if (existing is not null)
        {
            if (many) existing.Many = true;
            if (existing.Type != type && !(existing.IsSimple && simple)) Notes.Add("element " + local + " has two types in " + t.Name);
            return;
        }
        t.Slots.Add(new ChildSlot { Ns = ns, Local = local, Type = type, IsSimple = simple, Many = many, Rank = rank });
    }

    private void CollectAttributes(TypeDef t, XElement host)
    {
        foreach (var c in host.Elements())
        {
            switch (c.Name.LocalName)
            {
                case "attribute": AddAttr(t, c); break;
                case "attributeGroup": AddAttrGroup(t, c, 0); break;
                case "anyAttribute": t.HasWildcardAttributes = true; break;
            }
        }
    }

    private void AddAttrGroup(TypeDef t, XElement refEl, int depth)
    {
        var gq = Library.Resolve(refEl, refEl.Attribute("ref")!.Value);
        if (depth > 16 || !_lib.AttributeGroups.TryGetValue(gq, out var g)) { Notes.Add("unresolved attributeGroup " + gq); return; }
        CollectAttributes2(t, g, depth + 1);
    }

    private void CollectAttributes2(TypeDef t, XElement host, int depth)
    {
        foreach (var c in host.Elements())
        {
            switch (c.Name.LocalName)
            {
                case "attribute": AddAttr(t, c); break;
                case "attributeGroup": AddAttrGroup(t, c, depth); break;
                case "anyAttribute": t.HasWildcardAttributes = true; break;
            }
        }
    }

    private void AddAttr(TypeDef t, XElement a)
    {
        if (a.Attribute("use")?.Value == "prohibited") return;
        string ns, local;
        XElement decl = a;
        var refAttr = a.Attribute("ref");
        if (refAttr is not null)
        {
            var rq = Library.Resolve(a, refAttr.Value);
            ns = rq.Ns; local = rq.Local;
            if (_lib.Attributes.TryGetValue(rq, out var g)) decl = g;
            else if (rq.Ns == Library.XmlNs)
            {
                AddAttrDef(t, ns, local, new SimpleInfo { Kind = ValueKind.String, DisplayName = "xsd:string" }, "xsd:string", a);
                return;
            }
            else { Notes.Add("unresolved attribute ref " + rq + " in " + t.Name); return; }
        }
        else
        {
            local = a.Attribute("name")!.Value;
            var doc = DocOf(a);
            var qualified = a.Attribute("form") is { } f ? f.Value == "qualified" : doc.AttributesQualified;
            ns = qualified ? doc.Ns : "";
        }
        SimpleInfo info;
        string typeName;
        var ta = decl.Attribute("type");
        if (ta is not null)
        {
            var tq = Library.Resolve(decl, ta.Value);
            info = Simple(tq);
            typeName = (tq.Ns == Library.XsNs ? "xsd:" : _lib.DocFor(tq.Ns)?.Entry.Prefix + ":") + tq.Local;
        }
        else if (Library.Child(decl, "simpleType") is { } inl)
        {
            var key = t.Name.Ns + "#" + t.Name.Local + "@" + local;
            info = FromSimpleType(inl, null, DocOf(a), key);
            typeName = "inline";
        }
        else
        {
            info = new SimpleInfo { Kind = ValueKind.String };
            typeName = "xsd:anySimpleType";
        }
        AddAttrDef(t, ns, local, info, typeName, a);
    }

    private static void AddAttrDef(TypeDef t, string ns, string local, SimpleInfo info, string typeName, XElement use)
    {
        if (t.Attrs.Any(x => x.Ns == ns && x.Local == local)) return;
        t.Attrs.Add(new AttrDef
        {
            Ns = ns,
            Local = local,
            Info = info,
            TypeName = typeName,
            Required = use.Attribute("use")?.Value == "required",
            Default = use.Attribute("default")?.Value ?? use.Attribute("fixed")?.Value,
        });
    }

    /// <summary>Builds everything the root schemas declare, and with it every type they reach.</summary>
    public void BuildRoots()
    {
        foreach (var doc in _lib.Docs.Where(d => d.Entry.Root))
        {
            foreach (var q in _lib.ComplexTypes.Keys.Where(k => k.Ns == doc.Ns).OrderBy(k => k.Local, StringComparer.Ordinal))
                BuildNamed(q);
            foreach (var q in _lib.Elements.Keys.Where(k => k.Ns == doc.Ns).OrderBy(k => k.Local, StringComparer.Ordinal))
            {
                var e = _lib.Elements[q];
                TypeDef? type = null;
                var ta = e.Attribute("type");
                if (ta is not null)
                {
                    var tq = Library.Resolve(e, ta.Value);
                    if (_lib.ComplexTypes.ContainsKey(tq)) type = BuildNamed(tq);
                }
                else if (Library.Child(e, "complexType") is { } ict) type = BuildAnonymous(ict, "Root", q.Local);
                type?.ElementNames.Add((q.Ns, q.Local));
                RootElements.Add((q.Ns, q.Local, type));
            }
            // simple types of root schemas: make their enumerations available even when no attribute reaches them
            foreach (var q in _lib.SimpleTypes.Keys.Where(k => k.Ns == doc.Ns).OrderBy(k => k.Local, StringComparer.Ordinal))
                Simple(q);
        }
    }
}
