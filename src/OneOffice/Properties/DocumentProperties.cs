using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Properties;

/// <summary>
/// The three property sets of an Office package: the core properties (<c>docProps/core.xml</c>), the extended properties
/// (<c>docProps/app.xml</c>) and the custom properties (<c>docProps/custom.xml</c>). The same parts serve every Office kind.
/// </summary>
/// <remarks>
/// <para>Each part is read when first asked for and is created when first written to, with its relationship and content type. A part
/// nobody edits is not touched, so saving an unedited package keeps it byte-identical. Edits keep the other elements of a part as
/// they are, in order.</para>
/// <para>Nothing here reads the clock: setting a value never changes <see cref="CoreProperties.Modified"/> or another date, the caller
/// sets the dates it wants. A part that cannot be read is reported as <c>props.part-unreadable</c>, reads as empty, and raises that
/// error on a write instead of being overwritten.</para>
/// <para>Not safe for concurrent use.</para>
/// </remarks>
public sealed class DocumentProperties
{
    private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships/";
    private const string DocumentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private readonly PropertyPart _coreStore;
    private readonly PropertyPart _extendedStore;
    private readonly PropertyPart _customStore;

    /// <summary>Opens the property sets of a package. Nothing is read until a property is asked for.</summary>
    /// <param name="package">The package.</param>
    /// <param name="context">The limits and defect sink; null for the defaults.</param>
    public DocumentProperties(IOpcPackage package, OpenContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        context ??= new OpenContext(package.Limits);
        _coreStore = new PropertyPart(package, context, "/metadata/core-properties", PackageRelationships + "metadata/core-properties",
            "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml",
            "<cp:coreProperties xmlns:cp=\"" + CoreProperties.CpNs + "\" xmlns:dc=\"" + CoreProperties.DcNs + "\" xmlns:dcterms=\"" + CoreProperties.DctermsNs
            + "\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"" + CoreProperties.XsiNs + "\"/>",
            CoreProperties.CpNs, "coreProperties");
        _extendedStore = new PropertyPart(package, context, "/extended-properties", DocumentRelationships + "extended-properties",
            "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml",
            "<Properties xmlns=\"" + ExtendedProperties.Ns + "\" xmlns:vt=\"" + CustomProperties.VtNs + "\"/>",
            ExtendedProperties.Ns, "Properties");
        _customStore = new PropertyPart(package, context, "/custom-properties", DocumentRelationships + "custom-properties",
            "/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml",
            "<Properties xmlns=\"" + CustomProperties.Ns + "\" xmlns:vt=\"" + CustomProperties.VtNs + "\"/>",
            CustomProperties.Ns, "Properties");
        Core = new CoreProperties(_coreStore);
        Extended = new ExtendedProperties(_extendedStore);
        Custom = new CustomProperties(_customStore);
    }

    /// <summary>Gets the core properties: title, author, dates and the like.</summary>
    public CoreProperties Core { get; }

    /// <summary>Gets the extended properties: application, company, page and word counts.</summary>
    public ExtendedProperties Extended { get; }

    /// <summary>Gets the custom properties: named, typed values.</summary>
    public CustomProperties Custom { get; }

    /// <summary>Gets a value indicating whether a property part was edited and not yet written to the package.</summary>
    public bool HasUncommittedChanges => _coreStore.IsModified || _extendedStore.IsModified || _customStore.IsModified;

    /// <summary>Writes the edited property parts into the package. Parts nobody edited are left alone.</summary>
    public void Commit()
    {
        _coreStore.Commit();
        _extendedStore.Commit();
        _customStore.Commit();
    }
}

/// <summary>
/// The core properties of a package (ECMA-376 Part 2 §11, <c>docProps/core.xml</c>). Every property is optional: reading gives null
/// for an absent one, and setting null removes the element. Dates read as W3CDTF values (a bare date or year is taken as midnight UTC,
/// a time without a zone as UTC) and are written as UTC to the second.
/// </summary>
public sealed class CoreProperties
{
    internal const string CpNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    internal const string DcNs = "http://purl.org/dc/elements/1.1/";
    internal const string DctermsNs = "http://purl.org/dc/terms/";
    internal const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    private readonly PropertyPart _part;

    internal CoreProperties(PropertyPart part) => _part = part;

    /// <summary>Gets or sets the title (<c>dc:title</c>).</summary>
    public string? Title { get => Text(DcNs, "title"); set => Set(DcNs, "title", "dc", value); }

    /// <summary>Gets or sets the subject (<c>dc:subject</c>).</summary>
    public string? Subject { get => Text(DcNs, "subject"); set => Set(DcNs, "subject", "dc", value); }

