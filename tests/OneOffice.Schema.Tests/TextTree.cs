using System.Text;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Schema.Tests;

/// <summary>
/// A small in-memory markup tree used to exercise the typed views until the real lazy tree is wired in. It keeps every piece of
/// the source text (tags, attribute spelling, whitespace, comments) and rewrites only what an edit touches, so a test can show
/// that editing through a view changes one region and nothing else. It is a test fixture: it accepts well-formed documents only.
/// </summary>
public sealed class TextTree : IMarkupTree
{
    private sealed class Attr
    {
        public string Lead = " ";
        public string Prefix = "";
        public string Local = "";
        public string Ns = "";
        public string Quote = "\"";
        public string Raw = "";
        public bool IsDeclaration;
        public byte[] LocalU8 = [];
        public byte[] NsU8 = [];
        public byte[] RawU8 = [];
    }

    private sealed class Node
    {
        public bool IsElement;
        public string Text = "";
        public string Prefix = "";
        public string Local = "";
        public byte[] LocalU8 = [];
        public byte[] NsU8 = [];
        public List<Attr> Attrs = [];
        public List<int> Kids = [];
        public int Parent;
        public bool SelfClosing;
        public string TagTail = "";
        public string EndTag = "";
    }

    private readonly List<Node> _nodes = [new Node()]; // handle 0 is unused
    private readonly List<int> _top = []; // document-level pieces (prolog, root element, trailing whitespace)

    /// <summary>The handle of the root element.</summary>
    public int Root { get; private set; }

    /// <summary>Parses a well-formed document.</summary>
    public static TextTree Parse(string xml)
    {
        var t = new TextTree();
        var i = 0;
        var scopes = new List<Dictionary<string, string>> { new() { ["xml"] = "http://www.w3.org/XML/1998/namespace" } };
        t.ParseContent(xml, ref i, 0, scopes, true);
        return t;
    }

    private int NewNode(Node n)
    {
        _nodes.Add(n);
        return _nodes.Count - 1;
    }

    private void ParseContent(string s, ref int i, int parent, List<Dictionary<string, string>> scopes, bool top)
    {
        var kids = top ? _top : _nodes[parent].Kids;
        while (i < s.Length)
        {
            if (s[i] != '<')
            {
                var j = s.IndexOf('<', i);
                if (j < 0) j = s.Length;
                kids.Add(NewNode(new Node { Text = s[i..j], Parent = parent }));
                i = j;
                continue;
            }
            if (string.CompareOrdinal(s, i, "</", 0, 2) == 0) return;
            if (string.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
            {
                var j = s.IndexOf("-->", i, StringComparison.Ordinal) + 3;
                kids.Add(NewNode(new Node { Text = s[i..j], Parent = parent }));
                i = j;
                continue;
            }
            if (s[i + 1] is '?' or '!')
            {
                var j = s.IndexOf('>', i) + 1;
                kids.Add(NewNode(new Node { Text = s[i..j], Parent = parent }));
                i = j;
                continue;
            }
            var id = ParseElement(s, ref i, parent, scopes);
            kids.Add(id);
            if (top && Root == 0) Root = id;
        }
    }

    private int ParseElement(string s, ref int i, int parent, List<Dictionary<string, string>> scopes)
    {
        i++; // <
        var start = i;
        while (!char.IsWhiteSpace(s[i]) && s[i] != '>' && s[i] != '/') i++;
        var qname = s[start..i];
        var n = new Node { IsElement = true, Parent = parent };
        var colon = qname.IndexOf(':');
        n.Prefix = colon < 0 ? "" : qname[..colon];
        n.Local = colon < 0 ? qname : qname[(colon + 1)..];
        var scope = new Dictionary<string, string>();
        while (true)
        {
            var ws = i;
            while (char.IsWhiteSpace(s[i])) i++;
            if (s[i] is '>' or '/') { n.TagTail = s[ws..i]; break; }
            var a = new Attr { Lead = s[ws..i] };
            var ns0 = i;
            while (s[i] != '=') i++;
            var an = s[ns0..i].TrimEnd();
            i++;
            a.Quote = s[i].ToString();
            var close = s.IndexOf(a.Quote, i + 1, StringComparison.Ordinal);
            a.Raw = s[(i + 1)..close];
            i = close + 1;
            var c = an.IndexOf(':');
            a.Prefix = c < 0 ? "" : an[..c];
            a.Local = c < 0 ? an : an[(c + 1)..];
            if (an == "xmlns") { a.IsDeclaration = true; scope[""] = a.Raw; }
            else if (a.Prefix == "xmlns") { a.IsDeclaration = true; scope[a.Local] = a.Raw; }
            n.Attrs.Add(a);
        }
        scopes.Add(scope);
        n.NsU8 = Encoding.UTF8.GetBytes(Lookup(scopes, n.Prefix));
        n.LocalU8 = Encoding.UTF8.GetBytes(n.Local);
        foreach (var a in n.Attrs.Where(a => !a.IsDeclaration))
        {
            a.Ns = a.Prefix.Length == 0 ? "" : Lookup(scopes, a.Prefix);
            a.NsU8 = Encoding.UTF8.GetBytes(a.Ns);
            a.LocalU8 = Encoding.UTF8.GetBytes(a.Local);
            a.RawU8 = Encoding.UTF8.GetBytes(a.Raw);
        }
        var id = NewNode(n);
        if (s[i] == '/')
        {
            n.SelfClosing = true;
            i += 2; // />
        }
        else
        {
            i++; // >
            ParseContent(s, ref i, id, scopes, false);
            var end = s.IndexOf('>', i);
            n.EndTag = s[i..(end + 1)];
            i = end + 1;
        }
        scopes.RemoveAt(scopes.Count - 1);
        return id;
    }

    private static string Lookup(List<Dictionary<string, string>> scopes, string prefix)
    {
        for (var k = scopes.Count - 1; k >= 0; k--)
            if (scopes[k].TryGetValue(prefix, out var v)) return v;
        return "";
    }

    /// <summary>A reference to the root element.</summary>
    public MarkupRef RootRef => new(this, Root);

    /// <summary>Writes the document back, with the original text wherever nothing was edited.</summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        foreach (var k in _top) Write(sb, k);
        return sb.ToString();
    }

