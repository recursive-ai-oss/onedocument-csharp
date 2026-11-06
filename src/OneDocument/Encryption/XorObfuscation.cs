namespace RecursiveAI.OneDocument.Encryption;

/// <summary>
/// XOR obfuscation, the oldest Office password scheme ([MS-OFFCRYPTO] §2.3.7). Method 1 is what Excel uses (the <c>FilePass</c> record
/// carries a 16-bit key and verifier); Method 2 is what Word uses (a 32-bit verifier in the FIB). An instance holds the 16-byte
/// obfuscation array for one password, which is only known once a verifier has matched.
/// </summary>
public sealed class XorObfuscation
{
    // [MS-OFFCRYPTO] §2.3.7.2 constants.
    private static readonly byte[] PadArray = [0xBB, 0xFF, 0xFF, 0xBA, 0xFF, 0xFF, 0xB9, 0x80, 0x00, 0xBE, 0x0F, 0x00, 0xBF, 0x0F, 0x00];

    private static readonly ushort[] InitialCode = [0xE1F0, 0x1D0F, 0xCC9C, 0x84C0, 0x110C, 0x0E10, 0xF1CE, 0x313E, 0x1872, 0xE139, 0xD40F, 0x84F9, 0x280C, 0xA96A, 0x4EC3];

    private static readonly ushort[] XorMatrix =
    [
        0xAEFC, 0x4DD9, 0x9BB2, 0x2745, 0x4E8A, 0x9D14, 0x2A09, 0x7B61, 0xF6C2, 0xFDA5, 0xEB6B, 0xC6F7, 0x9DCF, 0x2BBF, 0x4563,
        0x8AC6, 0x05AD, 0x0B5A, 0x16B4, 0x2D68, 0x5AD0, 0x0375, 0x06EA, 0x0DD4, 0x1BA8, 0x3750, 0x6EA0, 0xDD40, 0xD849, 0xA0B3,
        0x5147, 0xA28E, 0x553D, 0xAA7A, 0x44D5, 0x6F45, 0xDE8A, 0xAD35, 0x4A4B, 0x9496, 0x390D, 0x721A, 0xEB23, 0xC667, 0x9CEF,
        0x29FF, 0x53FE, 0xA7FC, 0x5FD9, 0x47D3, 0x8FA6, 0x0F6D, 0x1EDA, 0x3DB4, 0x7B68, 0xF6D0, 0xB861, 0x60E3, 0xC1C6, 0x93AD,
        0x377B, 0x6EF6, 0xDDEC, 0x45A0, 0x8B40, 0x06A1, 0x0D42, 0x1A84, 0x3508, 0x6A10, 0xAA51, 0x4483, 0x8906, 0x022D, 0x045A,
        0x08B4, 0x1168, 0x76B4, 0xED68, 0xCAF1, 0x85C3, 0x1BA7, 0x374E, 0x6E9C, 0x3730, 0x6E60, 0xDCC0, 0xA9A1, 0x4363, 0x86C6,
        0x1DAD, 0x3331, 0x6662, 0xCCC4, 0x89A9, 0x0373, 0x06E6, 0x0DCC, 0x1021, 0x2042, 0x4084, 0x8108, 0x1231, 0x2462, 0x48C4,
    ];

    private readonly byte[] _array;

    private XorObfuscation(byte[] array)
    {
        _array = array;
    }

    /// <summary>
    /// Checks a password against a Method 1 key and verifier (an XLS <c>FilePass</c> record) and, when they match, returns the obfuscation.
    /// </summary>
    /// <param name="password">The password. Both single-byte conversions of it are tried ([MS-OFFCRYPTO] §2.3.7.4).</param>
    /// <param name="key">The record's <c>key</c> field.</param>
    /// <param name="verifier">The record's <c>verificationBytes</c> field.</param>
    /// <returns>The obfuscation, or null when the password does not match.</returns>
    public static XorObfuscation? TryUnlockMethod1(string password, ushort key, ushort verifier)
    {
        ArgumentNullException.ThrowIfNull(password);
        foreach (byte[] pw in EncryptionText.XorPasswordForms(password))
        {
            if (pw.Length > 0 && Verifier1(pw) == verifier && XorKey(pw) == key)
            {
                return new XorObfuscation(Array1(pw));
            }
        }

        return null;
    }

