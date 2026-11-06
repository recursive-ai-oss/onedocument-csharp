using System.Linq;
using System.Globalization;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Inspection;

/// <summary>
/// The categories every OOXML package shares, whichever kind of document it is: properties, personal information stored outside the
/// properties, custom XML, embedded objects, macros, external links, remote templates, web extensions, ink, thumbnails and hidden drawing
/// objects. One method serves both inspecting and sanitising so that what is found and what is removed cannot drift apart.
/// </summary>
internal static class PackageInspection
{
    private static readonly HashSet<string> s_coreNames = new(StringComparer.Ordinal)
    {
        "title", "subject", "creator", "keywords", "description", "lastModifiedBy", "category", "contentStatus", "identifier", "language",
        "created", "modified", "lastPrinted", "revision", "version", "contentType",
    };

    private static readonly string[] s_appPersonal = ["Manager", "Company", "HyperlinkBase", "Template"];

    internal static void Run(InspectionSession s, SanitizeSession? z)
    {
        Properties(s, z);
        PersonalInformation(s, z);
        CustomXml(s, z);
        ReportOnly(s);
        ExternalLinksAndTemplates(s, z);
        WebExtensions(s, z);
        Ink(s, z);
        Thumbnails(s, z);
        HiddenDrawingObjects(s, z);
        Macros(s, z);
    }

    private static bool Doing(SanitizeSession? z, InspectionCategory c) => z is not null && z.Selected(c);

    private static void Properties(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.Properties);
        foreach (IPackagePart core in s.TargetsOf(null, "/metadata/core-properties"))
        {
            MarkupDocument? doc = doing ? z!.Edit(core) : s.Markup(core);
            if (doc is null)
            {
                continue;
            }

            int removed = 0;
            foreach (MarkupNode c in InspectionXml.Children(doc.Root))
            {
                string local = c.LocalNameString;
                string text = c.InnerText;
                if (!s_coreNames.Contains(local) || text.Length == 0)
                {
                    continue;
                }

                s.Add(InspectionCategory.Properties, core.Name + "!" + local, "core property " + local, 1, InspectionXml.Brief(text));
                if (doing)
                {
                    c.Remove();
                    removed++;
                }
            }

            if (removed > 0)
            {
                z!.Removed(InspectionCategory.Properties, core.Name, "core properties", removed);
            }
        }

        foreach (IPackagePart app in s.TargetsOf(null, "/extended-properties"))
        {
            MarkupDocument? doc = doing ? z!.Edit(app) : s.Markup(app);
            if (doc is null)
            {
                continue;
            }

            int removed = 0;
            foreach (MarkupNode c in InspectionXml.Children(doc.Root))
            {
                string local = c.LocalNameString;
                bool personal = Array.IndexOf(s_appPersonal, local) >= 0 && c.InnerText.Length > 0;
                bool titles = local is "TitlesOfParts" or "HeadingPairs" or "HLinks";
                if (!personal && !titles)
                {
                    continue;
                }

                if (titles && c.InnerText.Length == 0 && !InspectionXml.HasChildElements(c))
                {
                    continue;
                }

                string desc = titles ? "extended property " + local + " (names of sheets, slides, links or headings)" : "extended property " + local;
                s.Add(InspectionCategory.Properties, app.Name + "!" + local, desc, 1, personal ? InspectionXml.Brief(c.InnerText) : null);
                if (doing)
                {
                    c.Remove();
                    removed++;
                }
            }

            if (removed > 0)
            {
                z!.Removed(InspectionCategory.Properties, app.Name, "extended properties", removed);
            }
        }

