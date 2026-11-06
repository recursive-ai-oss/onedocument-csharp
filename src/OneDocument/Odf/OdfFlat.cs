using System.Buffers;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.Odf;

// Zip package -> flat XML document (OpenDocument 1.3 Part 1, the office:document root). The parts keep their elements; the
// namespace declarations of the four parts are united on the root, and pictures stored in the package are inlined as base64 in
// office:binary-data, which is how a flat file carries them.
internal static class OdfFlat
{
    public static void Write(OdfPackage package, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new XmlWriter(buffer);
        MarkupNode content = package.ContentRoot;
        MarkupNode styles = package.StylesRoot;
        MarkupNode meta = package.MetaRoot;
        MarkupNode settings = package.SettingsRoot;

        w.WriteXmlDeclaration(standalone: null);
        w.WriteStartElement("office:document");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        bool officeDeclared = false;
        foreach (MarkupNode root in new[] { content, styles, meta, settings })
        {
            if (root.IsNull)
            {
                continue;
            }

            foreach (MarkupAttributeRef a in root.Attributes)
            {
                if (a.IsNamespaceDeclaration && declared.Add(Encoding.UTF8.GetString(a.RawName)))
                {
                    w.WriteAttributeRaw(a.RawName, a.RawValue, a.Quote);
                    officeDeclared |= a.RawName.SequenceEqual("xmlns:office"u8);
                }
            }
        }

        if (!officeDeclared)
        {
            w.WriteNamespaceDeclaration("office", OdfNamespaces.Office);
        }

        w.WriteAttribute("office:version", package.Version);
        w.WriteAttribute("office:mimetype", package.MimeType);

        CopyChild(w, meta, "meta");
        CopyChild(w, settings, "settings");
        CopyChild(w, content, "scripts");
        MergeInto(w, "font-face-decls", [OdfMarkup.Child(styles, OdfNamespaces.Office, "font-face-decls"), OdfMarkup.Child(content, OdfNamespaces.Office, "font-face-decls")]);
        CopyChild(w, styles, "styles");
        MergeInto(w, "automatic-styles", [OdfMarkup.Child(styles, OdfNamespaces.Office, "automatic-styles"), OdfMarkup.Child(content, OdfNamespaces.Office, "automatic-styles")]);
        CopyChild(w, styles, "master-styles");
        MarkupNode body = package.Body;
        if (!body.IsNull)
        {
            CopyNode(w, body, package, 0, cancellationToken);
        }

        w.WriteEndElement();
        w.Complete();
        destination.Write(buffer.WrittenSpan);
    }

    private static void CopyChild(XmlWriter w, MarkupNode parent, string local)
    {
        MarkupNode c = OdfMarkup.Child(parent, OdfNamespaces.Office, local);
        if (!c.IsNull)
        {
            c.WriteTo(WriterOf(w));
        }
    }

    // Writes the union of several containers of the same name: the first has priority and later ones add the styles it lacks.
    private static void MergeInto(XmlWriter w, string local, MarkupNode[] containers)
    {
        bool any = false;
        foreach (MarkupNode c in containers)
        {
            any |= !c.IsNull;
        }

        if (!any)
        {
            return;
        }

        w.WriteStartElement("office:" + local);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkupNode c in containers)
        {
            if (c.IsNull)
            {
                continue;
            }

            foreach (MarkupNode e in c.ChildElements)
            {
                string? name = OdfMarkup.Attribute(e, OdfNamespaces.Style, "name");
                string? family = OdfMarkup.Attribute(e, OdfNamespaces.Style, "family");
                string key = e.NamespaceUri + "|" + e.LocalNameString + "|" + family + "|" + name;
                if (name is null || seen.Add(key))
                {
                    CopyNode(w, e, null, 0, default);
                }
            }
        }

        w.WriteEndElement();
    }

    // MarkupNode.WriteTo needs an IBufferWriter; the XmlWriter wraps one, so a verbatim copy goes through a small adapter.
    private static RawAdapter WriterOf(XmlWriter w) => new RawAdapter(w);

    private sealed class RawAdapter(XmlWriter w) : IBufferWriter<byte>
    {
        private byte[] _buf = new byte[4096];

        public void Advance(int count)
        {
            w.WriteRaw(_buf.AsSpan(0, count));
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_buf.Length < sizeHint)
            {
                _buf = new byte[sizeHint];
            }

            return _buf;
        }

        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
    }

    private static void CopyNode(XmlWriter w, MarkupNode n, OdfPackage? package, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        switch (n.Kind)
        {
            case MarkupNodeKind.Element:
                CopyElement(w, n, package, depth, ct);
                break;
            case MarkupNodeKind.Text:
            case MarkupNodeKind.Whitespace:
                w.WriteRaw(n.RawContent);
                break;
            case MarkupNodeKind.CData:
                w.WriteCData(Encoding.UTF8.GetString(n.RawContent));
                break;
            case MarkupNodeKind.Comment:
                w.WriteComment(Encoding.UTF8.GetString(n.RawContent));
                break;
        }
    }

    private static void CopyElement(XmlWriter w, MarkupNode n, OdfPackage? package, int depth, CancellationToken ct)
    {
        w.WriteStartElement(n.Name);
        byte[]? picture = null;
        bool inline = package is not null && n.IsElement(OdfNamespaces.Draw, "image");
        if (inline)
        {
            string? href = OdfMarkup.Attribute(n, OdfNamespaces.XLink, "href");
            if (href is not null && !href.StartsWith('#') && !href.Contains(':', StringComparison.Ordinal) && package!.TryReadEntry(href, out ReadOnlyMemory<byte> bytes))
            {
                picture = bytes.ToArray();
            }
        }

        foreach (MarkupAttributeRef a in n.Attributes)
        {
            if (picture is not null && a.NamespaceUri == OdfNamespaces.XLink && a.LocalName.SequenceEqual("href"u8))
            {
                continue;
            }

            w.WriteAttributeRaw(a.RawName, a.RawValue, a.Quote);
        }

        if (picture is not null)
        {
            w.WriteStartElement("office:binary-data");
            w.WriteText(Convert.ToBase64String(picture));
            w.WriteEndElement();
        }

        for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            CopyNode(w, c, package, depth + 1, ct);
        }

        w.WriteEndElement();
    }
}
