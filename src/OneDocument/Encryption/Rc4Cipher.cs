namespace RecursiveAI.OneDocument.Encryption;

/// <summary>
/// The RC4 stream cipher, which the .NET libraries do not provide. Office's binary formats (RC4 and RC4 CryptoAPI encryption) are built on
/// it. Encryption and decryption are the same operation. An instance is not safe for concurrent use.
/// </summary>
/// <remarks>
/// RC4 is cryptographically weak ([MS-OFFCRYPTO] §4.1.3.2 lists its weaknesses in this use); it is here only so that existing
/// documents can be read and, where Excel still produces them, written. Keys are 1 to 256 bytes.
/// </remarks>
public sealed class Rc4Cipher
{
    private readonly byte[] _state = new byte[256];
    private int _i;
    private int _j;

    /// <summary>Creates a cipher positioned at the start of its key stream.</summary>
    /// <param name="key">The key, 1 to 256 bytes.</param>
    /// <exception cref="ArgumentException">The key is empty or longer than 256 bytes.</exception>
    public Rc4Cipher(ReadOnlySpan<byte> key)
    {
        if (key.Length is 0 or > 256)
        {
            throw new ArgumentException("An RC4 key is 1 to 256 bytes.", nameof(key));
        }

        for (int n = 0; n < 256; n++)
        {
            _state[n] = (byte)n;
        }

        int j = 0;
        for (int n = 0; n < 256; n++)
        {
            j = (j + _state[n] + key[n % key.Length]) & 0xFF;
            (_state[n], _state[j]) = (_state[j], _state[n]);
        }
    }

    /// <summary>Transforms bytes in place (encrypts plaintext or decrypts ciphertext) and advances the key stream.</summary>
    /// <param name="data">The bytes to transform.</param>
    public void Process(Span<byte> data)
    {
        byte[] s = _state;
        int i = _i;
        int j = _j;
        for (int n = 0; n < data.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            data[n] ^= s[(s[i] + s[j]) & 0xFF];
        }

        _i = i;
        _j = j;
    }

    /// <summary>Advances the key stream without transforming anything. Office does this for the bytes it leaves in the clear.</summary>
    /// <param name="count">The number of key stream bytes to discard.</param>
    public void Skip(int count)
    {
        byte[] s = _state;
        int i = _i;
        int j = _j;
        for (int n = 0; n < count; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        _i = i;
        _j = j;
    }
}
