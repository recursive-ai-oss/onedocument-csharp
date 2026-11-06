using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using Rev = RecursiveAI.OneDocument.Documents.Revisions;
using RecursiveAI.OneDocument.Protection;
using RecursiveAI.OneDocument.Schema;
using W = RecursiveAI.OneDocument.Schema.Wordprocessing;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>The kind of editing a protected document still allows (ECMA-376 Part 1 §17.18.18, ST_DocProtect).</summary>
public enum DocumentEditRestriction
{
    /// <summary>No restriction.</summary>
    None = 0,

    /// <summary>The document is read-only.</summary>
    ReadOnly = 1,

    /// <summary>Only comments may be added.</summary>
    Comments = 2,

    /// <summary>Changes are tracked and cannot be turned off.</summary>
    TrackedChanges = 3,

    /// <summary>Only form fields may be filled in.</summary>
    Forms = 4,
}

/// <summary>
/// Editing restrictions of a document (<c>w:documentProtection</c> in the settings part): read and set, with an optional password that
/// is stored as a salted, iterated hash.
/// </summary>
/// <remarks>
/// <para><b>Protection is not encryption.</b> The restrictions are enforced by the application that opens the file; the content is stored
/// in the clear and anyone who edits the XML can remove the element. To keep a document confidential, encrypt it.</para>
/// <para><b>Hashes.</b> New passwords are written the way current Office writes them (<c>w:algorithmName</c>, <c>w:hashValue</c>,
/// <c>w:saltValue</c>, <c>w:spinCount</c>; SHA-512 by default) and any older <c>w:cryptAlgorithmSid</c> / <c>w:hash</c> attributes are replaced.
/// Both spellings are read and verified, including the pre-2007 Word key. Algorithms the library cannot compute (MD2, MD4, RIPEMD,
/// Whirlpool) verify as <see cref="PasswordCheck.UnsupportedAlgorithm"/>.</para>
/// <para>Changes are made to the settings part; when the document has none, protecting it creates one.</para>
/// </remarks>
public sealed class DocumentProtection
{
    private readonly Document _owner;

    internal DocumentProtection(Document owner)
    {
        _owner = owner;
    }

    private W.DocProtect? Element
    {
        get
        {
            MarkupDocument? settings = _owner.Parts.LoadRelated(_owner.MainPartName, "settings", out _);
            if (settings is null || settings.Root.IsNull)
            {
                return null;
            }

            MarkupRef? e = new MarkupRef(settings.Root).FindChild(SchemaNamespace.Wordprocessing, "documentProtection"u8);
            return e is { } m ? W.DocProtect.Create(m) : null;
        }
    }

    /// <summary>Gets a value indicating whether the document has restrictions that are turned on (<c>w:enforcement</c> is on).</summary>
    public bool IsEnforced => Element is { } e && e.Enforcement == true && Restriction != DocumentEditRestriction.None;

    /// <summary>Gets the kind of editing allowed; <see cref="DocumentEditRestriction.None"/> when the element is absent or names an unknown value.</summary>
    public DocumentEditRestriction Restriction => Element?.Edit switch
    {
        W.DocProtectValue.ReadOnly => DocumentEditRestriction.ReadOnly,
        W.DocProtectValue.Comments => DocumentEditRestriction.Comments,
        W.DocProtectValue.TrackedChanges => DocumentEditRestriction.TrackedChanges,
        W.DocProtectValue.Forms => DocumentEditRestriction.Forms,
        _ => DocumentEditRestriction.None,
    };

    /// <summary>Gets a value indicating whether formatting is restricted too (<c>w:formatting</c>).</summary>
    public bool FormattingRestricted => Element?.Formatting == true;

    /// <summary>Gets a value indicating whether a password hash is stored (either spelling).</summary>
    public bool HasPassword => Element is { } e && (!string.IsNullOrEmpty(e.HashValue) || !string.IsNullOrEmpty(e.Hash));

