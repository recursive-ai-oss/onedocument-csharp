using System.Text;

namespace RecursiveAI.OneDocument.Rtf;

/// <summary>The kind of an <see cref="RtfNode"/>.</summary>
public enum RtfNodeKind
{
    /// <summary>A group: <c>{</c> … <c>}</c>. <see cref="RtfNode.Children"/> holds what is inside.</summary>
    Group,

    /// <summary>A control word such as <c>\par</c> or <c>\fs24</c>.</summary>
    ControlWord,

    /// <summary>A control symbol such as <c>\~</c> or <c>\*</c> (<see cref="RtfNode.Name"/> is the symbol).</summary>
    ControlSymbol,

    /// <summary>A <c>\'hh</c> escape; <see cref="RtfNode.Hex"/> is the byte.</summary>
    Hex,

    /// <summary>A run of text bytes; <see cref="RtfNode.Data"/> holds them.</summary>
    Text,

    /// <summary>The raw bytes after <c>\binN</c>; <see cref="RtfNode.Data"/> holds them.</summary>
    Binary,
}

/// <summary>
/// One node of the group tree of an RTF file. Nodes remember where they came from (<see cref="Offset"/> to <see cref="End"/>) and
/// text and binary nodes point into the source bytes rather than copying them, so the tree is lossless: the source range of any group
/// is exactly its bytes.
/// </summary>
public sealed class RtfNode
{
    private static readonly List<RtfNode> NoChildren = [];

    internal RtfNode(RtfNodeKind kind, int offset)
    {
        Kind = kind;
        Offset = offset;
        Name = string.Empty;
    }

    /// <summary>Gets the kind of node.</summary>
    public RtfNodeKind Kind { get; }

    /// <summary>Gets the offset of the node's first byte in the source.</summary>
    public int Offset { get; internal set; }

    /// <summary>Gets the offset just after the node's last byte (a group's closing brace; a control word's delimiting space).</summary>
    public int End { get; internal set; }

    /// <summary>Gets the control word's letters or the control symbol's character; empty for other kinds.</summary>
    public string Name { get; internal set; }

    /// <summary>Gets a value indicating whether a control word has a numeric parameter.</summary>
    public bool HasParameter { get; internal set; }

    /// <summary>Gets the control word's parameter, or 0.</summary>
    public int Parameter { get; internal set; }

    /// <summary>Gets the byte of a <see cref="RtfNodeKind.Hex"/> node.</summary>
    public byte Hex { get; internal set; }

    /// <summary>Gets the bytes of a text or binary node.</summary>
    public ReadOnlyMemory<byte> Data { get; internal set; }

    /// <summary>Gets the nodes inside a group, in order; empty for other kinds.</summary>
    public IReadOnlyList<RtfNode> Children => ChildList ?? NoChildren;

    /// <summary>Gets a value indicating whether the group is an extension destination: its first node is <c>\*</c>.</summary>
    public bool IsStarred => Kind == RtfNodeKind.Group && ChildList is { Count: > 0 } c && c[0].Kind == RtfNodeKind.ControlSymbol && c[0].Name == "*";

    /// <summary>
    /// Gets the name of the group's destination: its first control word (after <c>\*</c> when starred), or null for a group that
    /// does not start with one. A group that merely starts with a formatting word reports that word.
    /// </summary>
    public string? Destination
    {
        get
        {
            if (ChildList is null)
            {
                return null;
            }

            foreach (RtfNode child in ChildList)
            {
                if (child.Kind == RtfNodeKind.ControlSymbol && child.Name == "*")
                {
                    continue;
                }

                return child.Kind == RtfNodeKind.ControlWord ? child.Name : null;
            }

            return null;
        }
    }

    internal List<RtfNode>? ChildList { get; set; }
}

/// <summary>
/// An RTF file as a tree of groups, control words and text (RTF 1.9.1 "Document Syntax"). The tree is the native, lossless layer: it keeps
/// every token with its offset and keeps unknown destinations as they are, and <see cref="Source"/> is the original bytes.
/// </summary>
public sealed class RtfTree
{
    private RtfTree(ReadOnlyMemory<byte> source, RtfNode root, IReadOnlyList<RtfDefect> defects, int nodeCount)
    {
        Source = source;
        Root = root;
        Defects = defects;
        NodeCount = nodeCount;
    }

    /// <summary>Gets the bytes the tree was read from.</summary>
    public ReadOnlyMemory<byte> Source { get; }

    /// <summary>Gets the outermost group (<c>{\rtf1 …}</c>).</summary>
    public RtfNode Root { get; }

    /// <summary>Gets what the reader repaired: unbalanced braces, bad escapes, a cut-off file.</summary>
    public IReadOnlyList<RtfDefect> Defects { get; }

    /// <summary>Gets the number of nodes in the tree.</summary>
    public int NodeCount { get; }