        foreach (IPackagePart custom in s.TargetsOf(null, "/custom-properties"))
        {
            MarkupDocument? doc = s.Markup(custom);
            if (doc is null)
            {
                continue;
            }

            int n = 0;
            foreach (MarkupNode p in InspectionXml.Children(doc.Root))
            {
                if (p.LocalName.SequenceEqual("property"u8))
                {
                    n++;
                    s.Add(InspectionCategory.Properties, custom.Name + "!" + (InspectionXml.Attr(p, "name"u8) ?? "?"), "custom property", 1, InspectionXml.Brief(p.InnerText));
                }
            }

            if (doing)
            {
                z!.RemoveEverywhere(custom.Name);
                z.Removed(InspectionCategory.Properties, custom.Name, "custom properties", Math.Max(1, n));
            }
        }
    }

    private static void PersonalInformation(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.PersonalInformation);
        foreach (IPackagePart p in s.PartsWhere(ct => ct.EndsWith(".people+xml", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("ms-excel.person+xml", StringComparison.OrdinalIgnoreCase)))
        {
            int n = 1;
            MarkupDocument? doc = s.Markup(p);
            if (doc is not null)
            {
                n = Math.Max(1, InspectionXml.Children(doc.Root).Count);
            }

            s.Add(InspectionCategory.PersonalInformation, p.Name, "person records (names and sign-in ids of authors)", n);
            if (doing)
            {
                string name = p.Name;
                z!.RemoveEverywhere(name);
                z.Removed(InspectionCategory.PersonalInformation, name, "person records", n);
            }
        }

        foreach (IPackagePart p in s.PartsWhere(ct => ct.Contains("printerSettings", StringComparison.OrdinalIgnoreCase)))
        {
            s.Add(InspectionCategory.PersonalInformation, p.Name, "printer settings (printer name and driver data)");
            if (doing)
            {
                string name = p.Name;
                z!.RemoveEverywhere(name);
                z.Removed(InspectionCategory.PersonalInformation, name, "printer settings");
            }
        }

        // ECMA-376 Part 1 §17.15.1.69 (rsids): editing-session ids stored in the settings and as attributes of the content identify
        // sessions, not people, but they link text to the sessions that wrote it; the Document Inspector class removes them.
        if (s.Kind == OfficeKind.Document)
        {
            foreach (IPackagePart p in s.PartsWhere(InspectionXml.IsWordMarkupType))
            {
                MarkupDocument? doc = doing ? z!.Edit(p) : s.Markup(p);
                if (doc is null)
                {
                    continue;
                }

                int n = 0;
                foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
                {
                    if (InspectionXml.Is(e, "wordprocessingml"u8, "rsids"u8))
                    {
                        n++;
                        if (doing)
                        {
                            e.Remove();
                        }

                        continue;
                    }

                    List<string>? names = null;
                    foreach (MarkupAttributeRef a in e.Attributes)
                    {
                        if (!a.IsNamespaceDeclaration && a.LocalName.StartsWith("rsid"u8) && a.Prefix.Length > 0)
                        {
                            n++;
                            (names ??= []).Add(System.Text.Encoding.UTF8.GetString(a.RawName));
                        }
                    }

                    if (doing && names is not null)
                    {
                        foreach (string raw in names)
                        {
                            e.RemoveAttribute(raw);
                        }
                    }
                }

                if (n > 0)
                {
                    s.Add(InspectionCategory.PersonalInformation, p.Name, "editing-session ids (rsid)", n);
                    if (doing)
                    {
                        z!.Removed(InspectionCategory.PersonalInformation, p.Name, "editing-session ids (rsid)", n);
                    }
                }
            }
        }
    }

    private static void CustomXml(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.CustomXml);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var owners = new List<string?> { null };
        owners.AddRange(s.Package.Parts.Where(p => p.ContentType.Contains("+xml", StringComparison.OrdinalIgnoreCase)).Select(p => (string?)p.Name));
        foreach (string? owner in owners)
        {
            var ids = new List<string>();
            foreach (Relationship r in s.RelationshipsOf(owner, "/customXml"))
            {
                ids.Add(r.Id);
                IPackagePart? target = s.Target(owner, r);
                if (target is null || !seen.Add(target.Name))
                {
                    continue;
                }

                string rootName = "data";
                MarkupDocument? doc = s.Markup(target);
                if (doc is not null)
                {
                    rootName = doc.Root.LocalNameString;
                    string ns = doc.Root.NamespaceUri;
                    if (rootName == "DataMashup")
                    {
                        s.Add(InspectionCategory.DataModel, target.Name, "Power Query mashup stored as custom XML");
                    }

                    if (ns.Length > 0)
                    {
                        rootName += " (" + ns + ")";
                    }
                }

                s.Add(InspectionCategory.CustomXml, target.Name, "custom XML data part", 1, InspectionXml.Brief(rootName));
            }

            if (doing && ids.Count > 0)
            {
                z!.Unlink(owner, ids);
            }
        }

        if (doing)
        {
            foreach (string name in seen)
            {
                z!.Removed(InspectionCategory.CustomXml, name, "custom XML data part");
            }
        }
    }

    private static void ReportOnly(InspectionSession s)
    {
        foreach (IPackagePart p in s.Package.Parts)
        {
            string ct = p.ContentType;
            if (ct.EndsWith("oleObject", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("openxmlformats-officedocument.package", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase))
            {
                s.Add(InspectionCategory.EmbeddedObjects, p.Name, "embedded object or document", 1, p.Length >= 0 ? p.Length.ToString(CultureInfo.InvariantCulture) + " bytes" : null);
            }
            else if (ct.StartsWith("application/vnd.ms-office.activeX", StringComparison.OrdinalIgnoreCase) && ct.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
            {
                s.Add(InspectionCategory.ActiveX, p.Name, "ActiveX control");
            }
            else if (ct.Contains("pivotCacheDefinition", StringComparison.OrdinalIgnoreCase))
            {
                s.Add(InspectionCategory.PivotCaches, p.Name, "PivotTable cache");
            }
            else if (ct.Contains("pivotCacheRecords", StringComparison.OrdinalIgnoreCase))
            {
                s.Add(InspectionCategory.PivotCaches, p.Name, "PivotTable cache records (a copy of the source data)");
            }
            else if (ct.Contains("slicerCache", StringComparison.OrdinalIgnoreCase) || ct.Contains("timelineCache", StringComparison.OrdinalIgnoreCase))
            {
                s.Add(InspectionCategory.PivotCaches, p.Name, "slicer or timeline cache");
            }
        }

        // ECMA-376 Part 1 §17.17.2 altChunk: another document (HTML, RTF, Word) stored as a part and merged into the text on open.
        if (s.MainPartName is not null)
        {
            foreach (Relationship r in s.RelationshipsOf(s.MainPartName, "/aFChunk"))
            {
                s.Add(InspectionCategory.EmbeddedObjects, s.MainPartName + "!" + r.Id, "imported document part (altChunk)", 1, InspectionXml.Brief(r.Target, 120));
            }
        }

        foreach (Relationship r in s.RelationshipsOf(s.MainPartName, "/powerPivotData"))
        {
            s.Add(InspectionCategory.DataModel, r.Target, "data model (Power Pivot)");
        }
    }

    private static void ExternalLinksAndTemplates(InspectionSession s, SanitizeSession? z)
    {
        bool doingTemplates = Doing(z, InspectionCategory.RemoteTemplates);
        var owners = new List<string?> { null };
        owners.AddRange(s.Package.Parts.Select(p => (string?)p.Name));
        foreach (string? owner in owners)
        {
            List<string>? templateIds = null;
            foreach (Relationship r in s.Package.GetRelationships(owner))
            {
                string where = (owner ?? "/") + "!" + r.Id;
                if (r.Type.EndsWith("/attachedTemplate", StringComparison.OrdinalIgnoreCase))
                {
                    s.Add(InspectionCategory.RemoteTemplates, where, "attached template", 1, InspectionXml.Brief(r.Target, 120));
                    (templateIds ??= []).Add(r.Id);
                    continue;
                }

                if (r.TargetMode != RelationshipTargetMode.External || r.Type.EndsWith("/hyperlink", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (r.Type.EndsWith("/externalLinkPath", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // reported with its link part by the workbook provider
                }

                if (r.Type.EndsWith("/image", StringComparison.OrdinalIgnoreCase) || r.Type.EndsWith("/oleObject", StringComparison.OrdinalIgnoreCase)
                    || r.Type.EndsWith("/frame", StringComparison.OrdinalIgnoreCase) || r.Type.EndsWith("/subDocument", StringComparison.OrdinalIgnoreCase)
                    || r.Type.EndsWith("/mailMergeSource", StringComparison.OrdinalIgnoreCase) || r.Type.EndsWith("/video", StringComparison.OrdinalIgnoreCase)
                    || r.Type.EndsWith("/audio", StringComparison.OrdinalIgnoreCase) || r.Type.EndsWith("/media", StringComparison.OrdinalIgnoreCase))
                {
                    string kind = r.Type[(r.Type.LastIndexOf('/') + 1)..];
                    s.Add(InspectionCategory.ExternalLinks, where, "linked " + kind, 1, InspectionXml.Brief(r.Target, 120));
                    if (Doing(z, InspectionCategory.ExternalLinks) && owner is not null)
                    {
                        z!.NotRemoved(InspectionCategory.ExternalLinks, "sanitize.unsupported-construct", where, "A linked " + kind + " is bound to the content that shows it; removing the link would remove or break that content.");
                    }
                }
            }

            if (doingTemplates && templateIds is not null)
            {
                z!.Unlink(owner, templateIds);
                z.Removed(InspectionCategory.RemoteTemplates, owner ?? "/", "attached template", templateIds.Count);
            }
        }
    }

    private static void WebExtensions(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.WebExtensions);
        foreach (IPackagePart p in s.PartsWhere(ct => ct.StartsWith("application/vnd.ms-office.webextension", StringComparison.OrdinalIgnoreCase)))
        {
            bool taskpanes = p.ContentType.Contains("taskpanes", StringComparison.OrdinalIgnoreCase);
            s.Add(InspectionCategory.WebExtensions, p.Name, taskpanes ? "task pane list" : "add-in (web extension)");
        }

        if (!doing)
        {
            return;
        }

        List<string> names = [.. s.PartsWhere(ct => ct.StartsWith("application/vnd.ms-office.webextension", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name)];
        foreach (string name in names)
        {
            z!.RemoveEverywhere(name);
            z.Removed(InspectionCategory.WebExtensions, name, "web extension part");
        }
    }

    private static void Ink(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.Ink);
        List<string> names = [.. s.PartsWhere(ct => ct.StartsWith("application/inkml+xml", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name)];
        foreach (string name in names)
        {
            s.Add(InspectionCategory.Ink, name, "ink annotation");
            if (doing)
            {
                z!.RemoveEverywhere(name);
                z.Removed(InspectionCategory.Ink, name, "ink annotation");
            }
        }
    }

    private static void Thumbnails(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.Thumbnails);
        foreach (Relationship r in s.RelationshipsOf(null, "/thumbnail"))
        {
            string where = "/" + r.Target.TrimStart('/');
            s.Add(InspectionCategory.Thumbnails, where, "preview picture");
            if (doing)
            {
                z!.Unlink(null, [r.Id]);
                z.Removed(InspectionCategory.Thumbnails, where, "preview picture");
            }
        }
    }

    // A shape with hidden="1" is not drawn but is stored whole, picture or text included (ECMA-376 Part 1 §20.1.2.2.8 cNvPr, §20.4.2.5 docPr).
    private static void HiddenDrawingObjects(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.InvisibleContent);
        foreach (IPackagePart p in s.Package.Parts)
        {
            string ct = p.ContentType;
            bool slide = ct.EndsWith("presentationml.slide+xml", StringComparison.OrdinalIgnoreCase)
                || ct.EndsWith("presentationml.slideLayout+xml", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("presentationml.slideMaster+xml", StringComparison.OrdinalIgnoreCase)
                || ct.EndsWith("presentationml.notesSlide+xml", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("presentationml.notesMaster+xml", StringComparison.OrdinalIgnoreCase)
                || ct.EndsWith("presentationml.handoutMaster+xml", StringComparison.OrdinalIgnoreCase);
            bool excelDrawing = s.Kind == OfficeKind.Workbook && ct.EndsWith("officedocument.drawing+xml", StringComparison.OrdinalIgnoreCase);
            bool word = s.Kind == OfficeKind.Document && InspectionXml.IsWordMarkupType(ct);
            if (!slide && !excelDrawing && !word)
            {
                continue;
            }

            MarkupDocument? doc = doing ? z!.Edit(p) : s.Markup(p);
            if (doc is null)
            {
                continue;
            }

            int removed = 0;
            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                bool nvPr = (e.LocalName.SequenceEqual("cNvPr"u8) || e.LocalName.SequenceEqual("docPr"u8)) && InspectionXml.AttrTrue(e, "hidden"u8);
                bool vml = word && e.LocalName.SequenceEqual("shape"u8) && e.NamespaceUriBytes.IndexOf("vml"u8) >= 0
                    && (InspectionXml.Attr(e, "style"u8)?.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase) ?? false);
                if ((!nvPr && !vml) || !InspectionXml.IsAttached(e))
                {
                    continue;
                }

                MarkupNode target = ShapeOf(e, vml);
                if (target.IsNull)
                {
                    continue;
                }

                string name = InspectionXml.Attr(e, "name"u8) ?? InspectionXml.Attr(e, "id"u8) ?? "shape";
                string where = p.Name + "!" + name;
                string shown = TextOf(target);
                s.Add(InspectionCategory.InvisibleContent, where, "hidden " + (slide ? "slide shape" : "drawing object"), 1, shown.Length == 0 ? null : InspectionXml.Brief(shown));
                if (!doing)
                {
                    continue;
                }

                if (slide && IsAnimated(doc, InspectionXml.Attr(e, "id"u8)))
                {
                    z!.NotRemoved(InspectionCategory.InvisibleContent, "sanitize.referenced", where, "An animation refers to the shape; removing it would leave the timeline pointing at nothing.");
                    continue;
                }

                z!.RemoveSubtree(p.Name, target);
                z.Removed(InspectionCategory.InvisibleContent, where, "hidden shape");
                removed++;
            }

            _ = removed;
        }
    }

    // The text a shape shows: its a:t / w:t runs.
    private static string TextOf(MarkupNode shape)
    {
        var sb = new System.Text.StringBuilder();
        foreach (MarkupNode t in InspectionXml.Descendants(shape))
        {
            if (t.LocalName.SequenceEqual("t"u8))
            {
                sb.Append(t.InnerText);
            }
        }

        return sb.ToString();
    }

    // The shape that a cNvPr/docPr describes, or a null node when it is something this does not remove.
    private static MarkupNode ShapeOf(MarkupNode e, bool vml)
    {
        if (vml)
        {
            MarkupNode run = InspectionXml.Ancestor(e, "r"u8);
            MarkupNode pict = InspectionXml.Ancestor(e, "pict"u8);
            return !pict.IsNull ? (run.IsNull ? pict : run) : default;
        }

        if (e.LocalName.SequenceEqual("docPr"u8))
        {
            // wp:docPr describes a drawing: wp:inline or wp:anchor inside w:drawing, in a run.
            MarkupNode drawing = InspectionXml.Ancestor(e, "drawing"u8);
            if (drawing.IsNull)
            {
                return default;
            }

            MarkupNode run = drawing.Parent;
            if (!run.IsNull && run.LocalName.SequenceEqual("r"u8) && OnlyRunProperties(run, drawing))
            {
                return run;
            }

            return drawing;
        }

        MarkupNode nv = e.Parent;
        MarkupNode shape = nv.IsNull ? default : nv.Parent;
        if (shape.IsNull || shape.Kind != MarkupNodeKind.Element)
        {
            return default;
        }

        ReadOnlySpan<byte> n = shape.LocalName;
        if (!(n.SequenceEqual("sp"u8) || n.SequenceEqual("pic"u8) || n.SequenceEqual("grpSp"u8) || n.SequenceEqual("graphicFrame"u8) || n.SequenceEqual("cxnSp"u8)))
        {
            return default;
        }

        // A drawing-sheet shape sits in an anchor that holds only it; the anchor goes with it.
        MarkupNode parent = shape.Parent;
        if (!parent.IsNull && parent.LocalName.EndsWith("Anchor"u8))
        {
            return parent;
        }

        // A shape inside a Word text-box (wps) or an AlternateContent branch is handled by the docPr of the outer drawing.
        return shape;
    }

    private static bool OnlyRunProperties(MarkupNode run, MarkupNode drawing)
    {
        foreach (MarkupNode c in run.ChildElements)
        {
            if (c != drawing && !c.LocalName.SequenceEqual("rPr"u8))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAnimated(MarkupDocument doc, string? id)
    {
        if (id is null)
        {
            return false;
        }

        foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
        {
            if (e.LocalName.SequenceEqual("spTgt"u8) && InspectionXml.Attr(e, "spid"u8) == id)
            {
                return true;
            }
        }

        return false;
    }

    private static void Macros(InspectionSession s, SanitizeSession? z)
    {
        VbaProject? project;
        try
        {
            project = VbaProject.FromPackage(s.Package, s.Context);
        }
        catch (OfficeException ex)
        {
            s.Note("The VBA project could not be read: " + ex.Defect.Code);
            return;
        }

        if (project is null)
        {
            return;
        }

        int modules = Math.Max(1, project.Modules.Count);
        s.Add(InspectionCategory.Macros, project.Container, "VBA project" + (project.IsSigned ? " (signed)" : string.Empty), modules, project.Name);
        if (Doing(z, InspectionCategory.Macros))
        {
            Vba.VbaRemovalReport report = VbaRemoval.RemoveFromPackage(s.Package, s.Format, s.Context);
            foreach (Vba.VbaRemovedItem item in report.Removed)
            {
                z!.Removed(InspectionCategory.Macros, item.Location, item.Description);
            }

            foreach (Vba.VbaRemovalNote note in report.NotDone)
            {
                z!.NotRemoved(InspectionCategory.Macros, "sanitize.unsupported-construct", note.Location.ToString(), note.Message);
            }
        }
    }
}
