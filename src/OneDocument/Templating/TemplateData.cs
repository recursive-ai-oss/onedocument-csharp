using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.Json;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Templating;

/// <summary>What a node of template data is.</summary>
public enum TemplateDataKind
{
    /// <summary>Nothing: a JSON <c>null</c>, a null reference, a database null. Renders empty and is false in a condition.</summary>
    Null = 0,

    /// <summary>A number, text, boolean or date.</summary>
    Scalar = 1,

    /// <summary>A record with named members (a POCO, a dictionary, a JSON object, a data row, an XML element).</summary>
    Record = 2,

    /// <summary>An ordered collection of nodes (a list, an array, a JSON array, a data table, repeated XML elements).</summary>
    Sequence = 3,
}

/// <summary>
/// One node of the data a template reads. The engine only ever sees data through this interface, so every source (POCOs, dictionaries,
/// JSON, data tables, XML) is adapted once, without reflection. Implementations are read-only after construction.
/// </summary>
public interface ITemplateData
{
    /// <summary>What this node is.</summary>
    TemplateDataKind Kind { get; }

    /// <summary>The value of a <see cref="TemplateDataKind.Scalar"/> node as the calc engine sees it (a date is its serial number); blank for other kinds.</summary>
    CalcValue Value { get; }

    /// <summary>The value as the original object for a scalar (a <see cref="DateTime"/>, a <c>byte[]</c> for an image tag, a string); null for other kinds.</summary>
    object? Raw { get; }

    /// <summary>Finds a member of an <see cref="TemplateDataKind.Record"/> node. An exact (ordinal) name wins; otherwise the first member whose name matches ignoring case.</summary>
    /// <param name="name">The member name.</param>
    /// <param name="value">The member, or null.</param>
    /// <returns>True when the member exists (its value may be a <see cref="TemplateDataKind.Null"/> node).</returns>
    bool TryGetMember(string name, out ITemplateData? value);

    /// <summary>The items of a <see cref="TemplateDataKind.Sequence"/> node, in order; empty for other kinds.</summary>
    IReadOnlyList<ITemplateData> Items { get; }
}

/// <summary>
/// Implemented by source-generated code for classes marked <see cref="TemplateModelAttribute"/>: it hands out members by name without
/// reflection. Write nothing against it by hand unless you need a custom shape.
/// </summary>
public interface ITemplateObject
{
    /// <summary>Reads a member by name.</summary>
    /// <param name="name">The member name; the generated code matches the exact spelling first, then ignoring case.</param>
    /// <param name="value">The member's value (a scalar, another template object, a collection, or null).</param>
    /// <returns>True when the type has such a member.</returns>
    bool TryGetTemplateMember(string name, out object? value);
}

/// <summary>Marks a partial class, struct or record so a source generator gives it an <see cref="ITemplateObject"/> implementation: all public readable instance properties and fields become members.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class TemplateModelAttribute : Attribute
{
}

/// <summary>Adapters from the data a caller has to <see cref="ITemplateData"/>.</summary>
public static class TemplateData
{
    /// <summary>The null node.</summary>
    public static ITemplateData Null { get; } = new NullData();

    /// <summary>
    /// Wraps a value. Understood: null; text, booleans, every numeric type, <see cref="DateTime"/>, <see cref="DateOnly"/>, <see cref="TimeOnly"/>,
    /// <see cref="DateTimeOffset"/>, <see cref="TimeSpan"/>, <see cref="Guid"/>, enums (their name) and <c>byte[]</c>; <see cref="ITemplateData"/> itself;
    /// <see cref="ITemplateObject"/> (generated); <see cref="JsonElement"/> and <see cref="JsonDocument"/>; <see cref="DataTable"/>, <see cref="DataRow"/>,
    /// <see cref="DataRowView"/> and <see cref="IDataReader"/> (read to the end, at most <paramref name="maxRows"/> rows); dictionaries with text keys
    /// (generic or not); any other <see cref="IEnumerable"/> as a sequence. Anything else is an error: a class without a generated model has no members the
    /// engine may read, because reading them would need reflection.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="maxRows">The most rows read from a data reader or an unbounded enumerable.</param>
    /// <returns>The node.</returns>
    /// <exception cref="OfficeUnsupportedException">The value is of a type the engine cannot read without reflection (mark it <see cref="TemplateModelAttribute"/>).</exception>
    /// <exception cref="OfficeLimitException">More than <paramref name="maxRows"/> rows or items.</exception>
    public static ITemplateData From(object? value, int maxRows = 1_000_000) => Wrap(value, maxRows, 0);

