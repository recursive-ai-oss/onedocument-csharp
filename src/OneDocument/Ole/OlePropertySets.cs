using System.Collections.ObjectModel;

namespace RecursiveAI.OneDocument.Ole;

/// <summary>One property of a property set: its identifier and typed value.</summary>
/// <param name="Id">The property identifier ([MS-OLEPS] §2.1): 2 to 0x7FFFFFFF for a normal property.</param>
/// <param name="Value">The value.</param>
public sealed record OleProperty(uint Id, OlePropertyValue Value);

/// <summary>
/// One property set of a property set stream ([MS-OLEPS] §2.20): the format identifier, the code page that string values use, the
/// optional dictionary of property names, and the properties.
/// </summary>
/// <remarks>
/// The special properties of §2.18 (dictionary, code page, locale, behavior) are not in <see cref="Properties"/>; they are the
/// <see cref="Names"/>, <see cref="CodePage"/>, <see cref="Locale"/> and <see cref="Behavior"/> members. Not safe for concurrent mutation.
/// </remarks>
public sealed class OlePropertySet
{
    /// <summary>The identifier of the dictionary property ([MS-OLEPS] §2.18.1).</summary>
    public const uint DictionaryId = 0x00000000;

    /// <summary>The identifier of the code page property ([MS-OLEPS] §2.18.2).</summary>
    public const uint CodePageId = 0x00000001;

    /// <summary>The identifier of the locale property ([MS-OLEPS] §2.18.3).</summary>
    public const uint LocaleId = 0x80000000;

    /// <summary>The identifier of the behavior property ([MS-OLEPS] §2.18.4).</summary>
    public const uint BehaviorId = 0x80000003;

    /// <summary>The code page value that means strings are stored as UTF-16 (<c>CP_WINUNICODE</c>).</summary>
    public const int UnicodeCodePage = 1200;

    private readonly List<OleProperty> _properties = [];
    private readonly Dictionary<uint, string> _names = [];

    /// <summary>Creates an empty property set.</summary>
    /// <param name="formatId">The format identifier (FMTID).</param>
    public OlePropertySet(Guid formatId)
    {
        FormatId = formatId;
        Properties = new ReadOnlyCollection<OleProperty>(_properties);
    }

    /// <summary>Gets the format identifier (FMTID) that says what the properties mean.</summary>
    public Guid FormatId { get; }

    /// <summary>
    /// Gets or sets the code page the string values are stored in: 1200 (the default) for UTF-16, otherwise a Windows code page such as
    /// 1252. Reading takes it from the code page property, or reads 1252 when the set has none.
    /// </summary>
    public int CodePage { get; set; } = UnicodeCodePage;

    /// <summary>Gets or sets the locale identifier property, or null when the set has none.</summary>
    public uint? Locale { get; set; }

    /// <summary>Gets or sets the behavior property (1: names compare case-sensitively), or null when the set has none.</summary>
    public uint? Behavior { get; set; }

    /// <summary>Gets a value indicating whether the stored set had a code page property. A set made in memory has none until it is written.</summary>
    public bool HasCodePageProperty { get; internal set; }

    /// <summary>Gets the properties in stored order, without the special properties.</summary>
    public IReadOnlyList<OleProperty> Properties { get; }

    /// <summary>Gets the dictionary: property identifier to property name, for the properties that have names (user-defined properties).</summary>
    public IDictionary<uint, string> Names => _names;

    /// <summary>Finds a property by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The value, or null when the set has no such property.</returns>
    public OlePropertyValue? Find(uint id)
    {
        foreach (OleProperty p in _properties)
        {
            if (p.Id == id)
            {
                return p.Value;
            }
        }

        return null;
    }

    /// <summary>Finds a property by name. Names compare ignoring case unless <see cref="Behavior"/> is 1.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The property, or null.</returns>
    public OleProperty? FindByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        StringComparison cmp = Behavior == 1 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (KeyValuePair<uint, string> n in _names)
        {
            if (string.Equals(n.Value, name, cmp) && Find(n.Key) is { } v)
            {
                return new OleProperty(n.Key, v);
            }
        }

