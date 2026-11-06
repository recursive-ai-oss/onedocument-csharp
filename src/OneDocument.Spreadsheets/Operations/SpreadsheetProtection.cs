using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Protection;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>What a protected sheet still lets the user do (ECMA-376 Part 1 §18.3.1.85, <c>sheetProtection</c>).</summary>
[Flags]
public enum SheetActions
{
    /// <summary>Nothing beyond what protection never blocks.</summary>
    None = 0,

    /// <summary>Format cells.</summary>
    FormatCells = 1,

    /// <summary>Format columns.</summary>
    FormatColumns = 2,

    /// <summary>Format rows.</summary>
    FormatRows = 4,

    /// <summary>Insert columns.</summary>
    InsertColumns = 8,

    /// <summary>Insert rows.</summary>
    InsertRows = 16,

    /// <summary>Insert hyperlinks.</summary>
    InsertHyperlinks = 32,

    /// <summary>Delete columns.</summary>
    DeleteColumns = 64,

    /// <summary>Delete rows.</summary>
    DeleteRows = 128,

    /// <summary>Select cells that are locked.</summary>
    SelectLockedCells = 256,

    /// <summary>Select cells that are not locked.</summary>
    SelectUnlockedCells = 512,

    /// <summary>Sort.</summary>
    Sort = 1024,

    /// <summary>Use the AutoFilter.</summary>
    AutoFilter = 2048,

    /// <summary>Use PivotTables.</summary>
    PivotTables = 4096,

    /// <summary>Edit drawing objects.</summary>
    EditObjects = 8192,

    /// <summary>Edit scenarios.</summary>
    EditScenarios = 16384,
}

/// <summary>A range of a protected sheet that may be edited without the sheet password (<c>protectedRange</c>).</summary>
/// <param name="Name">The range's title.</param>
/// <param name="Ranges">The space-separated references (<c>sqref</c>).</param>
/// <param name="HasPassword">True when a password is needed to edit it.</param>
public sealed record ProtectedRange(string Name, string Ranges, bool HasPassword);

internal static class HashAttributes
{
    internal static void Write(MarkupNode e, PasswordVerifier v, string algorithm, string hash, string salt, string spin)
    {
        e.SetAttribute(algorithm, v.AlgorithmName);
        e.SetAttribute(hash, v.HashBase64);
        e.SetAttribute(salt, v.SaltBase64);
        e.SetAttribute(spin, v.SpinCount.ToString(CultureInfo.InvariantCulture));
    }

    internal static string Attributes(PasswordVerifier v, string algorithm, string hash, string salt, string spin) =>
        " " + algorithm + "=\"" + v.AlgorithmName + "\" " + hash + "=\"" + v.HashBase64 + "\" " + salt + "=\"" + v.SaltBase64 + "\" " + spin + "=\"" + v.SpinCount.ToString(CultureInfo.InvariantCulture) + "\"";

    internal static bool Has(MarkupNode e, string hash, string legacy) => e.GetAttribute(hash) is { Length: > 0 } || e.GetAttribute(legacy) is { Length: > 0 };

    internal static PasswordCheck Check(MarkupNode e, string password, string algorithm, string hash, string salt, string spin, string legacy, OfficeLimits limits)
    {
        if (e.GetAttribute(algorithm) is { Length: > 0 } && e.GetAttribute(hash) is { Length: > 0 })
        {
            return PasswordVerifier.TryRead(e.GetAttribute(algorithm), e.GetAttribute(hash), e.GetAttribute(salt), e.GetAttribute(spin), out PasswordVerifier? v) && v is not null
                ? v.Check(password, limits)
                : PasswordCheck.Mismatch;
        }

        if (e.GetAttribute(legacy) is { Length: > 0 } hex)
        {
            return PasswordVerifier.CheckExcelHash(password, hex);
        }

        // No password is stored, so any input unlocks.
        return PasswordCheck.Match;
    }
}