    /// <summary>Gets or sets the author (<c>dc:creator</c>).</summary>
    public string? Creator { get => Text(DcNs, "creator"); set => Set(DcNs, "creator", "dc", value); }

    /// <summary>Gets or sets the keywords (<c>cp:keywords</c>).</summary>
    public string? Keywords { get => Text(CpNs, "keywords"); set => Set(CpNs, "keywords", "cp", value); }

    /// <summary>Gets or sets the description, shown as comments by the applications (<c>dc:description</c>).</summary>
    public string? Description { get => Text(DcNs, "description"); set => Set(DcNs, "description", "dc", value); }

    /// <summary>Gets or sets the name of the last person to save (<c>cp:lastModifiedBy</c>).</summary>
    public string? LastModifiedBy { get => Text(CpNs, "lastModifiedBy"); set => Set(CpNs, "lastModifiedBy", "cp", value); }

    /// <summary>Gets or sets the revision number as written (<c>cp:revision</c>, a string in the schema).</summary>
    public string? Revision { get => Text(CpNs, "revision"); set => Set(CpNs, "revision", "cp", value); }

    /// <summary>Gets or sets the category (<c>cp:category</c>).</summary>
    public string? Category { get => Text(CpNs, "category"); set => Set(CpNs, "category", "cp", value); }

    /// <summary>Gets or sets the content status, such as Draft or Final (<c>cp:contentStatus</c>).</summary>
    public string? ContentStatus { get => Text(CpNs, "contentStatus"); set => Set(CpNs, "contentStatus", "cp", value); }

    /// <summary>Gets or sets the language (<c>dc:language</c>).</summary>
    public string? Language { get => Text(DcNs, "language"); set => Set(DcNs, "language", "dc", value); }

    /// <summary>Gets or sets the identifier (<c>dc:identifier</c>).</summary>
    public string? Identifier { get => Text(DcNs, "identifier"); set => Set(DcNs, "identifier", "dc", value); }

    /// <summary>Gets or sets the version string (<c>cp:version</c>).</summary>
    public string? Version { get => Text(CpNs, "version"); set => Set(CpNs, "version", "cp", value); }

    /// <summary>Gets or sets the creation date (<c>dcterms:created</c>); null when absent or not a W3CDTF value.</summary>
    public DateTimeOffset? Created { get => Date(DctermsNs, "created"); set => SetDate(DctermsNs, "created", value); }

    /// <summary>Gets or sets the date of the last save (<c>dcterms:modified</c>). Never set by this library on its own.</summary>
    public DateTimeOffset? Modified { get => Date(DctermsNs, "modified"); set => SetDate(DctermsNs, "modified", value); }

    /// <summary>Gets or sets the date of the last print (<c>cp:lastPrinted</c>).</summary>
    public DateTimeOffset? LastPrinted { get => Date(CpNs, "lastPrinted"); set => SetDate(CpNs, "lastPrinted", value, xsiType: false); }

    private string? Text(string ns, string local)
    {
        MarkupNode e = PropertyPart.Find(_part.Root, ns, local);
        return e.IsNull ? null : e.InnerText;
    }

    private void Set(string ns, string local, string prefix, string? value) => _part.SetChildText(ns, local, prefix, value);

    private DateTimeOffset? Date(string ns, string local) => PropertyPart.ParseDate(Text(ns, local));

    private void SetDate(string ns, string local, DateTimeOffset? value, bool xsiType = true)
    {
        string prefix = ns == DctermsNs ? "dcterms" : "cp";
        string? attributes = null;
        if (value is not null && xsiType && PropertyPart.Find(_part.Root, ns, local).IsNull)
        {
            // ECMA-376 Part 2 §11.1: dcterms dates carry xsi:type="dcterms:W3CDTF".
            MarkupNode root = _part.RootForWrite();
            string xsi = root.TryGetPrefixInScope(XsiNs, out string p) ? p : "xsi";
            string dct = root.TryGetPrefixInScope(DctermsNs, out string q) ? q : "dcterms";
            attributes = (root.TryGetPrefixInScope(XsiNs, out _) ? string.Empty : " xmlns:xsi=\"" + XsiNs + "\"") + " " + xsi + ":type=\"" + dct + ":W3CDTF\"";
        }

        _part.SetChildText(ns, local, prefix, value is null ? null : PropertyPart.FormatDate(value.Value), attributes);
    }
}

