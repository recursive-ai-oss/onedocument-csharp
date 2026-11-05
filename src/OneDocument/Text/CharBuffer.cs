using System.Text.Unicode;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.TextExtraction;

/// <summary>A growable run of UTF-16 text that is reused from paragraph to paragraph, so extraction allocates nothing per paragraph.</summary>
internal sealed class CharBuffer(long maxBytes)
{
    private char[] _chars = new char[256];
    private byte[] _scratch = [];
    private int _length;

    public int Length => _length;

    public ReadOnlySpan<char> AsSpan() => _chars.AsSpan(0, _length);

    public void Clear() => _length = 0;

    public void Append(char c)
    {
        Ensure(1);
        _chars[_length++] = c;
    }

    public void Append(ReadOnlySpan<char> text)
    {
        Ensure(text.Length);
        text.CopyTo(_chars.AsSpan(_length));
        _length += text.Length;
    }

    public void Insert(string prefix)
    {
        Ensure(prefix.Length);
        Array.Copy(_chars, 0, _chars, prefix.Length, _length);
        prefix.CopyTo(0, _chars, 0, prefix.Length);
        _length += prefix.Length;
    }

    /// <summary>Appends the character data of a text token: references decoded and line ends normalised, then UTF-8 to UTF-16.</summary>
    /// <param name="raw">The token's raw content.</param>
    /// <param name="literal">True for a CDATA section, which has no references.</param>
    public void AppendXml(ReadOnlySpan<byte> raw, bool literal = false)
    {
        // The tokenizer has validated the bytes, and a UTF-16 unit never takes more than one byte of UTF-8 (a supplementary character is
        // four bytes and two units), so raw.Length chars always fit.
        ReadOnlySpan<byte> utf8 = raw;
        if (!literal && XmlText.NeedsDecoding(raw))
        {
            if (_scratch.Length < raw.Length)
            {
                _scratch = new byte[Math.Max(raw.Length, 512)];
            }

            int n = XmlText.Decode(raw, _scratch);
            utf8 = _scratch.AsSpan(0, n);
        }

        Ensure(utf8.Length);
        Utf8.ToUtf16(utf8, _chars.AsSpan(_length), out _, out int written, replaceInvalidSequences: true, isFinalBlock: true);
        _length += written;
    }

    public override string ToString() => new(_chars, 0, _length);

    private void Ensure(int extra)
    {
        long needed = (long)_length + extra;
        if (needed <= _chars.Length)
        {
            return;
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), maxBytes, needed * 2);
        Array.Resize(ref _chars, (int)Math.Min(Math.Max(needed, _chars.Length * 2L), int.MaxValue / 2));
    }
}
