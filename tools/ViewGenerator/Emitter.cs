using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RecursiveAI.OneOffice.Tools.ViewGenerator;

/// <summary>Spellings of names in generated code.</summary>
internal static class Naming
{
    public static string Pascal(string s)
    {
        var sb = new StringBuilder();
        var up = true;
        foreach (var ch in s)
        {
            if (!char.IsLetterOrDigit(ch) || ch > 127) { up = true; continue; }
            sb.Append(up ? char.ToUpperInvariant(ch) : ch);
            up = false;
        }
        if (sb.Length == 0) return "Empty";
        if (char.IsDigit(sb[0])) sb.Insert(0, 'V');
        return sb.ToString();
    }

    public static string Plural(string s)
    {
        if (s.EndsWith('s') || s.EndsWith('x') || s.EndsWith("ch", StringComparison.Ordinal) || s.EndsWith("sh", StringComparison.Ordinal)) return s + "es";
        if (s.Length > 1 && s.EndsWith('y') && "aeiou".IndexOf(char.ToLowerInvariant(s[^2])) < 0) return s[..^1] + "ies";
        return s + "s";
    }

    public static string Esc(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    public static string Lit(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"u8";

    public static string Str(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>Writes the C# for the resolved schema model. Everything is sorted, so the output is a function of the schemas alone.</summary>
internal sealed class Emitter
{
    private const string Root = "global::RecursiveAI.OneOffice.Schema";
    private readonly Library _lib;
    private readonly Semantics _sem;
    private readonly Dictionary<string, string> _typeNames = new(); // "ns|CT" -> view name (override table)
    private readonly Dictionary<string, string> _memberNames = new(); // "enumName|local" -> member
    private readonly Dictionary<string, string> _enumNames = new(StringComparer.Ordinal);
    private readonly List<string> _order;

    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public Emitter(Library lib, Semantics sem, string namesManifest)
    {
        _lib = lib;
        _sem = sem;
        _order = lib.Docs.Select(d => d.EnumName).ToList();
        foreach (var raw in System.IO.File.ReadAllLines(namesManifest))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l[0] == '#') continue;
            var p = l.Split('|');
            if (p.Length != 4) throw new System.IO.InvalidDataException("names.manifest: bad line " + l);
            if (p[0] == "type") _typeNames[p[1] + "|" + p[2]] = p[3];
            else if (p[0] == "member") _memberNames[p[1] + "|" + p[2]] = p[3];
            else throw new System.IO.InvalidDataException("names.manifest: bad kind " + l);
        }
    }

    // ---- naming ----

    private void AssignNames()
    {
        // Complex types, per C# namespace, in a fixed order.
        var used = new Dictionary<string, HashSet<string>>();
        foreach (var t in _sem.TypeList.OrderBy(t => _order.IndexOf(t.Doc.EnumName)).ThenBy(t => t.Name.Local, StringComparer.Ordinal))
        {
            var set = used.TryGetValue(t.Doc.EnumName, out var s0) ? s0 : used[t.Doc.EnumName] = new HashSet<string>();
            string name;
            if (_typeNames.TryGetValue(t.Doc.EnumName + "|" + t.Name.Local, out var o)) name = o;
            else if (t.Anonymous) name = Naming.Pascal(t.Name.Local);
            else name = Naming.Pascal(t.Name.Local.StartsWith("CT_", StringComparison.Ordinal) ? t.Name.Local[3..] : t.Name.Local);
            var final = name;
            for (var i = 2; !set.Add(final); i++) final = name + "Type" + (i > 2 ? i.ToString(CultureInfo.InvariantCulture) : "");
            t.ViewName = final;
        }
        // Enums share the namespace with the views.
        foreach (var e in _sem.Enums.Values.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var doc = e.Doc!;
            var set = used.TryGetValue(doc.EnumName, out var s0) ? s0 : used[doc.EnumName] = new HashSet<string>();
            var disp = e.DisplayName ?? "Enum";
            var baseName = _typeNames.TryGetValue(doc.EnumName + "|" + disp, out var ov) ? ov : Naming.Pascal(disp.StartsWith("ST_", StringComparison.Ordinal) ? disp[3..] : disp) + "Value";
            var final = baseName;
            for (var i = 2; !set.Add(final); i++) final = baseName + i.ToString(CultureInfo.InvariantCulture);
            _enumNames[e.Key!] = final;
        }
        var id = 2;
        foreach (var t in _sem.TypeList.OrderBy(t => _order.IndexOf(t.Doc.EnumName)).ThenBy(t => t.ViewName, StringComparer.Ordinal))
            t.KindId = id++;
        var kinds = new HashSet<string>();
        foreach (var t in _sem.TypeList) if (!kinds.Add(t.CsKindName)) throw new InvalidOperationException("duplicate view kind " + t.CsKindName);
    }

    private string EnumType(SimpleInfo i) => Root + "." + i.Doc!.EnumName + "." + _enumNames[i.Key!];

    private static string TypeRef(TypeDef t) => Root + "." + t.Doc.EnumName + "." + t.ViewName;

    private string NsRef(string ns)
    {
        if (ns.Length == 0) return Root + ".SchemaNamespace.None";
        if (ns == Library.XmlNs) return Root + ".SchemaNamespace.Xml";
        var d = _lib.DocFor(ns);
        return d is null ? "" : Root + ".SchemaNamespace." + d.EnumName;
    }

    private string Prefix(string ns) => ns.Length == 0 ? "" : ns == Library.XmlNs ? "xml" : _lib.DocFor(ns)?.Entry.Prefix ?? "";

    // ---- entry point ----

    public void Emit()
    {
        AssignNames();
        EmitNamespaces();
        foreach (var doc in _lib.Docs)
        {
            var types = _sem.TypeList.Where(t => t.Doc == doc).OrderBy(t => t.ViewName, StringComparer.Ordinal).ToList();
            var enums = _sem.Enums.Values.Where(e => e.Doc == doc).OrderBy(e => _enumNames[e.Key!], StringComparer.Ordinal).ToList();
            if (types.Count == 0 && enums.Count == 0) continue;
            var sb = new StringBuilder();
            Header(sb, doc);
            sb.Append("namespace ").Append(Root[8..]).Append('.').Append(doc.EnumName).Append(";\n\n");
            foreach (var e in enums) EmitEnum(sb, e);
            foreach (var t in types) EmitType(sb, t);
            Files["Generated/" + doc.EnumName + ".g.cs"] = sb.ToString();
        }
        EmitFactory();
    }

    private static void Header(StringBuilder sb, SchemaDoc? doc)
    {
        sb.Append("// <auto-generated>\n");
        sb.Append("// Generated by tools/ViewGenerator from the schemas pinned under schemas/ (see schemas/views.manifest). Do not edit.\n");
        if (doc is not null)
        {
            sb.Append("// Source schema: ").Append(doc.Entry.Path).Append('\n');
            sb.Append(doc.Entry.Path.StartsWith("ms/", StringComparison.Ordinal)
                ? "// Schema text: Microsoft Open Specifications, used under their intellectual property notice (see THIRD-PARTY-NOTICES.md).\n"
                : "// Schema text: ECMA-376, 5th edition, copyright Ecma International, used under its copyright licence (see THIRD-PARTY-NOTICES.md).\n");
        }
        sb.Append("// </auto-generated>\n#nullable enable\n\n");
    }

    // ---- namespaces ----

    private void EmitNamespaces()
    {
        var sb = new StringBuilder();
        Header(sb, null);
        sb.Append("namespace RecursiveAI.OneOffice.Schema;\n\n");
        sb.Append("/// <summary>The XML namespaces the generated views know. Transitional and Strict spellings of one vocabulary share a member.</summary>\n");
        sb.Append("public enum SchemaNamespace\n{\n");
        sb.Append("    /// <summary>No namespace.</summary>\n    None = 0,\n");
        sb.Append("    /// <summary>A namespace no loaded schema declares.</summary>\n    Unknown = 1,\n");
        sb.Append("    /// <summary>The XML namespace of <c>xml:space</c> and <c>xml:lang</c>.</summary>\n    Xml = 2,\n");
        sb.Append("    /// <summary>Markup Compatibility and Extensibility (<c>mc:</c>).</summary>\n    MarkupCompatibility = 3,\n");
        var n = 4;
        foreach (var d in _lib.Docs)
            sb.Append("    /// <summary>").Append(Naming.Esc(d.Ns)).Append(" (").Append(d.Entry.Prefix).Append(":).</summary>\n    ")
              .Append(d.EnumName).Append(" = ").Append(n++.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        sb.Append("}\n\n");
        sb.Append("/// <summary>Namespace URIs and conventional prefixes for <see cref=\"SchemaNamespace\"/>.</summary>\n");
        sb.Append("public static class SchemaNamespaces\n{\n");
        sb.Append("    /// <summary>Maps a namespace URI (UTF-8) to its member; Strict URIs map to the same member as their Transitional twin.</summary>\n");
        sb.Append("    /// <param name=\"uri\">The namespace URI.</param>\n    /// <returns>The member, <see cref=\"SchemaNamespace.None\"/> for an empty URI, or <see cref=\"SchemaNamespace.Unknown\"/>.</returns>\n");
        sb.Append("    public static SchemaNamespace Resolve(ReadOnlySpan<byte> uri)\n    {\n        if (uri.Length == 0) return SchemaNamespace.None;\n");
        foreach (var d in _lib.Docs.OrderBy(d => d.Entry.Path.StartsWith("ecma376/transitional/wml", StringComparison.Ordinal) ? 0 : 1))
        {
            sb.Append("        if (uri.SequenceEqual(").Append(Naming.Lit(d.Ns)).Append(")) return SchemaNamespace.").Append(d.EnumName).Append(";\n");
            if (d.StrictNs is not null)
                sb.Append("        if (uri.SequenceEqual(").Append(Naming.Lit(d.StrictNs)).Append(")) return SchemaNamespace.").Append(d.EnumName).Append(";\n");
        }
        sb.Append("        if (uri.SequenceEqual(").Append(Naming.Lit(Library.XmlNs)).Append(")) return SchemaNamespace.Xml;\n");
        sb.Append("        if (uri.SequenceEqual(\"http://schemas.openxmlformats.org/markup-compatibility/2006\"u8)) return SchemaNamespace.MarkupCompatibility;\n");
        sb.Append("        return SchemaNamespace.Unknown;\n    }\n\n");
        sb.Append("    /// <summary>The URI of a namespace in Transitional or Strict spelling (vocabularies without a Strict form return the same URI).</summary>\n");
        sb.Append("    /// <param name=\"ns\">The namespace.</param>\n    /// <param name=\"strict\">True for the ISO/IEC 29500 Strict spelling.</param>\n    /// <returns>The URI as UTF-8; empty for <see cref=\"SchemaNamespace.None\"/> and <see cref=\"SchemaNamespace.Unknown\"/>.</returns>\n");
        sb.Append("    public static ReadOnlySpan<byte> Uri(SchemaNamespace ns, bool strict)\n    {\n        switch (ns)\n        {\n");
        sb.Append("            case SchemaNamespace.Xml: return ").Append(Naming.Lit(Library.XmlNs)).Append(";\n");
        sb.Append("            case SchemaNamespace.MarkupCompatibility: return \"http://schemas.openxmlformats.org/markup-compatibility/2006\"u8;\n");
        foreach (var d in _lib.Docs)
        {
            sb.Append("            case SchemaNamespace.").Append(d.EnumName).Append(": return ");
            if (d.StrictNs is not null) sb.Append("strict ? ").Append(Naming.Lit(d.StrictNs)).Append(" : ").Append(Naming.Lit(d.Ns)).Append(";\n");
            else sb.Append(Naming.Lit(d.Ns)).Append(";\n");
        }
        sb.Append("            default: return default;\n        }\n    }\n\n");
        sb.Append("    /// <summary>The conventional prefix used when a new element or attribute needs a namespace declaration.</summary>\n");
        sb.Append("    /// <param name=\"ns\">The namespace.</param>\n    /// <returns>The prefix as UTF-8; empty for <see cref=\"SchemaNamespace.None\"/>.</returns>\n");
        sb.Append("    public static ReadOnlySpan<byte> Prefix(SchemaNamespace ns)\n    {\n        switch (ns)\n        {\n");
        sb.Append("            case SchemaNamespace.Xml: return \"xml\"u8;\n            case SchemaNamespace.MarkupCompatibility: return \"mc\"u8;\n");
        foreach (var d in _lib.Docs) sb.Append("            case SchemaNamespace.").Append(d.EnumName).Append(": return ").Append(Naming.Lit(d.Entry.Prefix)).Append(";\n");
        sb.Append("            default: return default;\n        }\n    }\n\n");
        sb.Append("    /// <summary>True when the URI is in the ISO/IEC 29500 Strict family.</summary>\n    /// <param name=\"uri\">The namespace URI.</param>\n    /// <returns>True for the Strict spelling.</returns>\n");
        sb.Append("    public static bool IsStrict(ReadOnlySpan<byte> uri) => uri.StartsWith(\"http://purl.oclc.org/ooxml/\"u8);\n}\n");
        Files["Generated/SchemaNamespace.g.cs"] = sb.ToString();
    }

    // ---- enums ----

    private void EmitEnum(StringBuilder sb, SimpleInfo e)
    {
        var name = _enumNames[e.Key!];
        var used = new HashSet<string> { "Unrecognized" };
        var members = new List<(string Name, string Value)>();
        foreach (var v in e.Values)
        {
            var m = Naming.Pascal(v);
            var f = m;
            for (var i = 2; !used.Add(f); i++) f = m + i.ToString(CultureInfo.InvariantCulture);
            members.Add((f, v));
        }
        sb.Append("/// <summary>Values of the schema simple type <c>").Append(Naming.Esc(e.DisplayName ?? name)).Append("</c>. A value the schema does not list reads as <see cref=\"Unrecognized\"/>; the attribute's <c>Text</c> property still has it.</summary>\n");
        sb.Append("public enum ").Append(name).Append("\n{\n");
        sb.Append("    /// <summary>The attribute holds a value this enumeration does not list.</summary>\n    Unrecognized = 0,\n");
        foreach (var (m, v) in members)
            sb.Append("    /// <summary>The value <c>").Append(Naming.Esc(v)).Append("</c>.</summary>\n    ").Append(m).Append(",\n");
        sb.Append("}\n\n");
        sb.Append("/// <summary>Parsing and spelling of <see cref=\"").Append(name).Append("\"/>.</summary>\npublic static class ").Append(name).Append("Text\n{\n");
        sb.Append("    /// <summary>Parses the attribute text (UTF-8); unlisted text gives <see cref=\"").Append(name).Append(".Unrecognized\"/>.</summary>\n");
        sb.Append("    /// <param name=\"raw\">The attribute value.</param>\n    /// <returns>The member.</returns>\n");
        sb.Append("    public static ").Append(name).Append(" Parse(ReadOnlySpan<byte> raw)\n    {\n");
        foreach (var (m, v) in members)
            sb.Append("        if (raw.SequenceEqual(").Append(Naming.Lit(v)).Append(")) return ").Append(name).Append('.').Append(m).Append(";\n");
        sb.Append("        return ").Append(name).Append(".Unrecognized;\n    }\n\n");
        sb.Append("    /// <summary>The text written for a member.</summary>\n    /// <param name=\"value\">The member.</param>\n    /// <returns>The schema spelling.</returns>\n    /// <exception cref=\"ArgumentOutOfRangeException\">The member is <c>Unrecognized</c>, which has no spelling.</exception>\n");
        sb.Append("    public static string ToText(this ").Append(name).Append(" value) => value switch\n    {\n");
        foreach (var (m, v) in members)
            sb.Append("        ").Append(name).Append('.').Append(m).Append(" => ").Append(Naming.Str(v)).Append(",\n");
        sb.Append("        _ => throw new ArgumentOutOfRangeException(nameof(value), value, \"The value has no schema spelling; set the Text property instead.\"),\n    };\n}\n\n");
    }

    // ---- views ----

    private void EmitType(StringBuilder sb, TypeDef t)
    {
        var self = TypeRef(t);
        var used = new HashSet<string>
        {
            "Markup", "Text", "Content", "Children", "UnknownChildren", "Equals", "GetHashCode", "GetType", "ToString", "Create", "Kind", "Accepts",
            "ChildKind", "IsKnownAttribute", "MemberwiseClone", "Finalize", t.ViewName,
        };
        foreach (var a in t.Attrs)
        {
            var baseName = Naming.Pascal(a.Local);
            // Two attributes with one local name in different namespaces (w:id and r:id): the foreign one carries its prefix.
            if (a.Ns.Length > 0 && a.Ns != t.Doc.Ns && t.Attrs.Any(o => o != a && o.Local == a.Local && o.Ns != a.Ns))
                baseName = Naming.Pascal(Prefix(a.Ns)) + baseName;
            var m = baseName;
            if (used.Contains(m)) m += "Attribute";
            for (var i = 2; used.Contains(m); i++) m = baseName + "Attribute" + i.ToString(CultureInfo.InvariantCulture);
            used.Add(m);
            a.Member = m;
            if (a.Info.Kind is ValueKind.Enum or ValueKind.NumericUnion) used.Add(m + "Text");
        }
        var methodNames = new HashSet<string>();
        foreach (var s in t.Slots)
        {
            var single = _memberNames.TryGetValue((s.Ns.Length == 0 ? "" : _lib.DocFor(s.Ns)?.EnumName ?? "") + "|" + s.Local, out var o) ? o : Naming.Pascal(s.Local);
            var foreign = s.Ns.Length > 0 && s.Ns != t.Doc.Ns;
            var pfx = foreign ? Naming.Pascal(Prefix(s.Ns)) : "";
            var propBase = s.Many && s.Type is not null ? Naming.Plural(single) : single;
            // A name already taken (often by the same local name in another namespace, w:t and m:t) takes the namespace prefix, then a suffix.
            var m = propBase;
            if (used.Contains(m) || (s.Type is not null && methodNames.Contains(single))) { single = pfx + single; m = s.Many && s.Type is not null ? Naming.Plural(single) : single; }
            var m0 = m;
            for (var i = 2; used.Contains(m); i++) m = m0 + "Element" + (i > 2 ? i.ToString(CultureInfo.InvariantCulture) : "");
            var sg = single;
            for (var i = 2; methodNames.Contains(sg); i++) sg = single + i.ToString(CultureInfo.InvariantCulture);
            used.Add(m);
            methodNames.Add(sg);
            s.Member = m;
            s.MethodSuffix = sg;
        }

        sb.Append("/// <summary>View of the schema type <c>").Append(Naming.Esc(t.Name.Local)).Append("</c>");
        if (t.ElementNames.Count > 0)
            sb.Append(", the type of ").Append(string.Join(", ", t.ElementNames.OrderBy(x => x.Ns, StringComparer.Ordinal).ThenBy(x => x.Local, StringComparer.Ordinal)
                .Select(x => "<c>" + Prefix(x.Ns) + (x.Ns.Length > 0 ? ":" : "") + Naming.Esc(x.Local) + "</c>")));
        sb.Append(". A zero-copy wrapper over a markup element: reading parses on demand, writing edits the tree.</summary>\n");
        sb.Append("public readonly struct ").Append(t.ViewName).Append(" : ").Append(Root).Append(".IElementView<").Append(self).Append(">\n{\n");
        sb.Append("    private readonly ").Append(Root).Append(".MarkupRef _markup;\n\n");
        sb.Append("    /// <summary>Wraps an element; the element's name is not checked.</summary>\n    /// <param name=\"markup\">The element.</param>\n");
        sb.Append("    public ").Append(t.ViewName).Append('(').Append(Root).Append(".MarkupRef markup) { _markup = markup; }\n\n");
        sb.Append("    /// <inheritdoc/>\n    public ").Append(Root).Append(".MarkupRef Markup => _markup;\n\n");
        sb.Append("    /// <inheritdoc/>\n    public static ").Append(self).Append(" Create(").Append(Root).Append(".MarkupRef markup) => new(markup);\n\n");
        sb.Append("    /// <inheritdoc/>\n    public static ").Append(Root).Append(".ViewKind Kind => ").Append(Root).Append(".ViewKind.").Append(t.CsKindName).Append(";\n\n");
        EmitAccepts(sb, t);
        EmitChildKind(sb, t);
        EmitKnownAttributes(sb, t);

        foreach (var a in t.Attrs) EmitAttribute(sb, a);
        foreach (var s in t.Slots) EmitSlot(sb, t, s, self);
        if (t.HasText)
            sb.Append("    /// <summary>The element's character data (concatenated text nodes, entities decoded). Setting replaces the text and keeps child elements.</summary>\n    public string " + (t.ViewName == "Text" ? "Content" : "Text") + " { get => _markup.GetText(); set => _markup.SetText(value); }\n\n");
        sb.Append("    /// <summary>Every child element, declared or not, in document order.</summary>\n    public ").Append(Root).Append(".ChildElements Children => _markup.Children;\n\n");
        sb.Append("    /// <summary>The child elements this type's content model does not declare (extensions, alternate content, foreign markup), in document order.</summary>\n    public ")
          .Append(Root).Append(".UnknownChildren<").Append(self).Append("> UnknownChildren => new(_markup);\n");
        if (t.Base is not null)
        {
            sb.Append("\n    /// <summary>Views this element as its base type <c>").Append(Naming.Esc(t.Base.Name.Local)).Append("</c>.</summary>\n");
            sb.Append("    /// <param name=\"view\">The view.</param>\n    /// <returns>The same element as the base view.</returns>\n");
            sb.Append("    public static implicit operator ").Append(TypeRef(t.Base)).Append('(').Append(self).Append(" view) => new(view._markup);\n");
            sb.Append("\n    /// <summary>Views this element as its base type <c>").Append(Naming.Esc(t.Base.Name.Local)).Append("</c>.</summary>\n    /// <returns>The same element as the base view.</returns>\n");
            sb.Append("    public ").Append(TypeRef(t.Base)).Append(" ToBase() => new(_markup);\n");
        }
        sb.Append("}\n\n");
    }

    private static IEnumerable<IGrouping<string, T>> ByNs<T>(IEnumerable<T> items, Func<T, string> ns) =>
        items.GroupBy(ns).OrderBy(g => g.Key, StringComparer.Ordinal);

    private void EmitAccepts(StringBuilder sb, TypeDef t)
    {
        sb.Append("    /// <inheritdoc/>\n    public static bool Accepts(").Append(Root).Append(".SchemaNamespace ns, ReadOnlySpan<byte> local)\n    {\n");
        foreach (var g in ByNs(t.ElementNames, x => x.Ns))
        {
            var r = NsRef(g.Key);
            if (r.Length == 0) continue;
            sb.Append("        if (ns == ").Append(r).Append(") return ")
              .Append(string.Join(" || ", g.OrderBy(x => x.Local, StringComparer.Ordinal).Select(x => "local.SequenceEqual(" + Naming.Lit(x.Local) + ")"))).Append(";\n");
        }
        sb.Append("        return false;\n    }\n\n");
    }

    private void EmitChildKind(StringBuilder sb, TypeDef t)
    {
        sb.Append("    /// <inheritdoc/>\n    public static ").Append(Root).Append(".ViewKind ChildKind(").Append(Root)
          .Append(".SchemaNamespace ns, ReadOnlySpan<byte> local, out int rank)\n    {\n");
        foreach (var g in ByNs(t.Slots, s => s.Ns))
        {
            var r = NsRef(g.Key);
            if (r.Length == 0) continue;
            sb.Append("        if (ns == ").Append(r).Append(")\n        {\n");
            foreach (var s in g.OrderBy(s => s.Local, StringComparer.Ordinal))
            {
                var kind = s.Type is null ? "Simple" : s.Type.CsKindName;
                sb.Append("            if (local.SequenceEqual(").Append(Naming.Lit(s.Local)).Append(")) { rank = ").Append(s.Rank.ToString(CultureInfo.InvariantCulture))
                  .Append("; return ").Append(Root).Append(".ViewKind.").Append(kind).Append("; }\n");
            }
            sb.Append("        }\n");
        }
        sb.Append("        rank = -1;\n        return ").Append(Root).Append(".ViewKind.None;\n    }\n\n");
    }

    private void EmitKnownAttributes(StringBuilder sb, TypeDef t)
    {
        sb.Append("    /// <inheritdoc/>\n    public static bool IsKnownAttribute(").Append(Root).Append(".SchemaNamespace ns, ReadOnlySpan<byte> local)\n    {\n");
        foreach (var g in ByNs(t.Attrs, a => a.Ns))
        {
            var r = NsRef(g.Key);
            if (r.Length == 0) continue;
            sb.Append("        if (ns == ").Append(r).Append(") return ")
              .Append(string.Join(" || ", g.OrderBy(a => a.Local, StringComparer.Ordinal).Select(a => "local.SequenceEqual(" + Naming.Lit(a.Local) + ")"))).Append(";\n");
        }
        sb.Append("        return false;\n    }\n\n");
    }

    private void EmitAttribute(StringBuilder sb, AttrDef a)
    {
        var ns = NsRef(a.Ns);
        if (ns.Length == 0) return; // namespace of no loaded schema: left to UnknownAttributes
        var local = Naming.Lit(a.Local);
        var qn = (Prefix(a.Ns) is { Length: > 0 } p ? p + ":" : "") + a.Local;
        var doc = "Attribute <c>" + Naming.Esc(qn) + "</c> (" + Naming.Esc(a.TypeName) + (a.Required ? ", required" : "") +
                  (a.Default is not null ? ", default <c>" + Naming.Esc(a.Default) + "</c>" : "") + "). Null when absent; the default is not applied here. Setting null removes the attribute.";
        var m = a.Member;
        switch (a.Info.Kind)
        {
            case ValueKind.Bool:
                sb.Append("    /// <summary>").Append(doc).Append(" Reads true/false/1/0/on/off; other text reads as null.</summary>\n    public bool? ").Append(m)
                  .Append(" { get => _markup.GetBool(").Append(ns).Append(", ").Append(local).Append("); set => _markup.SetBool(").Append(ns).Append(", ").Append(local).Append(", value); }\n\n");
                break;
            case ValueKind.Int:
            case ValueKind.NumericUnion:
                sb.Append("    /// <summary>").Append(doc).Append(" Reads null when the text is not a whole number.</summary>\n    public long? ").Append(m)
                  .Append(" { get => _markup.GetInt64(").Append(ns).Append(", ").Append(local).Append("); set => _markup.SetInt64(").Append(ns).Append(", ").Append(local).Append(", value); }\n\n");
                if (a.Info.Kind == ValueKind.NumericUnion)
                    sb.Append("    /// <summary>The raw text of <c>").Append(Naming.Esc(qn)).Append("</c>, for values that are not whole numbers (such as measures with units).</summary>\n    public string? ").Append(m)
                      .Append("Text { get => _markup.GetString(").Append(ns).Append(", ").Append(local).Append("); set => _markup.SetString(").Append(ns).Append(", ").Append(local).Append(", value); }\n\n");
                break;
            case ValueKind.Double:
                sb.Append("    /// <summary>").Append(doc).Append(" Reads null when the text is not a number.</summary>\n    public double? ").Append(m)
                  .Append(" { get => _markup.GetDouble(").Append(ns).Append(", ").Append(local).Append("); set => _markup.SetDouble(").Append(ns).Append(", ").Append(local).Append(", value); }\n\n");
                break;
            case ValueKind.Enum:
            {
                var et = EnumType(a.Info);
                sb.Append("    /// <summary>").Append(doc).Append(" A value the schema does not list reads as <c>Unrecognized</c>; <see cref=\"").Append(m).Append("Text\"/> keeps it.</summary>\n    public ").Append(et).Append("? ").Append(m)
                  .Append("\n    {\n        get => _markup.TryGetRaw(").Append(ns).Append(", ").Append(local).Append(", out var raw) ? ").Append(et).Append("Text.Parse(raw) : null;\n        set { if (value is null) _markup.RemoveAttribute(")
                  .Append(ns).Append(", ").Append(local).Append("); else _markup.SetRaw(").Append(ns).Append(", ").Append(local).Append(", ").Append(et).Append("Text.ToText(value.Value)); }\n    }\n\n");
                sb.Append("    /// <summary>The raw text of <c>").Append(Naming.Esc(qn)).Append("</c>, whatever it is.</summary>\n    public string? ").Append(m)
                  .Append("Text { get => _markup.GetString(").Append(ns).Append(", ").Append(local).Append("); set => _markup.SetString(").Append(ns).Append(", ").Append(local).Append(", value); }\n\n");
                break;
            }
            default:
                sb.Append("    /// <summary>").Append(doc).Append("</summary>\n    public string? ").Append(m)
                  .Append(" { get => _markup.GetString(").Append(ns).Append(", ").Append(local).Append("); set => _markup.SetString(").Append(ns).Append(", ").Append(local).Append(", value); }\n\n");
                break;
        }
    }

    private void EmitSlot(StringBuilder sb, TypeDef t, ChildSlot s, string self)
    {
        var ns = NsRef(s.Ns);
        if (ns.Length == 0) return;
        var local = Naming.Lit(s.Local);
        var qn = (Prefix(s.Ns) is { Length: > 0 } p ? p + ":" : "") + s.Local;
        var m = s.Member;
        var singular = s.MethodSuffix;
        if (s.Type is not null)
        {
            var tr = TypeRef(s.Type);
            if (s.Many)
            {
                sb.Append("    /// <summary>The <c>").Append(Naming.Esc(qn)).Append("</c> children, in document order.</summary>\n    public ").Append(Root).Append(".ChildList<").Append(tr).Append("> ").Append(m)
                  .Append(" => new(_markup, ").Append(ns).Append(", ").Append(local).Append(");\n\n");
                sb.Append("    /// <summary>Adds a <c>").Append(Naming.Esc(qn)).Append("</c> child where the content model puts it (after the elements that must precede it).</summary>\n    /// <returns>The new, empty element.</returns>\n    public ")
                  .Append(tr).Append(" Add").Append(singular).Append("() => _markup.AddChild<").Append(tr).Append(", ").Append(self).Append(">(").Append(ns).Append(", ").Append(local).Append(");\n\n");
            }
            else
            {
                sb.Append("    /// <summary>The <c>").Append(Naming.Esc(qn)).Append("</c> child; null when absent.</summary>\n    public ").Append(tr).Append("? ").Append(m)
                  .Append(" => _markup.GetChild<").Append(tr).Append(">(").Append(ns).Append(", ").Append(local).Append(");\n\n");
                sb.Append("    /// <summary>Returns the <c>").Append(Naming.Esc(qn)).Append("</c> child, adding an empty one where the content model puts it when absent.</summary>\n    /// <returns>The child.</returns>\n    public ")
                  .Append(tr).Append(" GetOrAdd").Append(m).Append("() => _markup.GetOrAddChild<").Append(tr).Append(", ").Append(self).Append(">(").Append(ns).Append(", ").Append(local).Append(");\n\n");
            }
        }
        else
        {
            sb.Append("    /// <summary>The first <c>").Append(Naming.Esc(qn)).Append("</c> child as a raw element (its content is not modelled); null when absent.</summary>\n    public ").Append(Root).Append(".MarkupRef? ").Append(m)
              .Append(" => _markup.FindChild(").Append(ns).Append(", ").Append(local).Append(");\n\n");
            sb.Append("    /// <summary>Adds a <c>").Append(Naming.Esc(qn)).Append("</c> child where the content model puts it.</summary>\n    /// <returns>The new, empty element.</returns>\n    public ")
              .Append(Root).Append(".MarkupRef Add").Append(m).Append("() => _markup.AddChildElement<").Append(self).Append(">(").Append(ns).Append(", ").Append(local).Append(");\n\n");
        }
        sb.Append("    /// <summary>Removes every <c>").Append(Naming.Esc(qn)).Append("</c> child.</summary>\n    /// <returns>True when any was removed.</returns>\n    public bool Remove").Append(s.Type is not null && s.Many ? singular : m)
          .Append("() => _markup.RemoveChild(").Append(ns).Append(", ").Append(local).Append(");\n\n");
    }

    // ---- factory ----

    private void EmitFactory()
    {
        var sb = new StringBuilder();
        Header(sb, null);
        sb.Append("namespace RecursiveAI.OneOffice.Schema;\n\n");
        var types = _sem.TypeList.OrderBy(t => t.KindId).ToList();
        sb.Append("/// <summary>Identifies a generated view type.</summary>\npublic enum ViewKind\n{\n");
        sb.Append("    /// <summary>No view: the element is not declared in this position.</summary>\n    None = 0,\n");
        sb.Append("    /// <summary>The element is declared but has a simple type or no modelled content, so there is no view for it.</summary>\n    Simple = 1,\n");
        foreach (var t in types) sb.Append("    /// <summary>").Append(Naming.Esc(t.Doc.EnumName)).Append('.').Append(Naming.Esc(t.ViewName)).Append(".</summary>\n    ").Append(t.CsKindName).Append(" = ").Append(t.KindId.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        sb.Append("}\n\n");
        sb.Append("/// <summary>Maps element names to generated views with explicit switches: no reflection, nothing to trim.</summary>\npublic static class ViewFactory\n{\n");
        sb.Append("    /// <summary>The view for a global element declared by a root schema (the element a part starts with, or one that can appear anywhere).</summary>\n");
        sb.Append("    /// <param name=\"ns\">The element's namespace.</param>\n    /// <param name=\"local\">The element's local name, UTF-8.</param>\n    /// <returns>The kind, or <see cref=\"ViewKind.None\"/> when no root schema declares the element.</returns>\n");
        sb.Append("    public static ViewKind RootKind(SchemaNamespace ns, ReadOnlySpan<byte> local)\n    {\n");
        foreach (var g in _sem.RootElements.GroupBy(r => r.Ns).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var r = NsRef(g.Key);
            if (r.Length == 0) continue;
            sb.Append("        if (ns == ").Append(r.Replace(Root + ".", "", StringComparison.Ordinal)).Append(")\n        {\n");
            foreach (var e in g.OrderBy(e => e.Local, StringComparer.Ordinal))
                sb.Append("            if (local.SequenceEqual(").Append(Naming.Lit(e.Local)).Append(")) return ViewKind.").Append(e.Type is null ? "Simple" : e.Type.CsKindName).Append(";\n");
            sb.Append("        }\n");
        }
        sb.Append("        return ViewKind.None;\n    }\n\n");
        sb.Append("    /// <summary>Looks up a child in the content model of the view type <paramref name=\"parent\"/>.</summary>\n");
        sb.Append("    /// <param name=\"parent\">The kind of the parent view.</param>\n    /// <param name=\"ns\">The child's namespace.</param>\n    /// <param name=\"local\">The child's local name, UTF-8.</param>\n    /// <param name=\"rank\">The child's position in the content model, -1 when undeclared.</param>\n    /// <returns>The child's kind, <see cref=\"ViewKind.Simple\"/>, or <see cref=\"ViewKind.None\"/>.</returns>\n");
        sb.Append("    public static ViewKind ChildKind(ViewKind parent, SchemaNamespace ns, ReadOnlySpan<byte> local, out int rank)\n    {\n        switch (parent)\n        {\n");
        foreach (var t in types)
            if (t.Slots.Count > 0)
                sb.Append("            case ViewKind.").Append(t.CsKindName).Append(": return ").Append(TypeRef(t)).Append(".ChildKind(ns, local, out rank);\n");
        sb.Append("            default:\n                rank = -1;\n                return ViewKind.None;\n        }\n    }\n\n");
        sb.Append("    /// <summary>Hands the element to <paramref name=\"visitor\"/> as the view of the given kind, without boxing.</summary>\n");
        sb.Append("    /// <typeparam name=\"TVisitor\">The visitor type.</typeparam>\n    /// <param name=\"kind\">The view kind to wrap the element as.</param>\n    /// <param name=\"markup\">The element.</param>\n    /// <param name=\"visitor\">Receives the view; nothing is called for <see cref=\"ViewKind.None\"/> and <see cref=\"ViewKind.Simple\"/>.</param>\n");
        sb.Append("    public static void Dispatch<TVisitor>(ViewKind kind, MarkupRef markup, ref TVisitor visitor) where TVisitor : IViewVisitor\n    {\n        switch (kind)\n        {\n");
        foreach (var t in types)
            sb.Append("            case ViewKind.").Append(t.CsKindName).Append(": visitor.Visit(new ").Append(TypeRef(t)).Append("(markup)); break;\n");
        sb.Append("            default: break;\n        }\n    }\n}\n");
        Files["Generated/ViewFactory.g.cs"] = sb.ToString();
    }
}