/// <summary>
/// The extended (application) properties of a package (<c>docProps/app.xml</c>, ECMA-376 Part 1 §22.2). The typed properties cover
/// the common elements; <see cref="GetValue"/> and <see cref="SetValue"/> reach any other simple element by its name. The structured
/// elements (<c>HeadingPairs</c>, <c>TitlesOfParts</c>, <c>HLinks</c>) are kept as they are.
/// </summary>
public sealed class ExtendedProperties
{
    internal const string Ns = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    private readonly PropertyPart _part;

    internal ExtendedProperties(PropertyPart part) => _part = part;

    /// <summary>Gets or sets the application that last saved the file (<c>Application</c>).</summary>
    public string? Application { get => GetValue("Application"); set => SetValue("Application", value); }

    /// <summary>Gets or sets the application version (<c>AppVersion</c>, <c>XX.YYYY</c>).</summary>
    public string? ApplicationVersion { get => GetValue("AppVersion"); set => SetValue("AppVersion", value); }

    /// <summary>Gets or sets the company (<c>Company</c>).</summary>
    public string? Company { get => GetValue("Company"); set => SetValue("Company", value); }

    /// <summary>Gets or sets the manager (<c>Manager</c>).</summary>
    public string? Manager { get => GetValue("Manager"); set => SetValue("Manager", value); }

    /// <summary>Gets or sets the name of the template the document was made from (<c>Template</c>).</summary>
    public string? Template { get => GetValue("Template"); set => SetValue("Template", value); }

    /// <summary>Gets or sets the base address for relative hyperlinks (<c>HyperlinkBase</c>).</summary>
    public string? HyperlinkBase { get => GetValue("HyperlinkBase"); set => SetValue("HyperlinkBase", value); }

    /// <summary>Gets or sets the page count (<c>Pages</c>). The value is stored as given, never computed.</summary>
    public long? Pages { get => Number("Pages"); set => SetNumber("Pages", value); }

    /// <summary>Gets or sets the word count (<c>Words</c>).</summary>
    public long? Words { get => Number("Words"); set => SetNumber("Words", value); }

    /// <summary>Gets or sets the character count without spaces (<c>Characters</c>).</summary>
    public long? Characters { get => Number("Characters"); set => SetNumber("Characters", value); }

    /// <summary>Gets or sets the character count with spaces (<c>CharactersWithSpaces</c>).</summary>
    public long? CharactersWithSpaces { get => Number("CharactersWithSpaces"); set => SetNumber("CharactersWithSpaces", value); }

    /// <summary>Gets or sets the line count (<c>Lines</c>).</summary>
    public long? Lines { get => Number("Lines"); set => SetNumber("Lines", value); }

    /// <summary>Gets or sets the paragraph count (<c>Paragraphs</c>).</summary>
    public long? Paragraphs { get => Number("Paragraphs"); set => SetNumber("Paragraphs", value); }

    /// <summary>Gets or sets the slide count of a presentation (<c>Slides</c>).</summary>
    public long? Slides { get => Number("Slides"); set => SetNumber("Slides", value); }

    /// <summary>Gets or sets the total editing time in minutes (<c>TotalTime</c>).</summary>
    public long? TotalTimeMinutes { get => Number("TotalTime"); set => SetNumber("TotalTime", value); }

    /// <summary>Gets or sets the document security flags (<c>DocSecurity</c>).</summary>
    public long? DocumentSecurity { get => Number("DocSecurity"); set => SetNumber("DocSecurity", value); }

    /// <summary>Reads a simple child element of <c>Properties</c> by name.</summary>
    /// <param name="element">The local name, such as <c>Company</c>.</param>
    /// <returns>The text, or null when the element is absent.</returns>
    public string? GetValue(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        MarkupNode e = PropertyPart.Find(_part.Root, Ns, element);
        return e.IsNull ? null : e.InnerText;
    }

    /// <summary>Writes a simple child element of <c>Properties</c>, creating or removing it.</summary>
    /// <param name="element">The local name. Only a name made of letters and digits is accepted.</param>
    /// <param name="value">The text; null removes the element.</param>
    /// <exception cref="ArgumentException">The name is not a plain element name, or the text has a character XML cannot carry.</exception>
    /// <exception cref="OfficeFormatException">The part exists and cannot be read (<c>props.part-unreadable</c>).</exception>
    public void SetValue(string element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Length == 0 || !element.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("The element name must be letters and digits.", nameof(element));
        }

        _part.SetChildText(Ns, element, "ep", value);
    }

    private long? Number(string element) =>
        long.TryParse(GetValue(element), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;

    private void SetNumber(string element, long? value) =>
        SetValue(element, value?.ToString(CultureInfo.InvariantCulture));
}

