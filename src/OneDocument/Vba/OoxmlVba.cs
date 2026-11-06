using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Vba;

/// <summary>Finding and removing the VBA project of an OPC package: one implementation under the three kinds.</summary>
internal static class OoxmlVba
{
    internal const string ProjectContentType = "application/vnd.ms-office.vbaProject";
    internal const string FormDataContentType = "application/vnd.ms-word.vbaData+xml";
    private const string SignaturePrefix = "application/vnd.ms-office.vbaProjectSignature";

    // [MS-OFFMACRO2] relationship types for the project, its signatures and the Word form data.
    private static readonly string[] RelationshipTypeEnds = ["/vbaProject", "/vbaProjectSignature", "/vbaProjectSignatureAgile", "/vbaProjectSignatureV3", "/wordVbaData"];

    // The main part's macro-enabled content type and the macro-free one it becomes. Add-ins and binary workbooks have no macro-free type.
    private static readonly (string Enabled, string Free, OfficeFormat From, OfficeFormat To)[] MainTypes =
    [
        ("application/vnd.ms-word.document.macroEnabled.main+xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", OfficeFormat.Docm, OfficeFormat.Docx),
        ("application/vnd.ms-word.template.macroEnabledTemplate.main+xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml", OfficeFormat.Dotm, OfficeFormat.Dotx),
        ("application/vnd.ms-excel.sheet.macroEnabled.main+xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", OfficeFormat.Xlsm, OfficeFormat.Xlsx),
        ("application/vnd.ms-excel.template.macroEnabled.main+xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml", OfficeFormat.Xltm, OfficeFormat.Xltx),
        ("application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml", OfficeFormat.Pptm, OfficeFormat.Pptx),
        ("application/vnd.ms-powerpoint.template.macroEnabled.main+xml", "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml", OfficeFormat.Potm, OfficeFormat.Potx),
        ("application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml", "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml", OfficeFormat.Ppsm, OfficeFormat.Ppsx),
    ];

    internal static bool IsProjectPart(IPackagePart p) =>
        string.Equals(p.ContentType, ProjectContentType, StringComparison.OrdinalIgnoreCase);

    internal static bool IsSignaturePart(IPackagePart p) => VbaSignatures.OfPart(p.Name, p.ContentType) != VbaSignatureKinds.None;

    internal static bool IsFormDataPart(IPackagePart p) =>
        string.Equals(p.ContentType, FormDataContentType, StringComparison.OrdinalIgnoreCase);

    internal static OfficeFormat MacroFreeFormat(OfficeFormat format)
    {
        foreach (var t in MainTypes)
        {
            if (t.From == format)
            {
                return t.To;
            }
        }

        return format;
    }

