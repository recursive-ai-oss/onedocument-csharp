using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.OfficeArt;

/// <summary>A rectangle as OfficeArt and the host formats store it. Units depend on where it came from (master units, EMU, group coordinates…).</summary>
/// <param name="Left">The minimum x.</param>
/// <param name="Top">The minimum y.</param>
/// <param name="Right">The maximum x.</param>
/// <param name="Bottom">The maximum y.</param>
public readonly record struct ArtRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Right minus left.</summary>
    public long Width => (long)Right - Left;

    /// <summary>Bottom minus top.</summary>
    public long Height => (long)Bottom - Top;
}

/// <summary>Flags of the shape header (<c>OfficeArtFSP</c>, [MS-ODRAW] §2.2.40).</summary>
[Flags]
public enum ArtShapeAttributes
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The shape is a group.</summary>
    Group = 0x1,

    /// <summary>The shape is a child of a group.</summary>
    Child = 0x2,

    /// <summary>The shape is the topmost group of the drawing.</summary>
    Patriarch = 0x4,

    /// <summary>The shape has been deleted.</summary>
    Deleted = 0x8,

    /// <summary>The shape is an OLE object.</summary>
    OleShape = 0x10,

    /// <summary>The shape has a master shape.</summary>
    HaveMaster = 0x20,

    /// <summary>Flipped horizontally.</summary>
    FlipH = 0x40,

    /// <summary>Flipped vertically.</summary>
    FlipV = 0x80,

    /// <summary>The shape is a connector.</summary>
    Connector = 0x100,

    /// <summary>The shape has an anchor.</summary>
    HaveAnchor = 0x200,

    /// <summary>The shape is the background of the drawing.</summary>
    Background = 0x400,

    /// <summary>The shape has a shape type.</summary>
    HaveShapeType = 0x800,
}

/// <summary>
/// One shape of a drawing: the header, the merged property tables, the anchors and the host-defined records, with groups holding their children.
/// Every <see cref="ArtRecord"/> kept here points into the parsed buffer, so unknown records survive for a writer or a host-specific reader.
/// </summary>
public sealed class ArtShape
{
    internal ArtShape(ArtRecord record)
    {
        Record = record;
    }

    /// <summary>The shape container.</summary>
    public ArtRecord Record { get; }

    /// <summary>The shape identifier (<c>spid</c>), or 0 when the header is missing.</summary>
    public int ShapeId { get; internal set; }

    /// <summary>The shape type (<c>MSOSPT</c> in the header's instance field, [MS-ODRAW] §2.4.5).</summary>
    public int ShapeType { get; internal set; }

    /// <summary>The header flags.</summary>
    public ArtShapeAttributes Flags { get; internal set; }

    /// <summary>True when this shape is a group (its children are in <see cref="Children"/>).</summary>
    public bool IsGroup => (Flags & ArtShapeAttributes.Group) != 0;

    /// <summary>The merged primary, secondary and tertiary property tables.</summary>
    public ArtProperties Properties { get; internal set; } = ArtProperties.Empty;

    /// <summary>The coordinate space children of a group are placed in (<c>OfficeArtFSPGR</c>), or null.</summary>
    public ArtRect? GroupSpace { get; internal set; }

    /// <summary>The position of the shape inside its parent group (<c>OfficeArtChildAnchor</c>), or null.</summary>
    public ArtRect? ChildAnchor { get; internal set; }

    /// <summary>The host-defined anchor record, or null. Its layout belongs to the host format.</summary>
    public ArtRecord? ClientAnchor { get; internal set; }

    /// <summary>The host-defined data container, or null.</summary>
    public ArtRecord? ClientData { get; internal set; }

    /// <summary>The host-defined text box record, or null. For the PowerPoint format its payload is a sequence of PowerPoint records.</summary>
    public ArtRecord? ClientTextbox { get; internal set; }

    /// <summary>Every record of the container in order, including those with no member here.</summary>
    public IReadOnlyList<ArtRecord> Records => Record.Children;