    /// <summary>Gets the source bytes of a node (a group with its braces).</summary>
    /// <param name="node">A node of this tree.</param>
    /// <returns>The bytes from its first to its last.</returns>
    public ReadOnlyMemory<byte> SourceOf(RtfNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Source.Slice(node.Offset, node.End - node.Offset);
    }

    /// <summary>Gets the default limit on the number of nodes (4,000,000).</summary>
    public const int DefaultMaxNodes = 4_000_000;

    /// <summary>Reads the tree. Never throws for malformed input: damage is listed in <see cref="Defects"/>.</summary>
    /// <param name="data">The RTF bytes; the tree keeps pointing into them, so they must not change.</param>
    /// <param name="options">Group-depth limit; null for the defaults.</param>
    /// <param name="maxNodes">The most nodes to build; reading stops there with <see cref="RtfDefectKind.ContentLimitExceeded"/>.</param>
    /// <returns>The tree. When the input does not start with <c>{\rtf</c> the root is an empty group and <see cref="RtfDefectKind.NotRtf"/> is listed.</returns>
    public static RtfTree Parse(ReadOnlyMemory<byte> data, RtfReaderOptions? options = null, int maxNodes = DefaultMaxNodes)
    {
        var defects = new List<RtfDefect>();
        var root = new RtfNode(RtfNodeKind.Group, 0) { ChildList = [] };
        ReadOnlySpan<byte> span = data.Span;
        int start = 0;
        while (start < span.Length && (span[start] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or 0xEF or 0xBB or 0xBF))
        {
            start++;
        }

        if (!span[start..].StartsWith("{\\rtf"u8))
        {
            defects.Add(new RtfDefect(RtfDefectKind.NotRtf, 0, "the input does not start with {\\rtf"));
            root.End = data.Length;
            return new RtfTree(data, root, defects, 1);
        }

        var stack = new List<RtfNode>();
        var reader = new RtfReader(span[start..], options);
        int count = 0;
        RtfNode? top = null;
        bool done = false;
        while (!done && reader.Read())
        {
            int offset = start + reader.Offset;
            if (++count > maxNodes)
            {
                defects.Add(new RtfDefect(RtfDefectKind.ContentLimitExceeded, offset, "too many nodes; the rest is ignored"));
                break;
            }

            switch (reader.Kind)
            {
                case RtfTokenKind.GroupStart:
                    {
                        var g = new RtfNode(RtfNodeKind.Group, offset) { ChildList = [] };
                        if (top is null)
                        {
                            root = g;
                        }
                        else
                        {
                            top.ChildList!.Add(g);
                        }

                        stack.Add(g);
                        top = g;
                        break;
                    }

                case RtfTokenKind.GroupEnd:
                    if (top is not null)
                    {
                        top.End = start + reader.End;
                        stack.RemoveAt(stack.Count - 1);
                        top = stack.Count > 0 ? stack[^1] : null;
                        if (top is null)
                        {
                            done = true; // bytes after the outermost group (the NULs some writers append) are not part of the document
                        }
                    }

                    break;
                default:
                    if (top is not null)
                    {
                        top.ChildList!.Add(Leaf(in reader, data, start, offset));
                    }

                    break;
            }
        }

        if (!done)
        {
            // Defects of the reader are only complete once it has returned false.
            foreach (RtfNode open in stack)
            {
                open.End = data.Length;
            }
        }

        foreach (RtfDefect d in reader.Defects)
        {
            defects.Add(d with { Offset = d.Offset + start });
        }

        return new RtfTree(data, root, defects, count);
    }

    private static RtfNode Leaf(in RtfReader reader, ReadOnlyMemory<byte> data, int start, int offset)
    {
        RtfNode n;
        switch (reader.Kind)
        {
            case RtfTokenKind.ControlWord:
                n = new RtfNode(RtfNodeKind.ControlWord, offset)
                {
                    Name = Encoding.ASCII.GetString(reader.Name),
                    HasParameter = reader.HasParameter,
                    Parameter = reader.Parameter,
                };
                break;
            case RtfTokenKind.ControlSymbol:
                n = new RtfNode(RtfNodeKind.ControlSymbol, offset) { Name = ((char)(reader.Name.Length > 0 ? reader.Name[0] : (byte)'\\')).ToString() };
                break;
            case RtfTokenKind.HexEscape:
                n = new RtfNode(RtfNodeKind.Hex, offset) { Hex = reader.HexValue };
                break;
            case RtfTokenKind.Binary:
                n = new RtfNode(RtfNodeKind.Binary, offset) { Data = data.Slice(start + reader.Offset, reader.Text.Length) };
                break;
            default:
                n = new RtfNode(RtfNodeKind.Text, offset) { Data = data.Slice(start + reader.Offset, reader.Text.Length) };
                break;
        }

        n.End = start + reader.End;
        return n;
    }
}