/// <summary>The type of a custom property's value (the <c>vt:</c> element that holds it).</summary>
public enum CustomPropertyType
{
    /// <summary>Text: <c>vt:lpwstr</c> or <c>vt:lpstr</c>.</summary>
    Text = 0,

    /// <summary>A 32-bit or smaller signed integer: <c>vt:i1</c>, <c>i2</c>, <c>i4</c>, <c>int</c>.</summary>
    SmallInteger = 1,

    /// <summary>A 64-bit integer: <c>vt:i8</c>, and the unsigned types that fit.</summary>
    LongInteger = 2,

    /// <summary>A floating-point number: <c>vt:r4</c>, <c>vt:r8</c>.</summary>
    Real = 3,

    /// <summary>A Boolean: <c>vt:bool</c>.</summary>
    Boolean = 4,

    /// <summary>A date and time: <c>vt:filetime</c> or <c>vt:date</c>.</summary>
    DateTime = 5,

    /// <summary>Any other variant type (currency, decimal, blob, vectors, …): kept as written and exposed as text.</summary>
    Other = 6,
}

/// <summary>One custom property.</summary>
/// <param name="Name">The property name.</param>
/// <param name="Type">The kind of value.</param>
/// <param name="Text">The value as written in the file.</param>
/// <param name="PropertyId">The <c>pid</c> that identifies the property inside the part (2 or more).</param>
public readonly record struct CustomProperty(string Name, CustomPropertyType Type, string Text, int PropertyId)
{
    /// <summary>Gets the text of a <see cref="CustomPropertyType.Text"/> value; null for other types.</summary>
    public string? AsString => Type == CustomPropertyType.Text ? Text : null;

    /// <summary>Gets an integer value that fits 32 bits; null otherwise.</summary>
    public int? AsInt32 => Type is CustomPropertyType.SmallInteger or CustomPropertyType.LongInteger
        && int.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    /// <summary>Gets an integer value; null when the value is not an integer.</summary>
    public long? AsInt64 => Type is CustomPropertyType.SmallInteger or CustomPropertyType.LongInteger
        && long.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;

    /// <summary>Gets a numeric value as a double (integers convert); null when the value is not numeric.</summary>
    public double? AsDouble => Type is CustomPropertyType.SmallInteger or CustomPropertyType.LongInteger or CustomPropertyType.Real
        && double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    /// <summary>Gets a Boolean value (<c>true</c>, <c>false</c>, <c>1</c>, <c>0</c>); null when the value is not a Boolean.</summary>
    public bool? AsBoolean => Type == CustomPropertyType.Boolean ? Text.Trim() switch { "true" or "1" => true, "false" or "0" => false, _ => null } : null;

    /// <summary>Gets a date value as UTC; null when the value is not a date or does not parse.</summary>
    public DateTimeOffset? AsDateTime => Type == CustomPropertyType.DateTime ? PropertyPart.ParseDate(Text) : null;
}

/// <summary>
/// The custom properties of a package (<c>docProps/custom.xml</c>, ECMA-376 Part 1 §22.3 and [MS-OSHARED]): named values of a few
/// types. Names compare ignoring case, which is how the applications look them up. A new property gets the next free <c>pid</c>; the
/// ids of the others stay as they are, also after a removal.
/// </summary>
public sealed class CustomProperties
{
    internal const string Ns = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    internal const string VtNs = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

    // [MS-OSHARED] 2.3.3.2.2.1: every custom property uses this format identifier.
    private const string FormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";

    private readonly PropertyPart _part;

    internal CustomProperties(PropertyPart part) => _part = part;

    /// <summary>Lists the properties in file order. An element without a name or a value is skipped.</summary>
    /// <returns>The properties; empty when the part is absent.</returns>
    public IReadOnlyList<CustomProperty> List()
    {
        var list = new List<CustomProperty>();
        foreach (MarkupNode p in Entries())
        {
            if (Read(p) is { } item)
            {
                list.Add(item);
            }
        }

        return list;
    }

