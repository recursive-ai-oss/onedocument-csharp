using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Encryption;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

// EncryptionProbe: finds the password-protected files in a directory and says how each is protected and what opening it gives.
//   EncryptionProbe scan <dir> [passwords.txt]
// One JSON line per encrypted file: path, format, scheme, and the outcome with no password (the defaults for files that are only
// protected against editing are tried by the readers) and, when a password list is given, with each password in it. The last line
// counts the files looked at. Nothing is written; the files are only read.
RecursiveAI.OneDocument.Documents.DocumentFormats.Register(FormatRegistry.Default);
RecursiveAI.OneDocument.Spreadsheets.SpreadsheetFormats.Register(FormatRegistry.Default);
RecursiveAI.OneDocument.Presentations.PresentationFormats.Register(FormatRegistry.Default);
return Run(args);

static int Run(string[] args)
{
    if (args.Length < 2 || args[0] != "scan")
    {
        Console.Error.WriteLine("usage: EncryptionProbe scan <dir> [passwords.txt]");
        return 2;
    }

    string[] passwords = args.Length > 2 ? File.ReadAllLines(args[2]).Where(l => l.Length > 0).ToArray() : [];
    int looked = 0;
    int encrypted = 0;
    var byScheme = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (string file in Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
    {
        looked++;
        OfficeFormat format;
        try
        {
            format = FormatSniffer.Detect(file);
        }
        catch (Exception ex) when (ex is OfficeException or IOException)
        {
            continue;
        }

        string? scheme = SchemeOf(file, format);
        if (scheme is null)
        {
            continue;
        }

        encrypted++;
        byScheme[scheme] = byScheme.GetValueOrDefault(scheme) + 1;
        string noPassword = Outcome(file, format, null);
        string? opened = null;
        foreach (string pw in passwords)
        {
            if (!Outcome(file, format, pw).StartsWith("encrypted:", StringComparison.Ordinal))
            {
                opened = pw.Length == 0 ? string.Empty : "(a listed password)";
                break;
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["path"] = Path.GetRelativePath(args[1], file),
            ["format"] = format.ToString(),
            ["scheme"] = scheme,
            ["noPassword"] = noPassword,
            ["listed"] = opened,
        }));
    }

    Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["files"] = looked, ["encrypted"] = encrypted, ["schemes"] = byScheme }));
    return 0;
}

// The scheme a file declares, read without decrypting anything; null when it is not protected.
static string? SchemeOf(string path, OfficeFormat format)
{
    try
    {
        using IByteSource source = ByteSource.FromFile(path);
        if (format == OfficeFormat.EncryptedPackage)
        {
            using CfbPackage cfb = CfbPackage.Open(source, new OpenContext());
            PackageEncryptionInfo info = PackageEncryption.Describe(cfb.FindPart("/EncryptionInfo")!.GetBytes().Span);
            return $"{info.Scheme} {info.VersionMajor}.{info.VersionMinor} aes-{info.KeyBits} {info.HashAlgorithm} spin {info.SpinCount}";
        }

        if (format is not (OfficeFormat.Xls or OfficeFormat.XlsLegacy or OfficeFormat.Doc or OfficeFormat.Ppt))
        {
            return null;
        }

        using CfbPackage file = CfbPackage.Open(source, new OpenContext());
        if (format is OfficeFormat.Xls or OfficeFormat.XlsLegacy)
        {
            byte[]? wb = (file.FindPart("/Workbook") ?? file.FindPart("/Book"))?.GetBytes().ToArray();
            if (wb is null || !XlsEncryption.TryFindFilePass(wb, out int at))
            {
                return null;
            }

            int type = BinaryPrimitives.ReadUInt16LittleEndian(wb.AsSpan(at + 4));
            if (type == 0)
            {
                return "xls xor";
            }

            BinaryRc4Encryption h = BinaryRc4Encryption.Parse(wb.AsSpan(at + 6), default);
            return "xls " + Describe(h);
        }

        if (format == OfficeFormat.Doc)
        {
            byte[]? word = file.FindPart("/WordDocument")?.GetBytes().ToArray();
            if (word is null || word.Length < 18 || (BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(10)) & 0x0100) == 0)
            {
                return null;
            }

            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(10));
            if ((flags & 0x8000) != 0)
            {
                return "doc xor";
            }

            byte[]? table = (file.FindPart((flags & 0x0200) != 0 ? "/1Table" : "/0Table"))?.GetBytes().ToArray();
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(word.AsSpan(14));
            return table is null || key > table.Length ? "doc ?" : "doc " + Describe(BinaryRc4Encryption.Parse(table.AsSpan(0, (int)key), default));
        }

        byte[]? cu = file.FindPart("/Current User")?.GetBytes().ToArray();
        return cu is { Length: >= 16 } && BinaryPrimitives.ReadUInt32LittleEndian(cu.AsSpan(12)) == 0xF3D1C4DF ? "ppt rc4-cryptoapi" : null;
    }
    catch (OfficeUnsupportedException ex)
    {
        return "unsupported: " + ex.Feature;
    }
    catch (Exception ex) when (ex is OfficeException or IOException)
    {
        return null;
    }
}

static string Describe(BinaryRc4Encryption h) =>
    h.Scheme == BinaryEncryptionScheme.Rc4 ? "rc4-40 (1.1)" : $"rc4-cryptoapi {h.KeyBits}-bit{(h.DocumentPropertiesClear ? string.Empty : ", properties encrypted")}";

static string Outcome(string path, OfficeFormat format, string? password)
{
    var context = new OpenContext(password: password);
    try
    {
        using OfficeFile file = Office.Open(path, context);
        return "opens";
    }
    catch (OfficeEncryptedException ex)
    {
        return "encrypted:" + (ex.Problem == EncryptionProblem.PasswordRequired ? "password-required" : "password-incorrect");
    }
    catch (OfficeException ex)
    {
        return "typed:" + ex.Defect.Code;
    }
}
