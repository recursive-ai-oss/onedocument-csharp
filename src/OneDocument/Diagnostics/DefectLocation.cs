namespace RecursiveAI.OneDocument.Diagnostics;

/// <summary>Which of the shapes a <see cref="DefectLocation"/> has.</summary>
public enum DefectLocationKind
{
    /// <summary>No location is known.</summary>
    None = 0,

    /// <summary>A byte offset inside a part of a package: <see cref="DefectLocation.Container"/> is the part URI.</summary>
    Part = 1,

    /// <summary>A byte offset inside a stream of a compound file or another record container: <see cref="DefectLocation.Container"/> is the stream path.</summary>
    Record = 2,

    /// <summary>A cell: <see cref="DefectLocation.Container"/> is the sheet name and <see cref="DefectLocation.Address"/> the A1 address.</summary>
    Cell = 3,

    /// <summary>A character offset in the document's text (for formats with no parts, such as RTF, CSV and Markdown).</summary>
    Text = 4,
}

/// <summary>
/// Where a <see cref="Defect"/> was found. Every defect says where, in the terms of the layer that found it: a part and a byte offset,
/// a stream and a record offset, a sheet and a cell, or a text position. Offsets count bytes of the part's or stream's
/// <em>uncompressed</em> content from zero, unless the shape says characters; a value of -1 means the offset is not known.
/// </summary>
public readonly record struct DefectLocation
{
    /// <summary>Gets a location that says nothing.</summary>
    public static DefectLocation None => default;

    /// <summary>Gets which shape this location has.</summary>
    public DefectLocationKind Kind { get; private init; }

    /// <summary>Gets the part URI (for <see cref="DefectLocationKind.Part"/>), the stream path (<see cref="DefectLocationKind.Record"/>) or the sheet name (<see cref="DefectLocationKind.Cell"/>); otherwise null.</summary>
    public string? Container { get; private init; }

    /// <summary>Gets the byte offset in the part or stream, the character offset for <see cref="DefectLocationKind.Text"/>, or -1 when unknown.</summary>
    public long Offset { get; private init; }

    /// <summary>Gets the A1-style cell address for <see cref="DefectLocationKind.Cell"/>; otherwise null.</summary>
    public string? Address { get; private init; }

    /// <summary>Creates a location inside a package part.</summary>
    /// <param name="partUri">The part URI, such as <c>/word/document.xml</c>.</param>
    /// <param name="offset">The byte offset in the part's uncompressed content, or -1 if unknown.</param>
    /// <returns>The location.</returns>
    public static DefectLocation InPart(string partUri, long offset = -1) =>
        new() { Kind = DefectLocationKind.Part, Container = partUri, Offset = offset };

    /// <summary>Creates a location inside a stream of a compound file (or another record container).</summary>
    /// <param name="streamPath">The stream path, such as <c>/WordDocument</c>.</param>
    /// <param name="offset">The byte offset of the record in the stream, or -1 if unknown.</param>
    /// <returns>The location.</returns>
    public static DefectLocation InRecord(string streamPath, long offset = -1) =>
        new() { Kind = DefectLocationKind.Record, Container = streamPath, Offset = offset };

    /// <summary>Creates a location at a cell.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="address">The A1 address, such as <c>B7</c>.</param>
    /// <returns>The location.</returns>
    public static DefectLocation InCell(string sheet, string address) =>
        new() { Kind = DefectLocationKind.Cell, Container = sheet, Address = address, Offset = -1 };

    /// <summary>Creates a location at a character offset in the text of a document.</summary>
    /// <param name="offset">The zero-based character offset.</param>
    /// <returns>The location.</returns>
    public static DefectLocation AtText(long offset) =>
        new() { Kind = DefectLocationKind.Text, Offset = offset };

    /// <summary>Returns the location in a stable, readable form such as <c>/word/document.xml@1042</c>, <c>Sheet1!B7</c> or <c>text@88</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Kind switch
    {
        DefectLocationKind.Part or DefectLocationKind.Record =>
            Offset >= 0 ? Container + "@" + Offset.ToString(System.Globalization.CultureInfo.InvariantCulture) : Container ?? string.Empty,
        DefectLocationKind.Cell => Container + "!" + Address,
        DefectLocationKind.Text => "text@" + Offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => string.Empty,
    };
}