    /// <summary>Finds a property by name, ignoring case.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The property, or null.</returns>
    public CustomProperty? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        MarkupNode node = FindNode(name);
        return node.IsNull ? null : Read(node);
    }

    /// <summary>Sets a text value, adding the property or replacing the value (and type) of the existing one.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The text.</param>
    /// <exception cref="ArgumentException">The name is empty or a value has a character XML cannot carry.</exception>
    public void SetString(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        PropertyPart.CheckXmlText(value);
        Write(name, "lpwstr", PropertyPart.Escape(value));
    }

    /// <summary>Sets a 32-bit integer value (<c>vt:i4</c>).</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public void SetInt32(string name, int value) => Write(name, "i4", value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Sets a 64-bit integer value (<c>vt:i8</c>).</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public void SetInt64(string name, long value) => Write(name, "i8", value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Sets a floating-point value (<c>vt:r8</c>), written with round-trip precision.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value; not NaN or infinite.</param>
    /// <exception cref="ArgumentException">The value is NaN or infinite.</exception>
    public void SetDouble(string name, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("The value must be finite.", nameof(value));
        }

        Write(name, "r8", value.ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>Sets a Boolean value (<c>vt:bool</c>).</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public void SetBoolean(string name, bool value) => Write(name, "bool", value ? "true" : "false");

    /// <summary>Sets a date value (<c>vt:filetime</c>), written as UTC to the second.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The date.</param>
    public void SetDateTime(string name, DateTimeOffset value) => Write(name, "filetime", PropertyPart.FormatDate(value));

    /// <summary>Removes a property by name, ignoring case.</summary>
    /// <param name="name">The name.</param>
    /// <returns>True when a property was removed.</returns>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        MarkupNode node = FindNode(name);
        if (node.IsNull)
        {
            return false;
        }

        _part.RootForWrite();
        node.Remove();
        return true;
    }

    private IEnumerable<MarkupNode> Entries()
    {
        MarkupNode root = _part.Root;
        if (root.IsNull)
        {
            yield break;
        }

        for (MarkupNode n = root.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.IsElement(Ns, "property"))
            {
                yield return n;
            }
        }
    }

    private MarkupNode FindNode(string name)
    {
        foreach (MarkupNode p in Entries())
        {
            if (string.Equals(p.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return default;
    }

    private static CustomProperty? Read(MarkupNode property)
    {
        if (property.GetAttribute("name") is not { } name)
        {
            return null;
        }

        int.TryParse(property.GetAttribute("pid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid);
        for (MarkupNode v = property.FirstChild; !v.IsNull; v = v.NextSibling)
        {
            if (v.Kind != MarkupNodeKind.Element || v.NamespaceUri != VtNs)
            {
                continue;
            }

            CustomPropertyType type = v.LocalNameString switch
            {
                "lpwstr" or "lpstr" or "bstr" => CustomPropertyType.Text,
                "i1" or "i2" or "i4" or "int" or "ui1" or "ui2" => CustomPropertyType.SmallInteger,
                "i8" or "ui4" or "uint" => CustomPropertyType.LongInteger,
                "r4" or "r8" => CustomPropertyType.Real,
                "bool" => CustomPropertyType.Boolean,
                "filetime" or "date" => CustomPropertyType.DateTime,
                _ => CustomPropertyType.Other,
            };
            return new CustomProperty(name, type, v.InnerText, pid);
        }

        return null;
    }

    private void Write(string name, string vtElement, string escapedValue)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
        {
            throw new ArgumentException("The name must not be empty.", nameof(name));
        }

        PropertyPart.CheckXmlText(name);
        MarkupNode root = _part.RootForWrite();
        MarkupNode existing = FindNode(name);
        int pid;
        string writtenName = name;
        if (!existing.IsNull)
        {
            int.TryParse(existing.GetAttribute("pid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
            writtenName = existing.GetAttribute("name") ?? name;
        }
        else
        {
            pid = 1;
            foreach (MarkupNode p in Entries())
            {
                if (int.TryParse(p.GetAttribute("pid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int other) && other > pid)
                {
                    pid = other;
                }
            }

            pid++;
        }

        string property = PropertyPart.Qualified(root, Ns, "property", "cp", out string declaration);
        string vt = root.TryGetPrefixInScope(VtNs, out string vtPrefix) ? vtPrefix : "vt";
        string vtDeclaration = root.TryGetPrefixInScope(VtNs, out _) ? string.Empty : " xmlns:vt=\"" + VtNs + "\"";
        string xml = "<" + property + declaration + vtDeclaration + " fmtid=\"" + FormatId + "\" pid=\"" + pid.ToString(CultureInfo.InvariantCulture)
            + "\" name=\"" + PropertyPart.Escape(writtenName) + "\"><" + vt + ":" + vtElement + ">" + escapedValue + "</" + vt + ":" + vtElement + "></" + property + ">";
        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        if (existing.IsNull)
        {
            root.AppendXml(bytes);
        }
        else
        {
            existing.ReplaceWithXml(bytes);
        }
    }
}
