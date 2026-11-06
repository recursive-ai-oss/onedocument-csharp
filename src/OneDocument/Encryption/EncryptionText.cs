using System.Text;

namespace RecursiveAI.OneDocument.Encryption;

/// <summary>How passwords become bytes for the schemes that take them.</summary>
internal static class EncryptionText
{
    /// <summary>[MS-OFFCRYPTO] §2.3.5.2: passwords are limited to 255 Unicode characters; the key derivations hash them as UTF-16LE.</summary>
    internal const int MaxPasswordChars = 255;

    internal static byte[] Utf16(string password)
    {
        ReadOnlySpan<char> p = password.Length > MaxPasswordChars ? password.AsSpan(0, MaxPasswordChars) : password;
        return Encoding.Unicode.GetBytes(p.ToString());
    }

    /// <summary>
    /// The single-byte strings an XOR obfuscation password can have been converted to ([MS-OFFCRYPTO] §2.3.7.4): the least significant byte
    /// of each character, or the most significant one when that is zero; and the Windows-1252-like ANSI form, approximated by Latin-1.
    /// Each is cut to 15 bytes. Readers try both and accept either.
    /// </summary>
    internal static List<byte[]> XorPasswordForms(string password)
    {
        var low = new List<byte>(15);
        var ansi = new List<byte>(15);
        foreach (char c in password)
        {
            if (low.Count >= 15)
            {
                break;
            }

            byte lsb = (byte)(c & 0xFF);
            low.Add(lsb != 0 ? lsb : (byte)(c >> 8));
            ansi.Add(c <= 0xFF ? (byte)c : (byte)'?');
        }

        var forms = new List<byte[]> { low.ToArray() };
        if (!low.ToArray().AsSpan().SequenceEqual(ansi.ToArray()))
        {
            forms.Add(ansi.ToArray());
        }

        return forms;
    }
}