    /// <summary>Gets the stored verifier in the current spelling, or null when none is stored or only the older spelling is.</summary>
    public PasswordVerifier? Verifier => Element is { } e && PasswordVerifier.TryRead(e.AlgorithmName, e.HashValue, e.SaltValue, e.SpinCount?.ToString(CultureInfo.InvariantCulture), out PasswordVerifier? v) ? v : null;

    /// <summary>Checks a password against the stored hash.</summary>
    /// <param name="password">The password to test.</param>
    /// <returns>
    /// <see cref="PasswordCheck.Match"/> when the hash matches, or when no password is stored (anyone may remove such a protection);
    /// <see cref="PasswordCheck.Mismatch"/> otherwise; <see cref="PasswordCheck.UnsupportedAlgorithm"/> when the stored algorithm cannot be computed.
    /// </returns>
    /// <exception cref="OfficeLimitException">The stored spin count is larger than <see cref="OfficeLimits.MaxEncryptionSpinCount"/>.</exception>
    public PasswordCheck Verify(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (Element is not { } e)
        {
            return PasswordCheck.Match;
        }

        return ProtectionReader.Check(password, e.AlgorithmName, e.HashValue, e.SaltValue, e.SpinCount, e.CryptAlgorithmSid, e.Hash, e.Salt, e.CryptSpinCount, _owner.Context.Limits);
    }

    /// <summary>Turns editing restrictions on, replacing any that were set.</summary>
    /// <param name="restriction">The kind of editing to allow; not <see cref="DocumentEditRestriction.None"/> (use <see cref="Remove"/>).</param>
    /// <param name="password">The password that will be needed to remove the restriction in Word; null or empty sets none.</param>
    /// <param name="restrictFormatting">True to restrict formatting as well (<c>w:formatting</c>).</param>
    /// <param name="options">How the password is hashed; null means SHA-512, 100,000 iterations.</param>
    /// <exception cref="ArgumentException"><paramref name="restriction"/> is <see cref="DocumentEditRestriction.None"/>.</exception>
    public void Protect(DocumentEditRestriction restriction, string? password = null, bool restrictFormatting = false, ProtectionOptions? options = null)
    {
        if (restriction == DocumentEditRestriction.None)
        {
            throw new ArgumentException("Choose a restriction, or call Remove to lift it.", nameof(restriction));
        }

        W.Settings settings = _owner.EnsureSettings();
        settings.RemoveDocumentProtection();
        W.DocProtect e = settings.GetOrAddDocumentProtection();
        e.Edit = restriction switch
        {
            DocumentEditRestriction.ReadOnly => W.DocProtectValue.ReadOnly,
            DocumentEditRestriction.Comments => W.DocProtectValue.Comments,
            DocumentEditRestriction.TrackedChanges => W.DocProtectValue.TrackedChanges,
            _ => W.DocProtectValue.Forms,
        };
        e.Enforcement = true;
        if (restrictFormatting)
        {
            e.Formatting = true;
        }

        if (!string.IsNullOrEmpty(password))
        {
            options ??= new ProtectionOptions();
            PasswordVerifier v = PasswordVerifier.Create(password, options.Algorithm, options.SpinCount, options.Seed.Span, "documentProtection");
            e.AlgorithmName = v.AlgorithmName;
            e.HashValue = v.HashBase64;
            e.SaltValue = v.SaltBase64;
            e.SpinCount = v.SpinCount;
        }
    }

    /// <summary>Removes the restrictions without asking for the password (the caller decides who may do that).</summary>
    /// <returns>True when there was something to remove.</returns>
    public bool Remove()
    {
        MarkupDocument? settings = _owner.Parts.LoadRelated(_owner.MainPartName, "settings", out _);
        return settings is not null && !settings.Root.IsNull && W.Settings.Create(new MarkupRef(settings.Root)).RemoveDocumentProtection();
    }

    /// <summary>Removes the restrictions when the password matches.</summary>
    /// <param name="password">The password.</param>
    /// <returns>The outcome of the check; the restrictions are removed only on <see cref="PasswordCheck.Match"/>.</returns>
    public PasswordCheck TryRemove(string password)
    {
        PasswordCheck c = Verify(password);
        if (c == PasswordCheck.Match)
        {
            _ = Remove();
        }

        return c;
    }
}