    private void Write(StringBuilder sb, int id)
    {
        var n = _nodes[id];
        if (!n.IsElement) { sb.Append(n.Text); return; }
        sb.Append('<');
        if (n.Prefix.Length > 0) sb.Append(n.Prefix).Append(':');
        sb.Append(n.Local);
        foreach (var a in n.Attrs)
        {
            sb.Append(a.Lead);
            if (a.IsDeclaration) { sb.Append(a.Prefix.Length > 0 ? "xmlns:" + a.Local : "xmlns"); }
            else { if (a.Prefix.Length > 0) sb.Append(a.Prefix).Append(':'); sb.Append(a.Local); }
            sb.Append('=').Append(a.Quote).Append(a.Raw).Append(a.Quote);
        }
        sb.Append(n.TagTail);
        if (n.SelfClosing) { sb.Append("/>"); return; }
        sb.Append('>');
        foreach (var k in n.Kids) Write(sb, k);
        sb.Append(n.EndTag);
    }

    // ---- IMarkupTree ----

    /// <inheritdoc/>
    public int FirstChildElement(int parent) => NextElement(_nodes[parent].Kids, -1);

    /// <inheritdoc/>
    public int NextSiblingElement(int element)
    {
        var kids = _nodes[_nodes[element].Parent].Kids;
        return NextElement(kids, kids.IndexOf(element));
    }

    private int NextElement(List<int> kids, int after)
    {
        for (var i = after + 1; i < kids.Count; i++)
            if (_nodes[kids[i]].IsElement) return kids[i];
        return 0;
    }

    /// <inheritdoc/>
    public ReadOnlySpan<byte> NamespaceUri(int element) => _nodes[element].NsU8;

    /// <inheritdoc/>
    public ReadOnlySpan<byte> LocalName(int element) => _nodes[element].LocalU8;

    private Attr? Find(int element, ReadOnlySpan<byte> ns, ReadOnlySpan<byte> local)
    {
        foreach (var a in _nodes[element].Attrs)
            if (!a.IsDeclaration && a.NsU8.AsSpan().SequenceEqual(ns) && a.LocalU8.AsSpan().SequenceEqual(local)) return a;
        return null;
    }

    /// <inheritdoc/>
    public bool TryGetAttribute(int element, ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName, out ReadOnlySpan<byte> rawValue)
    {
        var a = Find(element, namespaceUri, localName);
        rawValue = a is null ? default : a.RawU8;
        return a is not null;
    }