/// <summary>
/// The protection of one worksheet (<c>sheetProtection</c>) and its protected ranges. Reading and setting, with a password stored as a
/// salted, iterated hash (SHA-512 by default); the 16-bit hash older Excel versions wrote is read and verified.
/// </summary>
/// <remarks>
/// <b>Protection is not encryption.</b> Cell values and formulas stay readable and anyone who edits the XML can remove the element; the
/// restrictions bind only applications that choose to honour them. Encrypt the file to keep it confidential.
/// </remarks>
public sealed class SheetProtection
{
    private static readonly (string Name, SheetActions Flag, bool DefaultLocked)[] Table =
    [
        ("objects", SheetActions.EditObjects, false), ("scenarios", SheetActions.EditScenarios, false),
        ("formatCells", SheetActions.FormatCells, true), ("formatColumns", SheetActions.FormatColumns, true), ("formatRows", SheetActions.FormatRows, true),
        ("insertColumns", SheetActions.InsertColumns, true), ("insertRows", SheetActions.InsertRows, true), ("insertHyperlinks", SheetActions.InsertHyperlinks, true),
        ("deleteColumns", SheetActions.DeleteColumns, true), ("deleteRows", SheetActions.DeleteRows, true),
        ("selectLockedCells", SheetActions.SelectLockedCells, false), ("sort", SheetActions.Sort, true), ("autoFilter", SheetActions.AutoFilter, true),
        ("pivotTables", SheetActions.PivotTables, true), ("selectUnlockedCells", SheetActions.SelectUnlockedCells, false),
    ];

    private readonly Worksheet _sheet;

    internal SheetProtection(Worksheet sheet)
    {
        _sheet = sheet;
    }

    private MarkupNode? Element
    {
        get
        {
            if (_sheet.Kind != SheetKind.Worksheet)
            {
                return null;
            }

            foreach (MarkupNode c in _sheet.FrameRoot.ChildElements)
            {
                if (c.LocalName.SequenceEqual("sheetProtection"u8))
                {
                    return c;
                }
            }

            return null;
        }
    }

    /// <summary>Gets a value indicating whether the sheet is protected (<c>sheet</c> is on).</summary>
    public bool IsProtected => Element is { } e && e.GetAttribute("sheet") is "1" or "true";

    /// <summary>Gets a value indicating whether a password hash is stored (either spelling).</summary>
    public bool HasPassword => Element is { } e && HashAttributes.Has(e, "hashValue", "password");

    /// <summary>Gets the actions a protected sheet allows, from the attributes and the defaults the schema gives them.</summary>
    public SheetActions Allowed
    {
        get
        {
            SheetActions a = SheetActions.None;
            MarkupNode? e = Element;
            foreach ((string name, SheetActions flag, bool defaultLocked) in Table)
            {
                bool locked = e?.GetAttribute(name) switch { "1" or "true" => true, "0" or "false" => false, _ => defaultLocked };
                if (!locked)
                {
                    a |= flag;
                }
            }

            return a;
        }
    }

    /// <summary>Gets the stored verifier in the current spelling, or null when none is stored in it.</summary>
    public PasswordVerifier? Verifier => Element is { } e && PasswordVerifier.TryRead(e.GetAttribute("algorithmName"), e.GetAttribute("hashValue"), e.GetAttribute("saltValue"), e.GetAttribute("spinCount"), out PasswordVerifier? v) ? v : null;

    /// <summary>Checks a password against the stored hash.</summary>
    /// <param name="password">The password to test.</param>
    /// <returns><see cref="PasswordCheck.Match"/> when it matches or none is stored; <see cref="PasswordCheck.Mismatch"/>; or <see cref="PasswordCheck.UnsupportedAlgorithm"/>.</returns>
    /// <exception cref="OfficeLimitException">The stored spin count is larger than <see cref="OfficeLimits.MaxEncryptionSpinCount"/>.</exception>
    public PasswordCheck Verify(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return Element is { } e ? HashAttributes.Check(e, password, "algorithmName", "hashValue", "saltValue", "spinCount", "password", _sheet.Workbook.Limits) : PasswordCheck.Match;
    }

