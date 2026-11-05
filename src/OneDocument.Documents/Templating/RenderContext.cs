using System.Globalization;
using System.Security.Cryptography;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>What one render of a document needs across its stories: identifiers, the links the template made, relationships added to parts, pictures, and the warnings.</summary>
internal sealed class RenderContext
{
    private const string TransitionalRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string StrictRelationships = "http://purl.oclc.org/ooxml/officeDocument/relationships/";

    private readonly Dictionary<string, string> _imageParts = new(StringComparer.Ordinal);
    private readonly List<(string Url, string Text)> _links = [];

    public RenderContext(Document document, TemplateOptions options)
    {
        Document = document;
        Options = options;
        RootIds = new IdTracker(options.RandomSeed);
        Ids = RootIds;
        Ns = document.WordprocessingNamespaceUri;
        Strict = document.IsStrict;
        Functions = BuildFunctions(options.Functions);
    }

    public Document Document { get; }

    public TemplateOptions Options { get; }

    public IdTracker Ids { get; set; }

    public IdTracker RootIds { get; }

    public RichListState Lists { get; } = new();

    public string Ns { get; }

    public bool Strict { get; }

    public FunctionRegistry Functions { get; }

    /// <summary>The warnings from this document's own markup (dropped HTML, unusable pictures).</summary>
    public List<Defect> Warnings { get; } = [];

    /// <summary>Where the story being emitted lives, for relationships.</summary>
    public string CurrentPart { get; set; } = string.Empty;

    public string RelationshipType(string name) => (Strict ? StrictRelationships : TransitionalRelationships) + name;

    // The link function: `link(url, text)` registers the pair and returns a token the emitter turns into a w:hyperlink. The token holds an index into
    // this table only, so data that happens to contain the marker characters can reach nothing the template did not itself create.
    private FunctionRegistry BuildFunctions(FunctionRegistry? user)
    {
        FunctionRegistry registry = FunctionRegistry.CreateDefault();
        if (user is not null)
        {
            foreach (FunctionDefinition d in user.Definitions)
            {
                registry.Add(d);
            }
        }

        registry.AddCustom("LINK", 1, 2, args =>
        {
            string url = CalcConvert.ToText(args[0]);
            string text = args.Length > 1 ? CalcConvert.ToText(args[1]) : url;
            if (!LinkTokens.IsAllowedTarget(url))
            {
                return CalcValue.FromText(text);
            }

            _links.Add((url, text));
            return CalcValue.FromText(LinkTokens.Open + (_links.Count - 1).ToString(CultureInfo.InvariantCulture) + LinkTokens.Close);
        });
        return registry;
    }

    public (string Url, string Text)? Link(int index) => index >= 0 && index < _links.Count ? _links[index] : null;

    /// <summary>Adds a relationship from a part and returns its id; the same target and type reuse one relationship. It is written to the package at once, so parts created meanwhile (a style sheet, a numbering part) cannot overwrite it.</summary>
    public string AddRelationship(string partName, string type, string target, RelationshipTargetMode mode)
    {
        var list = new List<Relationship>(Document.Package.GetRelationships(partName));
        foreach (Relationship r in list)
        {
            if (r.Type == type && r.Target == target && r.TargetMode == mode)
            {
                return r.Id;
            }
        }

        int n = 1;
        while (list.Exists(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
        {
            n++;
        }

        string id = "rId" + n.ToString(CultureInfo.InvariantCulture);
        list.Add(new Relationship(id, type, target, mode));
        Document.Package.SetRelationships(partName, list);
        return id;
    }

    /// <summary>Stores a picture once per distinct content and relates it from the current part.</summary>
    /// <returns>The relationship id.</returns>
    public string AddImage(byte[] bytes, string extension, string contentType)
    {
        string key = Convert.ToHexString(SHA256.HashData(bytes));
        if (!_imageParts.TryGetValue(key, out string? partName))
        {
            int n = 1;
            do
            {
                partName = "/word/media/image" + n.ToString(CultureInfo.InvariantCulture) + "." + extension;
                n++;
            }
            while (Document.Package.FindPart(partName) is not null);
            Document.Package.AddPart(partName, contentType, bytes);
            _imageParts[key] = partName;
        }

        string target = RelativeTarget(CurrentPart, partName);
        return AddRelationship(CurrentPart, RelationshipType("image"), target, RelationshipTargetMode.Internal);
    }

    private static string RelativeTarget(string fromPart, string toPart)
    {
        // Parts of a Word package sit in /word/ (or its subfolders); targets are relative to the source part's folder (ECMA-376 Part 2 §9.3).
        string from = fromPart[..(fromPart.LastIndexOf('/') + 1)];
        return toPart.StartsWith(from, StringComparison.OrdinalIgnoreCase) ? toPart[from.Length..] : toPart;
    }
}

/// <summary>The marker characters of a template-made link in rendered text.</summary>
internal static class LinkTokens
{
    public const char Open = '\uE000';
    public const char Close = '\uE001';

    /// <summary>Only web, mail and in-document targets become links; anything else (script, file, custom schemes) is printed as plain text.</summary>
    public static bool IsAllowedTarget(string url)
    {
        if (url.Length == 0 || url.Length > 2083)
        {
            return false;
        }

        foreach (char c in url)
        {
            if (c < 0x20 || c == '\u007F')
            {
                return false;
            }
        }

        return url.StartsWith('#')
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
    }
}