    /// <summary>Wraps a JSON document's root.</summary>
    /// <param name="json">The document; it must stay alive while the data is used.</param>
    /// <returns>The node.</returns>
    public static ITemplateData FromJson(JsonDocument json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return new JsonData(json.RootElement);
    }

    /// <summary>
    /// Reads XML text into a node with the library's own tokenizer (no BCL XML classes, no DTDs). An element becomes an object whose members are its
    /// attributes (by name), its child elements (by local name; a repeated name becomes a sequence) and, when it has no child elements, a member <c>#text</c>;
    /// an element with only text and no attributes is a text scalar. The root element's own name is not a member: its children are.
    /// </summary>
    /// <param name="utf8Xml">The document.</param>
    /// <param name="limits">Limits for depth and size, or null for the defaults.</param>
    /// <returns>The root element as an object node.</returns>
    /// <exception cref="OfficeFormatException">The XML is malformed.</exception>
    public static ITemplateData FromXml(ReadOnlyMemory<byte> utf8Xml, OfficeLimits? limits = null) => XmlData.Read(utf8Xml, limits ?? OfficeLimits.Default);

    /// <summary>Builds an object node from names and nodes (the shape template functions and binders use for synthetic records).</summary>
    /// <param name="members">The members; later names replace earlier ones with the same spelling.</param>
    /// <returns>The node.</returns>
    public static ITemplateData Record(IEnumerable<KeyValuePair<string, ITemplateData>> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var map = new Dictionary<string, ITemplateData>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ITemplateData> kv in members)
        {
            map[kv.Key] = kv.Value;
        }