    /// <summary>Protects the sheet, replacing any protection it had.</summary>
    /// <param name="allowed">The actions to allow; null allows selecting locked and unlocked cells, what Excel offers by default.</param>
    /// <param name="password">The password; null or empty sets none.</param>
    /// <param name="options">How the password is hashed; null means SHA-512, 100,000 iterations.</param>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    public void Protect(SheetActions? allowed = null, string? password = null, ProtectionOptions? options = null)
    {
        if (_sheet.Kind != SheetKind.Worksheet)
        {
            throw new NotSupportedException("Only worksheets can be protected here.");
        }

        SheetActions set = allowed ?? (SheetActions.SelectLockedCells | SheetActions.SelectUnlockedCells);
        var sb = new StringBuilder();
        string pc = Xl.Prefix(_sheet.FrameRoot);
        sb.Append('<').Append(pc).Append("sheetProtection");
        if (!string.IsNullOrEmpty(password))
        {
            options ??= new ProtectionOptions();
            PasswordVerifier v = PasswordVerifier.Create(password, options.Algorithm, options.SpinCount, options.Seed.Span, "sheetProtection:" + _sheet.Name);
            sb.Append(HashAttributes.Attributes(v, "algorithmName", "hashValue", "saltValue", "spinCount"));
        }

        sb.Append(" sheet=\"1\"");
        foreach ((string name, SheetActions flag, bool defaultLocked) in Table)
        {
            bool locked = !set.HasFlag(flag);
            if (locked != defaultLocked)
            {
                sb.Append(' ').Append(name).Append("=\"").Append(locked ? '1' : '0').Append('"');
            }
        }

        sb.Append("/>");
        MarkupNode? old = Element;
        old?.Remove();
        _ = _sheet.InsertFrameElement("sheetProtection", sb.ToString());
        _sheet.NoteFrameEdited();
    }

    /// <summary>Removes the protection without asking for the password. Protected ranges are kept.</summary>
    /// <returns>True when the sheet had a protection element.</returns>
    public bool Remove()
    {
        MarkupNode? e = Element;
        if (e is null)
        {
            return false;
        }

        e.Value.Remove();
        _sheet.NoteFrameEdited();
        return true;
    }

    /// <summary>Removes the protection when the password matches.</summary>
    /// <param name="password">The password.</param>
    /// <returns>The check's outcome; the protection is removed only on <see cref="PasswordCheck.Match"/>.</returns>
    public PasswordCheck TryRemove(string password)
    {
        PasswordCheck c = Verify(password);
        if (c == PasswordCheck.Match)
        {
            _ = Remove();
        }

        return c;
    }

    /// <summary>Lists the protected ranges (<c>protectedRange</c>) in file order.</summary>
    /// <returns>The ranges; empty when there are none.</returns>
    public IReadOnlyList<ProtectedRange> ProtectedRanges()
    {
        var list = new List<ProtectedRange>();
        if (_sheet.Kind != SheetKind.Worksheet)
        {
            return list;
        }

        foreach (MarkupNode c in _sheet.FrameRoot.ChildElements)
        {
            if (!c.LocalName.SequenceEqual("protectedRanges"u8))
            {
                continue;
            }

            foreach (MarkupNode r in c.ChildElements)
            {
                if (r.LocalName.SequenceEqual("protectedRange"u8))
                {
                    list.Add(new ProtectedRange(r.GetAttribute("name") ?? string.Empty, r.GetAttribute("sqref") ?? string.Empty, HashAttributes.Has(r, "hashValue", "password")));
                }
            }
        }

        return list;
    }

    /// <summary>Checks a password against a protected range's stored hash.</summary>
    /// <param name="name">The range's name.</param>
    /// <param name="password">The password to test.</param>
    /// <returns>The outcome; <see cref="PasswordCheck.Match"/> for a range without a password.</returns>
    /// <exception cref="KeyNotFoundException">The sheet has no protected range with that name.</exception>
    public PasswordCheck VerifyRange(string name, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        MarkupNode r = FindRange(name) ?? throw new KeyNotFoundException("The sheet has no protected range named '" + name + "'.");
        return HashAttributes.Check(r, password, "algorithmName", "hashValue", "saltValue", "spinCount", "password", _sheet.Workbook.Limits);
    }