        return null;
    }

    /// <summary>Sets a property, replacing one with the same identifier or adding it at the end.</summary>
    /// <param name="id">The identifier, 2 to 0x7FFFFFFF.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentOutOfRangeException">The identifier is one of the special properties or above 0x7FFFFFFF.</exception>
    public void Set(uint id, OlePropertyValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (id < 2 || id > 0x7FFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "A normal property identifier is 2 to 0x7FFFFFFF; the code page, locale, behavior and dictionary have their own members.");
        }

        AddOrReplace(id, value);
    }

    /// <summary>Sets a named property: gives the name an identifier (the next free one at 2 or above, or the existing one) and sets the value.</summary>
    /// <param name="name">The name; not empty.</param>
    /// <param name="value">The value.</param>
    /// <returns>The identifier used.</returns>
    public uint SetNamed(string name, OlePropertyValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        if (FindByName(name) is { } existing)
        {
            AddOrReplace(existing.Id, value);
            return existing.Id;
        }

        uint id = 2;
        while (_names.ContainsKey(id) || Find(id) is not null)
        {
            id++;
        }

        _names[id] = name;
        AddOrReplace(id, value);
        return id;
    }

    /// <summary>Removes a property, and its name if it has one.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>True if a property was removed.</returns>
    public bool Remove(uint id)
    {
        _names.Remove(id);
        return _properties.RemoveAll(p => p.Id == id) > 0;
    }

    internal void AddOrReplace(uint id, OlePropertyValue value)
    {
        for (int i = 0; i < _properties.Count; i++)
        {
            if (_properties[i].Id == id)
            {
                _properties[i] = new OleProperty(id, value);
                return;
            }
        }

        _properties.Add(new OleProperty(id, value));
    }
}

/// <summary>
/// A property set stream ([MS-OLEPS] §2.21): the content of <c>\u0005SummaryInformation</c>, <c>\u0005DocumentSummaryInformation</c> or any
/// other property set stream, as one or two property sets.
/// </summary>
/// <remarks>
/// <para><see cref="Read"/> and <see cref="TryRead"/> decode a stream; <see cref="ToArray"/> and <see cref="WriteTo"/> encode it. Reading
/// is tolerant: a damaged property is dropped with a defect (<c>ole.propset-*</c>, see the defect catalogue) and the others are kept.
/// A stream whose header is unusable raises <see cref="OfficeFormatException"/> (<c>TryRead</c> reports it instead).</para>
/// <para>Writing gives a version 0 stream for a set whose strings are all in the ANSI code page of the set or UTF-16 and a version 1 stream
/// when <see cref="Version"/> says so; a stream read and written again reads back to the same values.</para>
/// </remarks>
public sealed partial class OlePropertySets
{
    /// <summary>The format identifier of the SummaryInformation property set ([MS-OLEPS] §2.25.1).</summary>
    public static readonly Guid SummaryInformationFormatId = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9");

    /// <summary>The format identifier of the DocumentSummaryInformation property set ([MS-OLEPS] §2.21).</summary>
    public static readonly Guid DocumentSummaryInformationFormatId = new("D5CDD502-2E9C-101B-9397-08002B2CF9AE");

    /// <summary>The format identifier of the user-defined properties set that follows the DocumentSummaryInformation set ([MS-OLEPS] §2.21).</summary>
    public static readonly Guid UserDefinedPropertiesFormatId = new("D5CDD505-2E9C-101B-9397-08002B2CF9AE");

    /// <summary>The name of the SummaryInformation stream in a compound file.</summary>
    public const string SummaryInformationStreamName = "\u0005SummaryInformation";

    /// <summary>The name of the DocumentSummaryInformation stream in a compound file.</summary>
    public const string DocumentSummaryInformationStreamName = "\u0005DocumentSummaryInformation";

    private readonly List<OlePropertySet> _sets = [];

    /// <summary>Creates an empty stream.</summary>
    public OlePropertySets()
    {
        Sets = new ReadOnlyCollection<OlePropertySet>(_sets);
    }

    /// <summary>Gets or sets the version: 0, or 1 for property sets that use the version 1 types. The default is 0.</summary>
    public ushort Version { get; set; }

    /// <summary>Gets or sets the system identifier: the operating system version that wrote the stream. The default is 0x00020006, what Office writes.</summary>
    public uint SystemIdentifier { get; set; } = 0x00020006;

    /// <summary>Gets or sets the class identifier of the stream's owner; <see cref="Guid.Empty"/> when it has none.</summary>
    public Guid ClassId { get; set; }

    /// <summary>Gets the property sets in stored order: one, or two for a document summary stream that carries the user-defined properties.</summary>
    public IReadOnlyList<OlePropertySet> Sets { get; }

    /// <summary>Adds a property set at the end.</summary>
    /// <param name="set">The set.</param>
    public void Add(OlePropertySet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _sets.Add(set);
    }

    /// <summary>Finds the first property set with a format identifier.</summary>
    /// <param name="formatId">The FMTID.</param>
    /// <returns>The set, or null.</returns>
    public OlePropertySet? FindSet(Guid formatId)
    {
        foreach (OlePropertySet s in _sets)
        {
            if (s.FormatId == formatId)
            {
                return s;
            }
        }

        return null;
    }
}
