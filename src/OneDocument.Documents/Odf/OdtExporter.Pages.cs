using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Sections, page layouts and master pages: each w:sectPr becomes a page layout and a master page (with its headers and footers); a section
// after the first starts its first block with a style that names its master page.
internal sealed partial class OdtExporter
{
    private sealed class SectionInfo
    {
        public MarkupNode SectPr;
        public string Master = "Standard";
        public bool Continuous;
        public int Columns = 1;
        public Dictionary<string, string> Refs = new(StringComparer.Ordinal);
    }

    private sealed class MasterDef
    {
        public string Name = string.Empty;
        public string Layout = string.Empty;
        public MarkupNode SectPr;
        public Dictionary<string, string> Refs = new(StringComparer.Ordinal);
    }

    private readonly List<SectionInfo> _sections = [];
    private readonly List<MasterDef> _masters = [];
    private readonly Dictionary<string, MasterDef> _mastersBySig = new(StringComparer.Ordinal);
    private int _sectionIndex;
    private string? _pendingMaster;

    private void FindSections(MarkupNode body)
    {
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(MarkupNode sectPr)
        {
            var info = new SectionInfo { SectPr = sectPr };
            foreach (MarkupNode r in sectPr.ChildElements)
            {
                if (IsW(r, "headerReference") || IsW(r, "footerReference"))
                {
                    string key = (IsW(r, "headerReference") ? "header:" : "footer:") + (A(r, "type") ?? "default");
                    if (RA(r, "id") is { } id)
                    {
                        refs[key] = id;
                    }
                }
            }

            // A section with no header of a kind shows the previous section's (ECMA-376 Part 1 §17.10.5: linked to previous).
            info.Refs = new Dictionary<string, string>(refs, StringComparer.Ordinal);
            info.Continuous = A(Child(sectPr, "type"), "val") == "continuous";
            info.Columns = Math.Max(1, Int(A(Child(sectPr, "cols"), "num"), 1));
            info.Master = MasterFor(info).Name;
            _sections.Add(info);
        }

        if (!body.IsNull)
        {
            foreach (MarkupNode c in body.ChildElements)
            {
                if (IsW(c, "p") && Child(Child(c, "pPr"), "sectPr") is { IsNull: false } sp)
                {
                    Add(sp);
                }
            }

            MarkupNode last = Child(body, "sectPr");
            Add(last);
        }
        else
        {
            Add(default);
        }
    }

    private MasterDef MasterFor(SectionInfo s)
    {
        MarkupNode pgSz = Child(s.SectPr, "pgSz"), pgMar = Child(s.SectPr, "pgMar"), cols = Child(s.SectPr, "cols"), pgNum = Child(s.SectPr, "pgNumType");
        var sig = new StringBuilder();
        foreach (MarkupNode n in new[] { pgSz, pgMar, cols, pgNum })
        {
            if (n.IsNull)
            {
                sig.Append('-');
                continue;
            }

            foreach (MarkupAttributeRef a in n.Attributes)
            {
                sig.Append(Encoding.UTF8.GetString(a.RawName)).Append('=').Append(a.Value).Append(';');
            }
        }

        bool titlePg = !Child(s.SectPr, "titlePg").IsNull && OnChild(s.SectPr, "titlePg");
        sig.Append("|tp=").Append(titlePg);
        foreach (KeyValuePair<string, string> kv in s.Refs.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sig.Append('|').Append(kv.Key).Append('=').Append(kv.Value);
        }

        string key = sig.ToString();
        if (_mastersBySig.TryGetValue(key, out MasterDef? existing))
        {
            return existing;
        }

        var m = new MasterDef
        {
            Name = _masters.Count == 0 ? "Standard" : "MP" + OdfUnits.I(_masters.Count),
            Layout = "pm" + OdfUnits.I(_masters.Count + 1),
            SectPr = s.SectPr,
            Refs = s.Refs,
        };
        _masters.Add(m);
        _mastersBySig[key] = m;
        return m;
    }

    // Called after a paragraph that carries w:sectPr: the next block opens the next section.
    private void SectionEnded()
    {
        _sectionIndex++;
        if (_sectionIndex < _sections.Count)
        {
            SectionInfo s = _sections[_sectionIndex];
            // A continuous section stays on the page; a change of columns in it is a text section.
            _pendingMaster = s.Continuous && s.Master == _sections[_sectionIndex - 1].Master ? null : s.Master;
            if (s.Continuous && s.Master != _sections[_sectionIndex - 1].Master)
            {
                Lost("loss.continuous-section-page-change", "page changes in continuous sections", "A continuous section break that changes the page size or margins starts a new page.");
            }
        }
    }

    private string? TakeMaster()
    {
        string? m = _pendingMaster;
        _pendingMaster = null;
        return m;
    }

    // ---- page layouts and master pages -----------------------------------------------------------------------------------------------