/// <summary>
/// The write-reservation password of a document (<c>w:writeProtection</c>): a reader may open it read-only without the password and
/// edit only with it. Like <see cref="DocumentProtection"/> it is not encryption and the hash is stored as Office writes it.
/// </summary>
public sealed class DocumentWriteProtection
{
    private readonly Document _owner;

    internal DocumentWriteProtection(Document owner)
    {
        _owner = owner;
    }

    private W.WriteProtection? Element
    {
        get
        {
            MarkupDocument? settings = _owner.Parts.LoadRelated(_owner.MainPartName, "settings", out _);
            if (settings is null || settings.Root.IsNull)
            {
                return null;
            }

            MarkupRef? e = new MarkupRef(settings.Root).FindChild(SchemaNamespace.Wordprocessing, "writeProtection"u8);
            return e is { } m ? W.WriteProtection.Create(m) : null;
        }
    }

    /// <summary>Gets a value indicating whether the element is present.</summary>
    public bool IsPresent => Element is not null;

    /// <summary>Gets a value indicating whether the file asks to be opened read-only (<c>w:recommended</c>).</summary>
    public bool Recommended => Element?.Recommended == true;

    /// <summary>Gets a value indicating whether a password hash is stored (either spelling).</summary>
    public bool HasPassword => Element is { } e && (!string.IsNullOrEmpty(e.HashValue) || !string.IsNullOrEmpty(e.Hash));

    /// <summary>Checks a password against the stored hash; see <see cref="DocumentProtection.Verify"/> for the results.</summary>
    /// <param name="password">The password to test.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="OfficeLimitException">The stored spin count is larger than <see cref="OfficeLimits.MaxEncryptionSpinCount"/>.</exception>
    public PasswordCheck Verify(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (Element is not { } e)
        {
            return PasswordCheck.Match;
        }

        return ProtectionReader.Check(password, e.AlgorithmName, e.HashValue, e.SaltValue, e.SpinCount, e.CryptAlgorithmSid, e.Hash, e.Salt, e.CryptSpinCount, _owner.Context.Limits);
    }

    /// <summary>Sets the write reservation, replacing any that was set.</summary>
    /// <param name="password">The password; null or empty sets none (the file is only marked).</param>
    /// <param name="recommended">True to also ask applications to open the file read-only.</param>
    /// <param name="options">How the password is hashed; null means SHA-512, 100,000 iterations.</param>
    public void Set(string? password, bool recommended = false, ProtectionOptions? options = null)
    {
        W.Settings settings = _owner.EnsureSettings();
        settings.RemoveWriteProtection();
        W.WriteProtection e = settings.GetOrAddWriteProtection();
        if (recommended)
        {
            e.Recommended = true;
        }

        if (!string.IsNullOrEmpty(password))
        {
            options ??= new ProtectionOptions();
            PasswordVerifier v = PasswordVerifier.Create(password, options.Algorithm, options.SpinCount, options.Seed.Span, "writeProtection");
            e.AlgorithmName = v.AlgorithmName;
            e.HashValue = v.HashBase64;
            e.SaltValue = v.SaltBase64;
            e.SpinCount = v.SpinCount;
        }
    }

    /// <summary>Removes the write reservation.</summary>
    /// <returns>True when there was something to remove.</returns>
    public bool Remove()
    {
        MarkupDocument? settings = _owner.Parts.LoadRelated(_owner.MainPartName, "settings", out _);
        return settings is not null && !settings.Root.IsNull && W.Settings.Create(new MarkupRef(settings.Root)).RemoveWriteProtection();
    }
}

/// <summary>A stretch of a protected document that may be edited by an editor or a group (<c>w:permStart</c> … <c>w:permEnd</c>).</summary>
/// <param name="Id">The id that pairs the start and end markers.</param>
/// <param name="Group">The editor group (<c>everyone</c>, <c>editors</c>, …), or null when the exception is for a user.</param>
/// <param name="User">The user the exception is for, or null when it is for a group.</param>
/// <param name="Part">The part that holds the markers.</param>
public sealed record EditingRange(string Id, string? Group, string? User, string Part);

