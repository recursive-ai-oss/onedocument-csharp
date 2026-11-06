namespace RecursiveAI.OneDocument.Vba;

/// <summary>What a module is, from the <c>PROJECT</c> stream's item lines ([MS-OVBA] §2.3.1.3 to §2.3.1.7) and the module's type record.</summary>
public enum VbaModuleKind
{
    /// <summary>A procedural module (<c>.bas</c>): <c>Module=</c> in the <c>PROJECT</c> stream, MODULETYPE 0x0021.</summary>
    Standard = 0,

    /// <summary>A class module (<c>.cls</c>): <c>Class=</c>.</summary>
    Class = 1,

    /// <summary>A document module such as <c>ThisWorkbook</c> or a sheet: <c>Document=</c>. Exported as <c>.cls</c>.</summary>
    Document = 2,

    /// <summary>A designer module, a user form (<c>.frm</c>): <c>BaseClass=</c>. Its designer storage is kept opaque.</summary>
    Designer = 3,
}

/// <summary>The platform a project was saved on ([MS-OVBA] §2.3.4.2.1.1).</summary>
public enum VbaSysKind
{
    /// <summary>16-bit Windows.</summary>
    Win16 = 0,

    /// <summary>32-bit Windows.</summary>
    Win32 = 1,

    /// <summary>Macintosh.</summary>
    Mac = 2,

    /// <summary>64-bit Windows.</summary>
    Win64 = 3,

    /// <summary>A value the specification does not define.</summary>
    Unknown = -1,
}

/// <summary>Which VBA signature streams accompany a project. The signatures are located, never verified.</summary>
[Flags]
public enum VbaSignatureKinds
{
    /// <summary>No signature stream.</summary>
    None = 0,

    /// <summary>The legacy signature (<c>\u0005DigitalSignature</c>).</summary>
    Legacy = 1,

    /// <summary>The agile signature (<c>\u0005DigitalSignatureEx</c>).</summary>
    Agile = 2,

    /// <summary>The V3 signature (<c>\u0005DigitalSignatureExt</c>).</summary>
    V3 = 4,
}

/// <summary>The kinds of reference a project holds ([MS-OVBA] §2.3.4.2.2).</summary>
public enum VbaReferenceKind
{
    /// <summary>A registered automation type library (REFERENCEREGISTERED).</summary>
    Registered = 0,

    /// <summary>Another VBA project (REFERENCEPROJECT).</summary>
    Project = 1,

    /// <summary>An ActiveX control's type library (REFERENCECONTROL).</summary>
    Control = 2,

    /// <summary>The original library a control reference was copied from (REFERENCEORIGINAL).</summary>
    Original = 3,
}

/// <summary>One reference of a project to an external library, control or project.</summary>
/// <param name="Kind">The record kind.</param>
/// <param name="Name">The reference's name (REFERENCENAME), or null when the record has none.</param>
/// <param name="Libid">The identifier string, for example <c>*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\System32\stdole2.tlb#OLE Automation</c>; for a project reference the absolute path.</param>
/// <param name="RelativeLibid">The relative path of a project reference, or null.</param>
/// <param name="MajorVersion">The major version of a project reference (0 for the other kinds).</param>
/// <param name="MinorVersion">The minor version of a project reference (0 for the other kinds).</param>
public sealed record VbaReference(VbaReferenceKind Kind, string? Name, string Libid, string? RelativeLibid, uint MajorVersion, ushort MinorVersion);

/// <summary>
/// The protection a project reports: the values of <c>CMG</c>, <c>DPB</c> and <c>GC</c> in the <c>PROJECT</c> stream, undone from the obfuscation
/// of [MS-OVBA] §2.4.3. The password hash is never extracted, compared or attacked: only whether there is one.
/// </summary>
/// <param name="ProtectionStateRaw">The <c>CMG</c> value as written (hexadecimal), or null when the stream has none.</param>
/// <param name="PasswordRaw">The <c>DPB</c> value as written, or null.</param>
/// <param name="VisibilityRaw">The <c>GC</c> value as written, or null.</param>
/// <param name="UserProtected">The user chose to protect the project (CMG bit A); false when CMG is absent or unreadable.</param>
/// <param name="HostProtected">The host application protected it (CMG bit B).</param>
/// <param name="EditorProtected">The VBA editor protected it (CMG bit C).</param>
/// <param name="HasPassword">DPB holds a password hash or password (anything but the one zero byte that means none).</param>
/// <param name="Visible">GC says the project is visible; true when GC is absent (the default is visible).</param>
/// <param name="Unreadable">One of the three values is present but cannot be undone; tools that lock a project without a password do this on purpose, so it counts as locked.</param>
public sealed record VbaProtection(
    string? ProtectionStateRaw,
    string? PasswordRaw,
    string? VisibilityRaw,
    bool UserProtected,
    bool HostProtected,
    bool EditorProtected,
    bool HasPassword,
    bool Visible,
    bool Unreadable)
{
    /// <summary>Gets a value indicating whether the project is locked for viewing: protected, passworded, hidden, or protected in a way that cannot be read.</summary>
    public bool IsLocked => UserProtected || HostProtected || EditorProtected || HasPassword || !Visible || Unreadable;

    /// <summary>The state of a project that reports no protection.</summary>
    public static VbaProtection None { get; } = new(null, null, null, false, false, false, false, true, false);
}