    /// <summary>Adds a protected range: cells editable without the sheet password, optionally behind their own.</summary>
    /// <param name="name">The title; not already used on this sheet.</param>
    /// <param name="ranges">The cell references, such as <c>A1:B5 D7</c> (space-separated).</param>
    /// <param name="password">A password for the range; null or empty sets none.</param>
    /// <param name="options">How the password is hashed.</param>
    /// <exception cref="ArgumentException">The name is empty or already used, or a reference does not parse.</exception>
    public void AddProtectedRange(string name, string ranges, string? password = null, ProtectionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(ranges);
        if (FindRange(name) is not null)
        {
            throw new ArgumentException("The sheet already has a protected range named '" + name + "'.", nameof(name));
        }

        foreach (string part in ranges.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!CellRange.TryParse(part, out _))
            {
                throw new ArgumentException("'" + part + "' is not a cell reference.", nameof(ranges));
            }
        }

        string pc = Xl.Prefix(_sheet.FrameRoot);
        var sb = new StringBuilder("<").Append(pc).Append("protectedRange");
        if (!string.IsNullOrEmpty(password))
        {
            options ??= new ProtectionOptions();
            PasswordVerifier v = PasswordVerifier.Create(password, options.Algorithm, options.SpinCount, options.Seed.Span, "protectedRange:" + _sheet.Name + ":" + name);
            sb.Append(HashAttributes.Attributes(v, "algorithmName", "hashValue", "saltValue", "spinCount"));
        }

        sb.Append(" sqref=\"").Append(Xl.Esc(string.Join(' ', ranges.Split(' ', StringSplitOptions.RemoveEmptyEntries)))).Append("\" name=\"").Append(Xl.Esc(name)).Append("\"/>");
        MarkupNode container = default;
        foreach (MarkupNode c in _sheet.FrameRoot.ChildElements)
        {
            if (c.LocalName.SequenceEqual("protectedRanges"u8))
            {
                container = c;
            }
        }