public sealed partial class Document
{
    private DocumentProtection? _protection;
    private DocumentWriteProtection? _writeProtection;

    /// <summary>Gets the editing restrictions (<c>w:documentProtection</c>); read, set, verify and remove. See <see cref="DocumentProtection"/>: protection is not encryption.</summary>
    public DocumentProtection Protection => _protection ??= new DocumentProtection(this);

    /// <summary>Gets the write reservation (<c>w:writeProtection</c>). See <see cref="DocumentWriteProtection"/>: protection is not encryption.</summary>
    public DocumentWriteProtection WriteProtection => _writeProtection ??= new DocumentWriteProtection(this);

    internal W.Settings EnsureSettings()
    {
        MarkupDocument? settings = Parts.LoadRelated(MainPartName, "settings", out _)
            ?? CreateRelatedPart("settings", "/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml", "settings", out _);
        return W.Settings.Create(new MarkupRef(settings.Root));
    }

    /// <summary>
    /// Marks the paragraphs from <paramref name="first"/> to <paramref name="last"/> as editable in a protected document, by writing
    /// <c>w:permStart</c> before the first and <c>w:permEnd</c> after the last (ECMA-376 Part 1 §17.13.7). Both must be in the same container
    /// (body, table cell or content control) in that order.
    /// </summary>
    /// <param name="first">The first paragraph of the range.</param>
    /// <param name="last">The last paragraph of the range; may be the same paragraph.</param>
    /// <param name="group">The editor group allowed to edit it (<c>everyone</c> by default); ignored when <paramref name="user"/> is given.</param>
    /// <param name="user">A user (for example an email address) allowed to edit it instead of a group; null for a group.</param>
    /// <returns>The exception written.</returns>
    /// <exception cref="ArgumentException">The paragraphs are not in this document, not siblings, or in the wrong order.</exception>
    public EditingRange AddEditingException(Paragraph first, Paragraph last, string? group = "everyone", string? user = null)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(last);
        MarkupNode a = first.Markup.Node;
        MarkupNode b = last.Markup.Node;
        if (!ReferenceEquals(a.Document, b.Document) || a.Parent != b.Parent || Parts.NameOf(a.Document!) is null)
        {
            throw new ArgumentException("The paragraphs must be siblings in a part of this document.", nameof(last));
        }

        bool ordered = false;
        for (MarkupNode n = a; !n.IsNull; n = n.NextSibling)
        {
            if (n == b)
            {
                ordered = true;
                break;
            }
        }

        if (!ordered)
        {
            throw new ArgumentException("The last paragraph comes before the first.", nameof(last));
        }

        string ns = a.NamespaceUri;
        string prefix = Paragraph.Owner_Prefix(a, ns, out bool declare);
        string decl = declare ? " xmlns:" + prefix + "=\"" + ns + "\"" : string.Empty;
        string part = Parts.NameOf(a.Document!) ?? MainPartName;
        int next = 0;
        foreach (EditingRange e in EditingExceptions())
        {
            if (int.TryParse(e.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id >= next)
            {
                next = id + 1;
            }
        }

        string idText = next.ToString(CultureInfo.InvariantCulture);
        string who = user is not null
            ? " " + prefix + ":ed=\"" + System.Security.SecurityElement.Escape(user) + "\""
            : " " + prefix + ":edGrp=\"" + System.Security.SecurityElement.Escape(group ?? "everyone") + "\"";
        a.InsertXmlBefore(Encoding.UTF8.GetBytes("<" + prefix + ":permStart" + decl + " " + prefix + ":id=\"" + idText + "\"" + who + "/>"));
        b.InsertXmlAfter(Encoding.UTF8.GetBytes("<" + prefix + ":permEnd" + decl + " " + prefix + ":id=\"" + idText + "\"/>"));
        return new EditingRange(idText, user is null ? group ?? "everyone" : null, user, part);
    }

