using RecursiveAI.OneOffice.Documents.Comments;

namespace RecursiveAI.OneOffice.Documents;

public sealed partial class Document
{
    private CommentCollection? _comments;

    /// <summary>Gets the comments (<c>/word/comments.xml</c> and the modern comment parts that go with it); empty when the document has none.</summary>
    public CommentCollection Comments => Init(ref _comments, () => new CommentCollection(this));
}