    /// <summary>Reads the project of a package.</summary>
    internal static VbaProject? Read(IOpcPackage package, OpenContext context)
    {
        IPackagePart? project = null;
        VbaSignatureKinds signatures = VbaSignatureKinds.None;
        foreach (IPackagePart p in package.Parts)
        {
            if (IsProjectPart(p))
            {
                project ??= p;
            }
            else
            {
                signatures |= VbaSignatures.OfPart(p.Name, p.ContentType);
            }
        }

        if (project is null)
        {
            return null;
        }

        try
        {
            using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(project.GetBytes()), context);
            return VbaProject.Parse(cfb, string.Empty, project.Name, context, signatures);
        }
        catch (OfficeFormatException ex)
        {
            context.Defects.Report(new Defect(DefectKind.Packaging, "vba.project-not-compound", DefectSeverity.Dropped, "The project part is not a readable compound file; it has no modules. " + ex.Message, DefectLocation.InPart(project.Name)));
            return new VbaProject(project.Name) { Signatures = signatures };
        }
    }

    internal static VbaRemovalReport RemoveFromFile(IByteSource source, OfficeFormat format, Stream destination, OpenContext context, CancellationToken cancellationToken)
    {
        using OpcPackage package = OpcPackage.Open(source, context);
        var report = new VbaRemovalReport(format);
        RemoveParts(package, report, context);
        OfficeFormat free = MacroFreeFormat(format);
        if (free != format)
        {
            RetypeMain(package, report);
            report.OutputFormat = free;
        }
        else if (format is OfficeFormat.Xlam or OfficeFormat.Ppam or OfficeFormat.Xlsb)
        {
            report.Note("vba.no-macro-free-type", "The format has no macro-free counterpart, so the file stays an add-in or binary workbook without its project.", DefectLocation.None);
        }

        package.WriteTo(destination, cancellationToken);
        return report;
    }

    // The package's officeDocument relationship names the main part.
    internal static void RetypeMain(OpcPackage package, VbaRemovalReport? report)
    {
        foreach (Relationship r in package.GetRelationships(null))
        {
            if (!r.Type.EndsWith("/officeDocument", StringComparison.Ordinal) || !OpcPackage.TryResolveTarget(null, r, out PartUri main))
            {
                continue;
            }

            IPackagePart? part = package.FindPart(main.Value);
            if (part is null)
            {
                return;
            }

            foreach (var t in MainTypes)
            {
                if (string.Equals(part.ContentType, t.Enabled, StringComparison.OrdinalIgnoreCase))
                {
                    package.SetPartContentType(part.Name, t.Free);
                    report?.Add(VbaRemovedKind.ContentType, part.Name, 0, "Main part content type " + t.Enabled + " changed to " + t.Free + ".");
                    return;
                }
            }

            return;
        }
    }

    /// <summary>
    /// Removes the project, signature and form-data parts, the relationships to them and their content types from a package, and fills in
    /// the report. The main part's content type is not changed here (the hubs retype it when they save in a macro-free format).
    /// </summary>
    internal static void RemoveParts(IOpcPackage package, VbaRemovalReport report, OpenContext context)
    {
        var doomed = new List<IPackagePart>();
        foreach (IPackagePart p in package.Parts)
        {
            if (IsProjectPart(p) || IsSignaturePart(p) || IsFormDataPart(p))
            {
                doomed.Add(p);
            }
        }

        if (doomed.Count > 0 && doomed.Exists(IsProjectPart))
        {
            report.HadProject = true;
            try
            {
                VbaProject? project = Read(package, context);
                report.ModulesRemoved = project?.Modules.Count ?? 0;
            }
            catch (OfficeException ex)
            {
                report.Note("vba.project-unreadable", "The project could not be read before removal, so its modules are not counted: " + ex.Message, ex.Location);
            }
        }

        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IPackagePart p in doomed)
        {
            VbaRemovedKind kind;
            string what;
            if (IsProjectPart(p))
            {
                kind = VbaRemovedKind.Project;
                what = "VBA project";
            }
            else if (IsFormDataPart(p))
            {
                kind = VbaRemovedKind.FormData;
                what = "VBA form data";
            }
            else
            {
                kind = VbaRemovedKind.Signature;
                what = "VBA project signature";
                report.SignaturesRemoved |= VbaSignatures.OfPart(p.Name, p.ContentType);
            }

            report.Add(kind, p.Name, p.Length, "Removed the " + what + ".");
            removed.Add(p.Name);
            string ext = ContentTypes.ExtensionOf(p.Name);
            if (ext.Length > 0)
            {
                extensions.Add(ext);
            }
        }

        foreach (IPackagePart p in doomed)
        {
            package.RemovePart(p.Name);
        }

        DropRelationships(package, removed, report);
        if (package is OpcPackage opc)
        {
            DropUnusedDefaults(opc, extensions);
        }
    }

    private static void DropRelationships(IOpcPackage package, HashSet<string> removed, VbaRemovalReport report)
    {
        var sources = new List<string?> { null };
        foreach (IPackagePart p in package.Parts)
        {
            sources.Add(p.Name);
        }

        foreach (string? source in sources)
        {
            IReadOnlyList<Relationship> rels = package.GetRelationships(source);
            if (rels.Count == 0)
            {
                continue;
            }

            var kept = new List<Relationship>(rels.Count);
            foreach (Relationship r in rels)
            {
                bool toRemoved = OpcPackage.TryResolveTarget(source, r, out PartUri target) && removed.Contains(target.Value);
                if (toRemoved || (r.TargetMode == RelationshipTargetMode.Internal && HasVbaRelationshipType(r.Type)))
                {
                    report.Add(VbaRemovedKind.Relationship, (source ?? "/") + "#" + r.Id, 0, "Removed the relationship to " + r.Target + ".");
                }
                else
                {
                    kept.Add(r);
                }
            }

            if (kept.Count != rels.Count)
            {
                package.SetRelationships(source, kept);
            }
        }
    }

    private static bool HasVbaRelationshipType(string type)
    {
        foreach (string end in RelationshipTypeEnds)
        {
            if (type.EndsWith(end, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void DropUnusedDefaults(OpcPackage package, HashSet<string> extensions)
    {
        foreach (string ext in extensions)
        {
            bool inUse = false;
            foreach (IPackagePart p in package.Parts)
            {
                if (string.Equals(ContentTypes.ExtensionOf(p.Name), ext, StringComparison.OrdinalIgnoreCase)
                    && !package.ContentTypes.Overrides.Any(o => string.Equals(o.PartName, p.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    inUse = true;
                    break;
                }
            }

            string? type = null;
            foreach (ContentTypeDefault d in package.ContentTypes.Defaults)
            {
                if (string.Equals(d.Extension, ext, StringComparison.OrdinalIgnoreCase))
                {
                    type = d.ContentType;
                }
            }

            if (!inUse && type is not null && (type.Equals(ProjectContentType, StringComparison.OrdinalIgnoreCase) || type.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase) || type.Equals(FormDataContentType, StringComparison.OrdinalIgnoreCase)))
            {
                package.ContentTypes.RemoveDefault(ext);
            }
        }
    }
}