    private static string Escape(string v) => v.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private string PrefixFor(int element, string ns, string preferred, out bool declare)
    {
        declare = false;
        for (var n = element; n != 0; n = _nodes[n].Parent)
            foreach (var d in _nodes[n].Attrs.Where(d => d.IsDeclaration && d.Raw == ns && d.Prefix.Length > 0))
                return d.Local;
        declare = true;
        return preferred;
    }

    /// <inheritdoc/>
    public void SetAttribute(int element, ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName, ReadOnlySpan<byte> preferredPrefix, string value)
    {
        var existing = Find(element, namespaceUri, localName);
        if (existing is not null)
        {
            existing.Raw = Escape(value);
            existing.RawU8 = Encoding.UTF8.GetBytes(existing.Raw);
            return;
        }
        var ns = Encoding.UTF8.GetString(namespaceUri);
        var n = _nodes[element];
        var declare = false;
        var prefix = ns.Length == 0 ? "" : PrefixFor(element, ns, Encoding.UTF8.GetString(preferredPrefix), out declare);
        if (ns.Length > 0 && declare) n.Attrs.Add(new Attr { Prefix = "xmlns", Local = prefix, Raw = ns, IsDeclaration = true });
        var raw = Escape(value);
        n.Attrs.Add(new Attr
        {
            Prefix = prefix, Local = Encoding.UTF8.GetString(localName), Ns = ns, Raw = raw,
            LocalU8 = localName.ToArray(), NsU8 = namespaceUri.ToArray(), RawU8 = Encoding.UTF8.GetBytes(raw),
        });
    }

    /// <inheritdoc/>
    public bool RemoveAttribute(int element, ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName)
    {
        var a = Find(element, namespaceUri, localName);
        return a is not null && _nodes[element].Attrs.Remove(a);
    }

    /// <inheritdoc/>
    public int AttributeCount(int element) => _nodes[element].Attrs.Count(a => !a.IsDeclaration);

    /// <inheritdoc/>
    public void GetAttributeName(int element, int index, out ReadOnlySpan<byte> namespaceUri, out ReadOnlySpan<byte> localName)
    {
        var a = _nodes[element].Attrs.Where(a => !a.IsDeclaration).ElementAt(index);
        namespaceUri = a.NsU8;
        localName = a.LocalU8;
    }

    /// <inheritdoc/>
    public int InsertElement(int parent, int before, ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName, ReadOnlySpan<byte> preferredPrefix)
    {
        var ns = Encoding.UTF8.GetString(namespaceUri);
        var prefix = PrefixFor(parent, ns, Encoding.UTF8.GetString(preferredPrefix), out var declare);
        var n = new Node
        {
            IsElement = true, Parent = parent, Prefix = prefix, Local = Encoding.UTF8.GetString(localName),
            LocalU8 = localName.ToArray(), NsU8 = namespaceUri.ToArray(), SelfClosing = true,
        };
        if (declare) n.Attrs.Add(new Attr { Prefix = "xmlns", Local = prefix, Raw = ns, IsDeclaration = true });
        var id = NewNode(n);
        var p = _nodes[parent];
        if (p.SelfClosing)
        {
            p.SelfClosing = false;
            p.EndTag = "</" + (p.Prefix.Length > 0 ? p.Prefix + ":" : "") + p.Local + ">";
        }
        p.Kids.Insert(before == 0 ? p.Kids.Count : p.Kids.IndexOf(before), id);
        return id;
    }

    /// <inheritdoc/>
    public void RemoveElement(int element) => _nodes[_nodes[element].Parent].Kids.Remove(element);

    /// <inheritdoc/>
    public string GetText(int element)
        => string.Concat(_nodes[element].Kids.Select(k => _nodes[k]).Where(k => !k.IsElement && !k.Text.StartsWith("<!--", StringComparison.Ordinal)).Select(k => k.Text))
            .Replace("&lt;", "<", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal);

    /// <inheritdoc/>
    public void SetText(int element, string text)
    {
        var n = _nodes[element];
        n.Kids.RemoveAll(k => !_nodes[k].IsElement && !_nodes[k].Text.StartsWith("<!--", StringComparison.Ordinal));
        if (n.SelfClosing)
        {
            n.SelfClosing = false;
            n.EndTag = "</" + (n.Prefix.Length > 0 ? n.Prefix + ":" : "") + n.Local + ">";
        }
        n.Kids.Insert(0, NewNode(new Node { Text = Escape(text).Replace("\"", "&quot;", StringComparison.Ordinal), Parent = element }));
    }
}