    /// <summary>
    /// Checks a password against a Method 2 verifier (the Word FIB's <c>lKey</c>) and, when it matches, returns the obfuscation.
    /// </summary>
    /// <param name="password">The password. Both single-byte conversions of it are tried.</param>
    /// <param name="verifier">The 32-bit verifier.</param>
    /// <returns>The obfuscation, or null when the password does not match.</returns>
    public static XorObfuscation? TryUnlockMethod2(string password, uint verifier)
    {
        ArgumentNullException.ThrowIfNull(password);
        foreach (byte[] pw in EncryptionText.XorPasswordForms(password))
        {
            if (pw.Length > 0 && Verifier2(pw) == verifier)
            {
                return new XorObfuscation(Array2(pw));
            }
        }

        return null;
    }

    /// <summary>Creates the Method 1 obfuscation for a password, with the key and verifier a <c>FilePass</c> record stores.</summary>
    /// <param name="password">The password; the first 15 single-byte characters of its least-significant-byte form are used.</param>
    /// <param name="key">The record's <c>key</c> field.</param>
    /// <param name="verifier">The record's <c>verificationBytes</c> field.</param>
    /// <returns>The obfuscation.</returns>
    /// <exception cref="ArgumentException">The password is empty.</exception>
    public static XorObfuscation CreateMethod1(string password, out ushort key, out ushort verifier)
    {
        ArgumentNullException.ThrowIfNull(password);
        byte[] pw = EncryptionText.XorPasswordForms(password)[0];
        if (pw.Length == 0)
        {
            throw new ArgumentException("The password is empty.", nameof(password));
        }

        key = XorKey(pw);
        verifier = Verifier1(pw);
        return new XorObfuscation(Array1(pw));
    }

    /// <summary>Creates the Method 2 obfuscation for a password, with the verifier a Word file stores.</summary>
    /// <param name="password">The password; the first 15 single-byte characters of its least-significant-byte form are used (the form Office writes).</param>
    /// <param name="verifier">The 32-bit verifier.</param>
    /// <returns>The obfuscation.</returns>
    /// <exception cref="ArgumentException">The password is empty.</exception>
    public static XorObfuscation CreateMethod2(string password, out uint verifier)
    {
        ArgumentNullException.ThrowIfNull(password);
        byte[] pw = EncryptionText.XorPasswordForms(password)[0];
        if (pw.Length == 0)
        {
            throw new ArgumentException("The password is empty.", nameof(password));
        }

        verifier = Verifier2(pw);
        return new XorObfuscation(Array2(pw));
    }

    /// <summary>Deobfuscates bytes written with Method 1: XOR with the array, then rotate right by 5 ([MS-OFFCRYPTO] §2.3.7.3).</summary>
    /// <param name="data">The bytes, changed in place.</param>
    /// <param name="arrayIndex">The index in the 16-byte array that the first byte uses (any non-negative number; it is taken modulo 16).</param>
    public void DecryptMethod1(Span<byte> data, int arrayIndex)
    {
        int idx = arrayIndex & 15;
        for (int i = 0; i < data.Length; i++)
        {
            byte v = (byte)(data[i] ^ _array[idx]);
            data[i] = (byte)((v >> 5) | (v << 3));
            idx = (idx + 1) & 15;
        }
    }