    /// <summary>The child shapes of a group in z-order (back to front); empty for other shapes.</summary>
    public IReadOnlyList<ArtShape> Children { get; internal set; } = [];
}

/// <summary>One drawing: a page, slide, sheet or text-box story (<c>OfficeArtDgContainer</c>, [MS-ODRAW] §2.2.13).</summary>
public sealed class ArtDrawing
{
    internal ArtDrawing(ArtRecord record)
    {
        Record = record;
    }

    /// <summary>The drawing container.</summary>
    public ArtRecord Record { get; }

    /// <summary>The drawing identifier (<c>OfficeArtFDG</c> instance).</summary>
    public int DrawingId { get; internal set; }

    /// <summary>The topmost group (the patriarch), whose children are the drawing's shapes; null when the drawing has no shape group.</summary>
    public ArtShape? Root { get; internal set; }

    /// <summary>The background shape (a shape container after the group), carrying the fill of the page, or null.</summary>
    public ArtShape? Background { get; internal set; }

    /// <summary>The shapes at the top level in z-order (empty when there is no group).</summary>
    public IReadOnlyList<ArtShape> Shapes => Root?.Children ?? [];

    /// <summary>
    /// Parses a drawing container. Shapes whose records are damaged are kept as far as they parsed; nothing here throws for malformed content
    /// except the limit exceptions of <see cref="ArtRecords.Parse"/> and a nesting depth over <see cref="OfficeLimits.MaxRecursionDepth"/>.
    /// </summary>
    /// <param name="dg">An <c>OfficeArtDgContainer</c> record.</param>
    /// <param name="context">Limits and defect sink.</param>
    /// <returns>The drawing.</returns>
    /// <exception cref="OfficeLimitException">Groups nest deeper than the recursion limit.</exception>
    public static ArtDrawing Parse(ArtRecord dg, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(dg);
        ArgumentNullException.ThrowIfNull(context);
        var drawing = new ArtDrawing(dg);
        ArtRecord? fdg = dg.Find(ArtRecordType.Fdg);
        if (fdg is not null)
        {
            drawing.DrawingId = fdg.Header.Instance;
        }

        foreach (ArtRecord child in dg.Children)
        {
            if (child.Type == ArtRecordType.SpgrContainer && drawing.Root is null)
            {
                drawing.Root = ParseGroup(child, context, 0);
            }
            else if (child.Type == ArtRecordType.SpContainer && drawing.Background is null)
            {
                drawing.Background = ParseShape(child, context);
            }
        }

        return drawing;
    }