    private string BuildMasters(out string layouts)
    {
        var lay = new StringBuilder();
        var sb = new StringBuilder();
        foreach (MasterDef m in _masters)
        {
            MarkupNode sp = m.SectPr;
            MarkupNode pgSz = Child(sp, "pgSz"), pgMar = Child(sp, "pgMar"), cols = Child(sp, "cols"), pgNum = Child(sp, "pgNumType");
            int w = Int(A(pgSz, "w"), 11906), h = Int(A(pgSz, "h"), 16838);
            bool landscape = A(pgSz, "orient") == "landscape";
            int top = Int(A(pgMar, "top"), 1440), bottom = Int(A(pgMar, "bottom"), 1440), left = Int(A(pgMar, "left") ?? A(pgMar, "start"), 1440), right = Int(A(pgMar, "right") ?? A(pgMar, "end"), 1440);
            int headerDist = Int(A(pgMar, "header"), 720), footerDist = Int(A(pgMar, "footer"), 720);
            bool hasHeader = m.Refs.Keys.Any(k => k.StartsWith("header:", StringComparison.Ordinal));
            bool hasFooter = m.Refs.Keys.Any(k => k.StartsWith("footer:", StringComparison.Ordinal));

            lay.Append("<style:page-layout style:name=\"").Append(m.Layout).Append("\"><style:page-layout-properties fo:page-width=\"").Append(Cm(w)).Append("\" fo:page-height=\"").Append(Cm(h))
                .Append("\" style:print-orientation=\"").Append(landscape ? "landscape" : "portrait").Append('"');
            lay.Append(" fo:margin-top=\"").Append(Cm(hasHeader ? Math.Min(headerDist, top) : top)).Append("\" fo:margin-bottom=\"").Append(Cm(hasFooter ? Math.Min(footerDist, bottom) : bottom))
                .Append("\" fo:margin-left=\"").Append(Cm(left)).Append("\" fo:margin-right=\"").Append(Cm(right)).Append('"');
            string fmt = A(pgNum, "fmt") switch { "lowerRoman" => "i", "upperRoman" => "I", "lowerLetter" => "a", "upperLetter" => "A", _ => "1" };
            lay.Append(" style:num-format=\"").Append(fmt).Append("\" style:writing-mode=\"lr-tb\"");
            int n = Int(A(cols, "num"), 1);
            if (n > 1)
            {
                lay.Append("><style:columns fo:column-count=\"").Append(OdfUnits.I(n)).Append("\" fo:column-gap=\"").Append(Cm(Int(A(cols, "space"), 720))).Append("\">");
                if (A(cols, "sep") is "1" or "true" or "on")
                {
                    lay.Append("<style:column-sep style:width=\"0.018cm\" style:color=\"#000000\"/>");
                }

                for (int i = 0; i < n; i++)
                {
                    lay.Append("<style:column style:rel-width=\"1*\" fo:start-indent=\"0cm\" fo:end-indent=\"0cm\"/>");
                }

                lay.Append("</style:columns></style:page-layout-properties>");
            }
            else
            {
                lay.Append("/>");
            }

            if (hasHeader)
            {
                lay.Append("<style:header-style><style:header-footer-properties fo:min-height=\"").Append(Cm(Math.Max(0, top - headerDist))).Append("\" fo:margin-left=\"0cm\" fo:margin-right=\"0cm\" fo:margin-bottom=\"0cm\" style:dynamic-spacing=\"true\"/></style:header-style>");
            }

            if (hasFooter)
            {
                lay.Append("<style:footer-style><style:header-footer-properties fo:min-height=\"").Append(Cm(Math.Max(0, bottom - footerDist))).Append("\" fo:margin-left=\"0cm\" fo:margin-right=\"0cm\" fo:margin-top=\"0cm\" style:dynamic-spacing=\"true\"/></style:footer-style>");
            }

            lay.Append("</style:page-layout>");

            sb.Append("<style:master-page style:name=\"").Append(m.Name).Append("\" style:page-layout-name=\"").Append(m.Layout).Append("\">");
            bool titlePg = OnChild(sp, "titlePg") && !Child(sp, "titlePg").IsNull;
            foreach (string kind in new[] { "header", "footer" })
            {
                foreach ((string type, string element) in new[] { ("default", kind), ("even", kind + "-left"), ("first", kind + "-first") })
                {
                    if (type == "first" && !titlePg)
                    {
                        continue;
                    }

                    if (type == "even" && !_evenAndOdd)
                    {
                        continue;
                    }

                    if (!m.Refs.TryGetValue(kind + ":" + type, out string? rid))
                    {
                        continue;
                    }

                    sb.Append('<').Append("style:").Append(element).Append('>').Append(HeaderFooterContent(rid)).Append("</style:").Append(element).Append('>');
                }
            }

            sb.Append("</style:master-page>");
        }

        layouts = lay.ToString();
        return sb.ToString();
    }

    private string HeaderFooterContent(string rid)
    {
        string saved = _part;
        _part = _doc.MainPartName;
        try
        {
            Packaging.IPackagePart? target = Target(rid);
            if (target is null)
            {
                return "<text:p/>";
            }

            MarkupNode root = LoadPart(target.Name);
            _part = target.Name;
            var sb = new StringBuilder();
            SubContext(() =>
            {
                foreach (MarkupNode c in root.ChildElements)
                {
                    Block(sb, c);
                }

                CloseLists(sb);
            });
            return sb.Length == 0 ? "<text:p/>" : sb.ToString();
        }
        finally
        {
            _part = saved;
        }
    }
}