    /// <summary>Obfuscates bytes with Method 1 (the inverse of <see cref="DecryptMethod1"/>); used to build test files.</summary>
    /// <param name="data">The bytes, changed in place.</param>
    /// <param name="arrayIndex">The index in the array that the first byte uses.</param>
    public void EncryptMethod1(Span<byte> data, int arrayIndex)
    {
        int idx = arrayIndex & 15;
        for (int i = 0; i < data.Length; i++)
        {
            byte v = (byte)((data[i] << 5) | (data[i] >> 3));
            data[i] = (byte)(v ^ _array[idx]);
            idx = (idx + 1) & 15;
        }
    }

    /// <summary>
    /// Transforms bytes with Method 2, which is its own inverse: each byte is XORed with the array, except that a zero byte, or a byte
    /// that would become zero, is left as it is ([MS-OFFCRYPTO] §2.3.7.6).
    /// </summary>
    /// <param name="data">The bytes, changed in place.</param>
    /// <param name="arrayIndex">The index in the array that the first byte uses.</param>
    public void TransformMethod2(Span<byte> data, int arrayIndex)
    {
        int idx = arrayIndex & 15;
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            byte x = (byte)(b ^ _array[idx]);
            if (b != 0 && x != 0)
            {
                data[i] = x;
            }

            idx = (idx + 1) & 15;
        }
    }

    // ---- [MS-OFFCRYPTO] §2.3.7.1 to §2.3.7.5 ----

    private static ushort Verifier1(byte[] password)
    {
        ushort verifier = 0;
        for (int i = password.Length; i >= 0; i--)
        {
            byte b = i == 0 ? (byte)password.Length : password[i - 1];
            int carry = (verifier & 0x4000) == 0 ? 0 : 1;
            int shifted = (verifier * 2) & 0x7FFF;
            verifier = (ushort)((carry | shifted) ^ b);
        }

        return (ushort)(verifier ^ 0xCE4B);
    }

    private static ushort XorKey(byte[] password)
    {
        ushort key = InitialCode[password.Length - 1];
        int element = 0x68;
        for (int i = password.Length - 1; i >= 0; i--)
        {
            int c = password[i];
            for (int n = 0; n < 7; n++)
            {
                if ((c & 0x40) != 0)
                {
                    key ^= XorMatrix[element];
                }

                c <<= 1;
                element--;
            }
        }

        return key;
    }

    private static uint Verifier2(byte[] password) => ((uint)XorKey(password) << 16) | Verifier1(password);

    private static byte Ror(int b) => (byte)((b >> 1) | (b << 7));

    private static byte[] Array1(byte[] password)
    {
        ushort xorKey = XorKey(password);
        byte hi = (byte)(xorKey >> 8);
        byte lo = (byte)(xorKey & 0xFF);
        int len = password.Length;
        byte[] a = new byte[16];
        int index = len;
        if ((index & 1) == 1)
        {
            a[index] = Ror(PadArray[0] ^ hi);
            index--;
            a[index] = Ror(password[len - 1] ^ lo);
        }

        while (index > 0)
        {
            index--;
            a[index] = Ror(password[index] ^ hi);
            index--;
            a[index] = Ror(password[index] ^ lo);
        }

        index = 15;
        int pad = 15 - len;
        while (pad > 0)
        {
            a[index] = Ror(PadArray[pad] ^ hi);
            index--;
            pad--;
            a[index] = Ror(PadArray[pad] ^ lo);
            index--;
            pad--;
        }

        return a;
    }

    private static byte[] Array2(byte[] password)
    {
        ushort high = XorKey(password);
        byte keyHigh = (byte)(high >> 8);
        byte keyLow = (byte)(high & 0xFF);
        byte[] a = new byte[16];
        int index = 0;
        while (index < password.Length)
        {
            a[index] = password[index];
            index++;
        }

        while (index < 16)
        {
            a[index] = PadArray[index - password.Length];
            index++;
        }

        for (index = 0; index < 16; index += 2)
        {
            a[index] = Ror(a[index] ^ keyLow);
            a[index + 1] = Ror(a[index + 1] ^ keyHigh);
        }

        return a;
    }
}