    // [MS-ODRAW] §2.2.16: an OfficeArtSpgrContainer holds OfficeArtSpgrContainerFileBlock records, each a shape container or a nested group
    // container. The first shape container is the group's own shape, carrying its FSPGR and anchor.
    internal static ArtShape ParseGroup(ArtRecord group, ArtReadContext context, int depth)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), context.Limits.MaxRecursionDepth, depth, DefectLocation.InRecord(context.StreamPath, group.Offset));
        ArtShape? own = null;
        var children = new List<ArtShape>();
        foreach (ArtRecord child in group.Children)
        {
            if (child.Type == ArtRecordType.SpContainer)
            {
                ArtShape shape = ParseShape(child, context);
                if (own is null && shape.IsGroup)
                {
                    own = shape;
                }
                else
                {
                    children.Add(shape);
                }
            }
            else if (child.Type == ArtRecordType.SpgrContainer)
            {
                children.Add(ParseGroup(child, context, depth + 1));
            }
        }

        own ??= new ArtShape(group) { Flags = ArtShapeAttributes.Group };
        if (!ReferenceEquals(own.Record, group))
        {
            // The group shape keeps the container record of its own shape container; expose the group container through a fresh wrapper so
            // callers see both the header data and the children.
            ArtShape wrapper = new(group)
            {
                ShapeId = own.ShapeId,
                ShapeType = own.ShapeType,
                Flags = own.Flags | ArtShapeAttributes.Group,
                Properties = own.Properties,
                GroupSpace = own.GroupSpace,
                ChildAnchor = own.ChildAnchor,
                ClientAnchor = own.ClientAnchor,
                ClientData = own.ClientData,
                ClientTextbox = own.ClientTextbox,
            };
            own = wrapper;
        }

        own.Children = children;
        return own;
    }

    internal static ArtShape ParseShape(ArtRecord container, ArtReadContext context)
    {
        var shape = new ArtShape(container);
        var properties = new ArtProperties();
        foreach (ArtRecord record in container.Children)
        {
            switch (record.Type)
            {
                case ArtRecordType.Fsp:
                    shape.ShapeType = record.Header.Instance;
                    shape.ShapeId = record.Int32At(0);
                    shape.Flags = (ArtShapeAttributes)record.Int32At(4);
                    break;
                case ArtRecordType.Fspgr:
                    if (record.Payload.Length >= 16)
                    {
                        shape.GroupSpace = new ArtRect(record.Int32At(0), record.Int32At(4), record.Int32At(8), record.Int32At(12));
                    }

                    break;
                case ArtRecordType.Fopt:
                case ArtRecordType.SecondaryFopt:
                case ArtRecordType.TertiaryFopt:
                    properties.AddTable(record, context);
                    break;
                case ArtRecordType.ChildAnchor:
                    // [MS-ODRAW] §2.2.37: left, top, right, bottom as 32-bit integers in the parent group's coordinate space.
                    if (record.Payload.Length >= 16)
                    {
                        shape.ChildAnchor = new ArtRect(record.Int32At(0), record.Int32At(4), record.Int32At(8), record.Int32At(12));
                    }

                    break;
                case ArtRecordType.ClientAnchor:
                    shape.ClientAnchor = record;
                    break;
                case ArtRecordType.ClientData:
                    shape.ClientData = record;
                    break;
                case ArtRecordType.ClientTextbox:
                    shape.ClientTextbox = record;
                    break;
            }
        }

        shape.Properties = properties.Count == 0 ? ArtProperties.Empty : properties;
        return shape;
    }
}

/// <summary>The drawing group ([MS-ODRAW] §2.2.12): identifiers shared by all drawings, the default properties and the BLIP store.</summary>
public sealed class ArtDrawingGroup
{
    private ArtDrawingGroup(ArtRecord record)
    {
        Record = record;
    }

    /// <summary>The drawing group container.</summary>
    public ArtRecord Record { get; private set; }

    /// <summary>The highest shape identifier in use (<c>spidMax</c>), or 0.</summary>
    public int ShapeIdMax { get; private set; }

    /// <summary>The default shape properties of the file, or an empty table.</summary>
    public ArtProperties DefaultProperties { get; private set; } = ArtProperties.Empty;

    /// <summary>The BLIP store, or null when the group has none.</summary>
    public ArtBlipStore? Blips { get; private set; }

    /// <summary>Parses a drawing group container.</summary>
    /// <param name="dgg">An <c>OfficeArtDggContainer</c> record.</param>
    /// <param name="context">Limits and defect sink.</param>
    /// <returns>The group.</returns>
    public static ArtDrawingGroup Parse(ArtRecord dgg, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(dgg);
        ArgumentNullException.ThrowIfNull(context);
        var group = new ArtDrawingGroup(dgg);
        ArtRecord? fdgg = dgg.Find(ArtRecordType.Fdgg);
        if (fdgg is not null)
        {
            group.ShapeIdMax = fdgg.Int32At(0);
        }

        var defaults = new ArtProperties();
        foreach (ArtRecord child in dgg.Children)
        {
            switch (child.Type)
            {
                case ArtRecordType.Fopt:
                case ArtRecordType.TertiaryFopt:
                    defaults.AddTable(child, context);
                    break;
                case ArtRecordType.BStoreContainer:
                    group.Blips = ArtBlipStore.Parse(child, context);
                    break;
            }
        }

        if (defaults.Count > 0)
        {
            group.DefaultProperties = defaults;
        }

        return group;
    }
}