        return new MapData(map);
    }

    /// <summary>Builds a sequence node.</summary>
    /// <param name="items">The items.</param>
    /// <returns>The node.</returns>
    public static ITemplateData Sequence(IEnumerable<ITemplateData> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new SequenceData([.. items]);
    }

    /// <summary>Builds a scalar node.</summary>
    /// <param name="value">A calc value (not an array or reference).</param>
    /// <returns>The node.</returns>
    public static ITemplateData Scalar(CalcValue value) => new ScalarData(value, null, null);

    private static ITemplateData Wrap(object? value, int maxRows, int depth)
    {
        if (depth > 64)
        {
            throw new OfficeLimitException(nameof(OfficeLimits.MaxRecursionDepth), 64, depth);
        }

        switch (value)
        {
            case null:
            case DBNull:
                return Null;
            case ITemplateData d:
                return d;
            case string s:
                return new ScalarData(CalcValue.FromText(s), s, null);
            case bool b:
                return new ScalarData(CalcValue.FromBoolean(b), b, null);
            case char ch:
                return new ScalarData(CalcValue.FromText(ch.ToString()), ch, null);
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double:
                return new ScalarData(CalcValue.FromNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture)), value, null);
            case decimal m:
                return new ScalarData(CalcValue.FromNumber(Convert.ToDouble(m, CultureInfo.InvariantCulture)), m, null);
            case DateTime dt:
                return DateScalar(dt, dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), dt);
            case DateTimeOffset dto:
                return DateScalar(dto.DateTime, dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), dto);
            case DateOnly dateOnly:
                return DateScalar(dateOnly.ToDateTime(TimeOnly.MinValue), dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dateOnly);
            case TimeOnly t:
                return new ScalarData(CalcValue.FromNumber(t.Ticks / (double)TimeSpan.TicksPerDay), t, t.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            case TimeSpan ts:
                return new ScalarData(CalcValue.FromNumber(ts.Ticks / (double)TimeSpan.TicksPerDay), ts, ts.ToString("c", CultureInfo.InvariantCulture));
            case Guid g:
                return new ScalarData(CalcValue.FromText(g.ToString("D", CultureInfo.InvariantCulture)), g, null);
            case Enum e:
                return new ScalarData(CalcValue.FromText(e.ToString()), e, null);
            case byte[] bytes:
                return new ScalarData(CalcValue.FromText(string.Empty), bytes, string.Empty);
            case ITemplateObject o:
                return new ObjectData(o, maxRows, depth);
            case JsonDocument jd:
                return new JsonData(jd.RootElement);
            case JsonElement je:
                return new JsonData(je);
            case DataTable table:
                return TableSequence(table, maxRows, depth);
            case DataRow row:
                return RowObject(row, maxRows, depth);
            case DataRowView view:
                return RowObject(view.Row, maxRows, depth);
            case IDataReader reader:
                return ReaderSequence(reader, maxRows, depth);
            case IDictionary dictionary:
                {
                    var map = new Dictionary<string, ITemplateData>(StringComparer.Ordinal);
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        map[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = Wrap(entry.Value, maxRows, depth + 1);
                    }

                    return new MapData(map);
                }

            case IEnumerable sequence:
                {
                    var items = new List<ITemplateData>();
                    foreach (object? item in sequence)
                    {
                        if (items.Count >= maxRows)
                        {
                            throw new OfficeLimitException("MaxRows", maxRows, items.Count + 1);
                        }

                        items.Add(Wrap(item, maxRows, depth + 1));
                    }

                    return new SequenceData(items);
                }

            default:
                throw new OfficeUnsupportedException(
                    "Template data of type " + value.GetType().FullName + " (mark the class [TemplateModel] and make it partial, or pass a dictionary)");
        }
    }

    private static ScalarData DateScalar(DateTime date, string display, object raw)
    {
        if (!RecursiveAI.OneDocument.NumberFormats.ExcelDate.TryToSerial(date, RecursiveAI.OneDocument.NumberFormats.DateSystem.Date1900, out double serial))
        {
            return new ScalarData(CalcValue.FromText(display), raw, display);
        }

        return new ScalarData(CalcValue.FromNumber(serial), raw, display);
    }

    private static SequenceData TableSequence(DataTable table, int maxRows, int depth)
    {
        if (table.Rows.Count > maxRows)
        {
            throw new OfficeLimitException("MaxRows", maxRows, table.Rows.Count);
        }

        var items = new List<ITemplateData>(table.Rows.Count);
        foreach (DataRow row in table.Rows)
        {
            items.Add(RowObject(row, maxRows, depth));
        }

        return new SequenceData(items);
    }

    private static MapData RowObject(DataRow row, int maxRows, int depth)
    {
        var map = new Dictionary<string, ITemplateData>(StringComparer.Ordinal);
        foreach (DataColumn column in row.Table.Columns)
        {
            map[column.ColumnName] = Wrap(row[column], maxRows, depth + 1);
        }

        return new MapData(map);
    }

    private static SequenceData ReaderSequence(IDataReader reader, int maxRows, int depth)
    {
        var items = new List<ITemplateData>();
        while (reader.Read())
        {
            if (items.Count >= maxRows)
            {
                throw new OfficeLimitException("MaxRows", maxRows, items.Count + 1);
            }

            var map = new Dictionary<string, ITemplateData>(StringComparer.Ordinal);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                map[reader.GetName(i)] = Wrap(reader.IsDBNull(i) ? null : reader.GetValue(i), maxRows, depth + 1);
            }

            items.Add(new MapData(map));
        }

        return new SequenceData(items);
    }

    // ---- nodes ---------------------------------------------------------------------------------------------------------------

    private sealed class NullData : ITemplateData
    {
        public TemplateDataKind Kind => TemplateDataKind.Null;

        public CalcValue Value => CalcValue.Blank;

        public object? Raw => null;

        public IReadOnlyList<ITemplateData> Items => [];

        public bool TryGetMember(string name, out ITemplateData? value)
        {
            value = null;
            return false;
        }
    }

    internal sealed class ScalarData(CalcValue value, object? raw, string? display) : ITemplateData
    {
        public TemplateDataKind Kind => TemplateDataKind.Scalar;

        public CalcValue Value => value;

        public object? Raw => raw;

        /// <summary>The text a bare <c>{{ path }}</c> prints when it differs from the calc rendering (dates, times).</summary>
        public string? Display => display;

        public IReadOnlyList<ITemplateData> Items => [];

        public bool TryGetMember(string name, out ITemplateData? member)
        {
            member = null;
            return false;
        }
    }

    internal sealed class SequenceData(List<ITemplateData> items) : ITemplateData
    {
        public TemplateDataKind Kind => TemplateDataKind.Sequence;

        public CalcValue Value => CalcValue.Blank;

        public object? Raw => null;

        public IReadOnlyList<ITemplateData> Items => items;

        public bool TryGetMember(string name, out ITemplateData? member)
        {
            member = null;
            return false;
        }
    }

    internal sealed class MapData(Dictionary<string, ITemplateData> map) : ITemplateData
    {
        public TemplateDataKind Kind => TemplateDataKind.Record;

        public CalcValue Value => CalcValue.Blank;

        public object? Raw => null;

        public IReadOnlyList<ITemplateData> Items => [];

        public bool TryGetMember(string name, out ITemplateData? member)
        {
            if (map.TryGetValue(name, out member))
            {
                return true;
            }

            foreach (KeyValuePair<string, ITemplateData> kv in map)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    member = kv.Value;
                    return true;
                }
            }

            member = null;
            return false;
        }
    }

    private sealed class ObjectData(ITemplateObject source, int maxRows, int depth) : ITemplateData
    {
        public TemplateDataKind Kind => TemplateDataKind.Record;

        public CalcValue Value => CalcValue.Blank;

        public object? Raw => null;

        public IReadOnlyList<ITemplateData> Items => [];

        public bool TryGetMember(string name, out ITemplateData? member)
        {
            if (source.TryGetTemplateMember(name, out object? raw))
            {
                member = Wrap(raw, maxRows, depth + 1);
                return true;
            }

            member = null;
            return false;
        }
    }

    private sealed class JsonData(JsonElement element) : ITemplateData
    {
        public TemplateDataKind Kind => element.ValueKind switch
        {
            JsonValueKind.Object => TemplateDataKind.Record,
            JsonValueKind.Array => TemplateDataKind.Sequence,
            JsonValueKind.Null or JsonValueKind.Undefined => TemplateDataKind.Null,
            _ => TemplateDataKind.Scalar,
        };

        public CalcValue Value => element.ValueKind switch
        {
            JsonValueKind.String => CalcValue.FromText(element.GetString()),
            JsonValueKind.Number => CalcValue.FromNumber(element.TryGetDouble(out double d) && double.IsFinite(d) ? d : 0),
            JsonValueKind.True => CalcValue.True,
            JsonValueKind.False => CalcValue.False,
            _ => CalcValue.Blank,
        };

        public object? Raw => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetDouble(out double d) ? d : null,
            _ => null,
        };

        public IReadOnlyList<ITemplateData> Items
        {
            get
            {
                if (element.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                var list = new List<ITemplateData>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    list.Add(new JsonData(item));
                }

                return list;
            }
        }

        public bool TryGetMember(string name, out ITemplateData? member)
        {
            member = null;
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (element.TryGetProperty(name, out JsonElement exact))
            {
                member = new JsonData(exact);
                return true;
            }

            foreach (JsonProperty p in element.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    member = new JsonData(p.Value);
                    return true;
                }
            }

            return false;
        }
    }
}