        if (container.IsNull)
        {
            _ = _sheet.InsertFrameElement("protectedRanges", "<" + pc + "protectedRanges>" + sb + "</" + pc + "protectedRanges>");
        }
        else
        {
            container.AppendXml(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        _sheet.NoteFrameEdited();
    }

    /// <summary>Removes a protected range.</summary>
    /// <param name="name">The range's name.</param>
    /// <returns>True when it existed.</returns>
    public bool RemoveProtectedRange(string name)
    {
        MarkupNode? r = FindRange(name);
        if (r is null)
        {
            return false;
        }

        MarkupNode parent = r.Value.Parent;
        r.Value.Remove();
        bool empty = true;
        foreach (MarkupNode unused in parent.ChildElements)
        {
            _ = unused;
            empty = false;
            break;
        }

        if (empty)
        {
            parent.Remove();
        }

        _sheet.NoteFrameEdited();
        return true;
    }

    private MarkupNode? FindRange(string name)
    {
        if (_sheet.Kind != SheetKind.Worksheet)
        {
            return null;
        }

        foreach (MarkupNode c in _sheet.FrameRoot.ChildElements)
        {
            if (!c.LocalName.SequenceEqual("protectedRanges"u8))
            {
                continue;
            }

            foreach (MarkupNode r in c.ChildElements)
            {
                if (r.LocalName.SequenceEqual("protectedRange"u8) && string.Equals(r.GetAttribute("name"), name, StringComparison.Ordinal))
                {
                    return r;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// The structure and window protection of a workbook (<c>workbookProtection</c>): whether sheets can be added, removed, renamed, hidden or
/// moved, and whether the windows can be resized, with an optional password. Not encryption; see <see cref="SheetProtection"/>.
/// </summary>
public sealed class WorkbookProtection
{
    private readonly Workbook _book;

    internal WorkbookProtection(Workbook book)
    {
        _book = book;
    }

    private MarkupNode? Element
    {
        get
        {
            foreach (MarkupNode c in _book.Markup.Root.ChildElements)
            {
                if (c.LocalName.SequenceEqual("workbookProtection"u8))
                {
                    return c;
                }
            }

            return null;
        }
    }

    /// <summary>Gets a value indicating whether the sheet structure is locked (<c>lockStructure</c>).</summary>
    public bool LockStructure => Element is { } e && e.GetAttribute("lockStructure") is "1" or "true";

    /// <summary>Gets a value indicating whether the windows are locked (<c>lockWindows</c>).</summary>
    public bool LockWindows => Element is { } e && e.GetAttribute("lockWindows") is "1" or "true";

    /// <summary>Gets a value indicating whether a password hash is stored for the structure and windows (either spelling).</summary>
    public bool HasPassword => Element is { } e && HashAttributes.Has(e, "workbookHashValue", "workbookPassword");

    /// <summary>Gets a value indicating whether a revisions password is stored (shared-workbook tracking), kept as it is.</summary>
    public bool HasRevisionsPassword => Element is { } e && HashAttributes.Has(e, "revisionsHashValue", "revisionsPassword");

    /// <summary>Checks a password against the stored structure and window hash.</summary>
    /// <param name="password">The password to test.</param>
    /// <returns>The outcome; <see cref="PasswordCheck.Match"/> when none is stored.</returns>
    /// <exception cref="OfficeLimitException">The stored spin count is larger than <see cref="OfficeLimits.MaxEncryptionSpinCount"/>.</exception>
    public PasswordCheck Verify(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return Element is { } e ? HashAttributes.Check(e, password, "workbookAlgorithmName", "workbookHashValue", "workbookSaltValue", "workbookSpinCount", "workbookPassword", _book.Limits) : PasswordCheck.Match;
    }

    /// <summary>Checks a password against the stored revisions hash.</summary>
    /// <param name="password">The password to test.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="OfficeLimitException">The stored spin count is larger than the limit.</exception>
    public PasswordCheck VerifyRevisions(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return Element is { } e ? HashAttributes.Check(e, password, "revisionsAlgorithmName", "revisionsHashValue", "revisionsSaltValue", "revisionsSpinCount", "revisionsPassword", _book.Limits) : PasswordCheck.Match;
    }

    /// <summary>Locks the structure and/or the windows, replacing the structure and window password (a revisions password stays).</summary>
    /// <param name="lockStructure">True to lock the sheet structure.</param>
    /// <param name="lockWindows">True to lock the windows.</param>
    /// <param name="password">The password; null or empty sets none.</param>
    /// <param name="options">How the password is hashed.</param>
    public void Protect(bool lockStructure = true, bool lockWindows = false, string? password = null, ProtectionOptions? options = null)
    {
        string pc = _book.Pc();
        string keep = string.Empty;
        if (Element is { } old)
        {
            var sb0 = new StringBuilder();
            foreach (MarkupAttributeRef a in old.Attributes)
            {
                string n = a.RawName.Length == 0 ? string.Empty : Encoding.UTF8.GetString(a.RawName);
                if (n.StartsWith("revisions", StringComparison.Ordinal) || n == "lockRevision")
                {
                    sb0.Append(' ').Append(n).Append("=\"").Append(Xl.Esc(a.Value)).Append('"');
                }
            }

            keep = sb0.ToString();
            old.Remove();
        }

        var sb = new StringBuilder();
        sb.Append('<').Append(pc).Append("workbookProtection");
        if (!string.IsNullOrEmpty(password))
        {
            options ??= new ProtectionOptions();
            PasswordVerifier v = PasswordVerifier.Create(password, options.Algorithm, options.SpinCount, options.Seed.Span, "workbookProtection");
            sb.Append(HashAttributes.Attributes(v, "workbookAlgorithmName", "workbookHashValue", "workbookSaltValue", "workbookSpinCount"));
        }

        sb.Append(keep);
        if (lockStructure)
        {
            sb.Append(" lockStructure=\"1\"");
        }

        if (lockWindows)
        {
            sb.Append(" lockWindows=\"1\"");
        }

        sb.Append("/>");
        _book.InsertWorkbookChild("workbookProtection", sb.ToString());
    }

    /// <summary>Removes the protection without asking for the password.</summary>
    /// <returns>True when the workbook had a protection element.</returns>
    public bool Remove()
    {
        MarkupNode? e = Element;
        if (e is null)
        {
            return false;
        }

        e.Value.Remove();
        return true;
    }

    /// <summary>Removes the protection when the password matches.</summary>
    /// <param name="password">The password.</param>
    /// <returns>The check's outcome; removed only on <see cref="PasswordCheck.Match"/>.</returns>
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

/// <summary>The write reservation of a workbook (<c>fileSharing</c>): opened read-only unless the reservation password is given. Not encryption.</summary>
public sealed class WorkbookFileSharing
{
    private readonly Workbook _book;

    internal WorkbookFileSharing(Workbook book)
    {
        _book = book;
    }

    private MarkupNode? Element
    {
        get
        {
            foreach (MarkupNode c in _book.Markup.Root.ChildElements)
            {
                if (c.LocalName.SequenceEqual("fileSharing"u8))
                {
                    return c;
                }
            }

            return null;
        }
    }

    /// <summary>Gets a value indicating whether the element is present.</summary>
    public bool IsPresent => Element is not null;

    /// <summary>Gets a value indicating whether the file asks to be opened read-only (<c>readOnlyRecommended</c>).</summary>
    public bool ReadOnlyRecommended => Element is { } e && e.GetAttribute("readOnlyRecommended") is "1" or "true";

    /// <summary>Gets the user name recorded with the reservation, or null.</summary>
    public string? UserName => Element?.GetAttribute("userName");

    /// <summary>Gets a value indicating whether a reservation password hash is stored (either spelling).</summary>
    public bool HasPassword => Element is { } e && HashAttributes.Has(e, "hashValue", "reservationPassword");

    /// <summary>Checks a password against the stored reservation hash.</summary>
    /// <param name="password">The password to test.</param>
    /// <returns>The outcome; <see cref="PasswordCheck.Match"/> when none is stored.</returns>
    /// <exception cref="OfficeLimitException">The stored spin count is larger than <see cref="OfficeLimits.MaxEncryptionSpinCount"/>.</exception>
    public PasswordCheck Verify(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return Element is { } e ? HashAttributes.Check(e, password, "algorithmName", "hashValue", "saltValue", "spinCount", "reservationPassword", _book.Limits) : PasswordCheck.Match;
    }

    /// <summary>Sets the write reservation, replacing any.</summary>
    /// <param name="password">The password; null or empty sets none.</param>
    /// <param name="readOnlyRecommended">True to also ask to be opened read-only.</param>
    /// <param name="userName">The name to record, or null.</param>
    /// <param name="options">How the password is hashed.</param>
    public void Set(string? password, bool readOnlyRecommended = false, string? userName = null, ProtectionOptions? options = null)
    {
        Element?.Remove();
        var sb = new StringBuilder("<").Append(_book.Pc()).Append("fileSharing");
        if (readOnlyRecommended)
        {
            sb.Append(" readOnlyRecommended=\"1\"");
        }

        if (userName is not null)
        {
            sb.Append(" userName=\"").Append(Xl.Esc(userName)).Append('"');
        }

        if (!string.IsNullOrEmpty(password))
        {
            options ??= new ProtectionOptions();
            PasswordVerifier v = PasswordVerifier.Create(password, options.Algorithm, options.SpinCount, options.Seed.Span, "fileSharing");
            sb.Append(HashAttributes.Attributes(v, "algorithmName", "hashValue", "saltValue", "spinCount"));
        }

        sb.Append("/>");
        _book.InsertWorkbookChild("fileSharing", sb.ToString());
    }

    /// <summary>Removes the write reservation.</summary>
    /// <returns>True when there was one.</returns>
    public bool Remove()
    {
        MarkupNode? e = Element;
        if (e is null)
        {
            return false;
        }

        e.Value.Remove();
        return true;
    }
}

public sealed partial class Worksheet
{
    private SheetProtection? _protection;

    /// <summary>Gets the sheet's protection and protected ranges. See <see cref="SheetProtection"/>: protection is not encryption.</summary>
    public SheetProtection Protection => _protection ??= new SheetProtection(this);
}

public sealed partial class Workbook
{
    private WorkbookProtection? _protection;
    private WorkbookFileSharing? _fileSharing;

    /// <summary>Gets the workbook's structure and window protection. See <see cref="WorkbookProtection"/>: protection is not encryption.</summary>
    public WorkbookProtection Protection => _protection ??= new WorkbookProtection(this);

    /// <summary>Gets the write reservation (<c>fileSharing</c>). See <see cref="WorkbookFileSharing"/>: protection is not encryption.</summary>
    public WorkbookFileSharing FileSharing => _fileSharing ??= new WorkbookFileSharing(this);
}
