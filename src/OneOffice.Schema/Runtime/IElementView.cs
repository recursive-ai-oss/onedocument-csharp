namespace RecursiveAI.OneOffice.Schema;

/// <summary>
/// The contract every generated view fulfils. A view is a <c>readonly struct</c> over a <see cref="MarkupRef"/>: reading an
/// attribute parses it on demand, writing one edits the markup tree, and the tree alone keeps the document lossless.
/// </summary>
/// <typeparam name="TSelf">The view type itself.</typeparam>
public interface IElementView<TSelf> where TSelf : struct, IElementView<TSelf>
{
    /// <summary>The element this view reads and edits: the way back from a view to the markup layer.</summary>
    MarkupRef Markup { get; }

    /// <summary>Wraps an element without checking its name.</summary>
    /// <param name="markup">The element to wrap.</param>
    /// <returns>A view over it.</returns>
    static abstract TSelf Create(MarkupRef markup);

    /// <summary>The kind identifying this view type in <see cref="ViewFactory"/>.</summary>
    static abstract ViewKind Kind { get; }

    /// <summary>True when the schema declares an element of this name with this view's type.</summary>
    /// <param name="ns">The element's namespace.</param>
    /// <param name="local">The element's local name, UTF-8.</param>
    static abstract bool Accepts(SchemaNamespace ns, ReadOnlySpan<byte> local);

    /// <summary>
    /// Looks up a child element in this type's content model.
    /// </summary>
    /// <param name="ns">The child's namespace.</param>
    /// <param name="local">The child's local name, UTF-8.</param>
    /// <param name="rank">The child's position in the content model (equal ranks may appear in any order), or -1 when undeclared.</param>
    /// <returns>The kind of view the child is, <see cref="ViewKind.Simple"/> when it has no view, or <see cref="ViewKind.None"/> when undeclared.</returns>
    static abstract ViewKind ChildKind(SchemaNamespace ns, ReadOnlySpan<byte> local, out int rank);

    /// <summary>True when the schema type declares this attribute.</summary>
    /// <param name="ns">The attribute's namespace (<see cref="SchemaNamespace.None"/> for none).</param>
    /// <param name="local">The attribute's local name, UTF-8.</param>
    static abstract bool IsKnownAttribute(SchemaNamespace ns, ReadOnlySpan<byte> local);
}

/// <summary>Receives a typed view from <see cref="ViewFactory.Dispatch{TVisitor}"/> without boxing or reflection.</summary>
public interface IViewVisitor
{
    /// <summary>Called with the view of the requested kind.</summary>
    /// <typeparam name="T">The generated view type.</typeparam>
    /// <param name="view">The view.</param>
    void Visit<T>(T view) where T : struct, IElementView<T>;
}