    /// <summary>Lists the editing exceptions (<c>w:permStart</c> markers) in the main part, headers, footers and notes, in document order.</summary>
    /// <returns>The exceptions; empty when there are none.</returns>
    public IReadOnlyList<EditingRange> EditingExceptions()
    {
        var list = new List<EditingRange>();
        foreach ((string name, MarkupDocument doc) in PermParts())
        {
            Collect(doc.Root, name, list, 0);
        }

        return list;

        void Collect(MarkupNode n, string part, List<EditingRange> into, int depth)
        {
            if (depth > 256)
            {
                return;
            }

            foreach (MarkupNode c in n.ChildElements)
            {
                if (Rev.WordXml.Is(c, "permStart"))
                {
                    string? ed = Rev.WordXml.Attr(c, "ed");
                    string? grp = Rev.WordXml.Attr(c, "edGrp");
                    into.Add(new EditingRange(Rev.WordXml.Attr(c, "id") ?? string.Empty, ed is null ? grp : null, ed, part));
                }
                else
                {
                    Collect(c, part, into, depth + 1);
                }
            }
        }
    }

    /// <summary>Removes every editing exception (all <c>w:permStart</c> and <c>w:permEnd</c> markers).</summary>
    /// <returns>The number of markers removed.</returns>
    public int RemoveEditingExceptions()
    {
        int removed = 0;
        foreach ((_, MarkupDocument doc) in PermParts())
        {
            var found = new List<MarkupNode>();
            Gather(doc.Root, found, 0);
            foreach (MarkupNode m in found)
            {
                m.Remove();
                removed++;
            }
        }

        return removed;

        static void Gather(MarkupNode n, List<MarkupNode> into, int depth)
        {
            if (depth > 256)
            {
                return;
            }

            foreach (MarkupNode c in n.ChildElements)
            {
                if (Rev.WordXml.IsAny(c, "permStart", "permEnd"))
                {
                    into.Add(c);
                }
                else
                {
                    Gather(c, into, depth + 1);
                }
            }
        }
    }

    private IEnumerable<(string Name, MarkupDocument Doc)> PermParts()
    {
        yield return (MainPartName, MainMarkup);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MainPartName };
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (r.TargetMode == RelationshipTargetMode.Internal
                && (RelationshipTypes.Is(r.Type, "header") || RelationshipTypes.Is(r.Type, "footer") || RelationshipTypes.Is(r.Type, "footnotes") || RelationshipTypes.Is(r.Type, "endnotes"))
                && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && seen.Add(uri.Value)
                && Package.FindPart(uri.Value) is { } part && Parts.Load(part.Name, required: false) is { } doc && !doc.Root.IsNull)
            {
                yield return (part.Name, doc);
            }
        }
    }
}

/// <summary>Reads stored protection attributes in either spelling and checks a password against them.</summary>
internal static class ProtectionReader
{
    internal static PasswordCheck Check(string password, string? algorithmName, string? hashValue, string? saltValue, long? spinCount, long? sid, string? hash, string? salt, long? cryptSpinCount, OfficeLimits limits)
    {
        if (!string.IsNullOrEmpty(algorithmName) && !string.IsNullOrEmpty(hashValue))
        {
            return PasswordVerifier.TryRead(algorithmName, hashValue, saltValue, spinCount?.ToString(CultureInfo.InvariantCulture), out PasswordVerifier? v) && v is not null
                ? v.Check(password, limits)
                : PasswordCheck.Mismatch;
        }

        if (!string.IsNullOrEmpty(hash))
        {
            try
            {
                byte[] h = Convert.FromBase64String(hash);
                byte[] s = string.IsNullOrEmpty(salt) ? [] : Convert.FromBase64String(salt);
                return PasswordVerifier.CheckLegacyWord(password, (int)(sid ?? 4), s, h, (int)Math.Clamp(cryptSpinCount ?? 0, 0, int.MaxValue), limits);
            }
            catch (FormatException)
            {
                return PasswordCheck.Mismatch;
            }
        }

        // Nothing stored: the protection has no password, so any input removes it.
        return PasswordCheck.Match;
    }
}
