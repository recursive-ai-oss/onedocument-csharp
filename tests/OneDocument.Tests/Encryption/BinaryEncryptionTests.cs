using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Encryption;

namespace RecursiveAI.OneDocument.Tests.Encryption;

public class BinaryEncryptionTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s);

    // Published RC4 test vectors (key "Key" / "Wiki" / "Secret").
    [Theory]
    [InlineData("4B6579", "506C61696E74657874", "BBF316E8D940AF0AD3")]
    [InlineData("57696B69", "7065646961", "1021BF0420")]
    [InlineData("536563726574", "41747461636B206174206461776E", "45A01F645FC35B383552544B9BF5")]
    public void Rc4MatchesPublishedVectors(string key, string plain, string expected)
    {
        byte[] data = Hex(plain);
        new Rc4Cipher(Hex(key)).Process(data);
        Assert.Equal(Hex(expected), data);
    }

    [Fact]
    public void Rc4SkipAndProcessKeepTheSameKeyStream()
    {
        byte[] a = new byte[300];
        new Rc4Cipher([1, 2, 3, 4, 5]).Process(a);
        var c = new Rc4Cipher([1, 2, 3, 4, 5]);
        c.Skip(100);
        byte[] rest = new byte[200];
        c.Process(rest);
        Assert.Equal(a.AsSpan(100).ToArray(), rest);
        Assert.Throws<ArgumentException>(() => new Rc4Cipher([]));
    }

    // [MS-OFFCRYPTO] §3.10.1: an RC4 header (version 1.1). The specification does not name the password; it is found below.
    private static readonly byte[] Rc4Example = Hex(
        "01000100" + "C4DC856991 13EC1CF1E529060E4900B3".Replace(" ", "") + "F353BB80366 3CDE3DDF2D1CB10239B5A".Replace(" ", "") + "398FEAC243ECF44B9A62291B1A4C9DCD");

    [Fact]
    public void TheRc4ExampleInTheSpecificationOpensWithItsPassword()
    {
        // [MS-OFFCRYPTO] §3.10.1: an RC4 header (version 1.1). The specification does not give its password; it is "secret".
        BinaryRc4Encryption h = BinaryRc4Encryption.Parse(Rc4Example, default);
        Assert.Equal(BinaryEncryptionScheme.Rc4, h.Scheme);
        Assert.Equal(52, h.Length);
        Assert.False(h.TryUnlock("Secret"));
        Assert.True(h.TryUnlock("secret"));
        Assert.Throws<NotSupportedException>(h.ToBytes);
    }

    [Fact]
    public void Rc4HeaderRoundTripsThroughCreateAndParse()
    {
        byte[] salt = Hex("00112233445566778899AABBCCDDEEFF");
        byte[] verifier = Hex("FFEEDDCCBBAA99887766554433221100");
        BinaryRc4Encryption made = BinaryRc4Encryption.Create("hunter2", salt, verifier, 128);
        byte[] bytes = made.ToBytes();
        BinaryRc4Encryption parsed = BinaryRc4Encryption.Parse(bytes, default);
        Assert.Equal(BinaryEncryptionScheme.Rc4CryptoApi, parsed.Scheme);
        Assert.Equal(128, parsed.KeyBits);
        Assert.True(parsed.DocumentPropertiesClear);
        Assert.Equal(bytes.Length, parsed.Length);
        Assert.False(parsed.TryUnlock("hunter3"));
        Assert.False(parsed.IsUnlocked);
        Assert.True(parsed.TryUnlock("hunter2"));

        byte[] data = [10, 20, 30, 40];
        parsed.CreateCipher(5).Process(data);
        made.CreateCipher(5).Process(data);
        Assert.Equal(new byte[] { 10, 20, 30, 40 }, data);
        Assert.Throws<InvalidOperationException>(() => BinaryRc4Encryption.Parse(bytes, default).CreateCipher(0));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(56)]
    [InlineData(128)]
    public void KeyLengthsAreHonoured(int bits)
    {
        BinaryRc4Encryption made = BinaryRc4Encryption.Create("pw", new byte[16], new byte[16], bits);
        BinaryRc4Encryption parsed = BinaryRc4Encryption.Parse(made.ToBytes(), default);
        Assert.Equal(bits, parsed.KeyBits);
        Assert.True(parsed.TryUnlock("pw"));
    }

    [Fact]
    public void BlockCipherKeepsTheKeyStreamAcrossClearBytesAndRekeysAtBlocks()
    {
        BinaryRc4Encryption enc = BinaryRc4Encryption.Create("pw", new byte[16], new byte[16], 128);
        byte[] data = new byte[2500];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 31);
        }

        byte[] copy = (byte[])data.Clone();
        var e = new Rc4BlockCipher(enc, 1024);
        e.Skip(10);
        e.Process(copy.AsSpan(10, 1500));
        e.Skip(990);
        Assert.Equal(2500, e.Position);
        Assert.Equal(data.AsSpan(0, 10).ToArray(), copy.AsSpan(0, 10).ToArray());
        Assert.NotEqual(data.AsSpan(10, 1500).ToArray(), copy.AsSpan(10, 1500).ToArray());
        var d = new Rc4BlockCipher(enc, 1024);
        d.Skip(10);
        d.Process(copy.AsSpan(10, 1500));
        Assert.Equal(data, copy);

        // The second block starts at byte 1024 with the block 1 key, whatever came before.
        byte[] direct = data.AsSpan(1024, 100).ToArray();
        enc.CreateCipher(1).Process(direct);
        byte[] viaBlock = data.AsSpan(1024, 100).ToArray();
        var c = new Rc4BlockCipher(enc, 1024);
        c.Skip(1024);
        c.Process(viaBlock);
        Assert.Equal(direct, viaBlock);
    }

    [Fact]
    public void HeadersAreCheckedAndFailuresAreTyped()
    {
        BinaryRc4Encryption made = BinaryRc4Encryption.Create("pw", new byte[16], new byte[16], 128);
        byte[] good = made.ToBytes();
        DefectLocation at = DefectLocation.InRecord("/Table", 12);

        foreach (byte[] bad in new[]
        {
            Array.Empty<byte>(),
            good.AsSpan(0, 3).ToArray(),
            good.AsSpan(0, 11).ToArray(),
            good.AsSpan(0, 40).ToArray(),
            good.AsSpan(0, good.Length - 1).ToArray(),
            new byte[] { 1, 0, 1, 0, 1, 2, 3 },
        })
        {
            var ex = Assert.Throws<OfficeFormatException>(() => BinaryRc4Encryption.Parse(bad, at));
            Assert.Equal("encryption.malformed-header", ex.Defect.Code);
            Assert.Equal(DefectKind.Encryption, ex.Defect.Kind);
            Assert.Equal("/Table", ex.Location.Container);
        }

        byte[] hugeHeader = (byte[])good.Clone();
        BitConverter.TryWriteBytes(hugeHeader.AsSpan(8), 0x7FFFFFFFu);
        Assert.Equal("encryption.malformed-header", Assert.Throws<OfficeFormatException>(() => BinaryRc4Encryption.Parse(hugeHeader, at)).Defect.Code);

        byte[] badKey = (byte[])good.Clone();
        BitConverter.TryWriteBytes(badKey.AsSpan(12 + 16), 200u);
        Assert.Equal("encryption.malformed-header", Assert.Throws<OfficeFormatException>(() => BinaryRc4Encryption.Parse(badKey, at)).Defect.Code);
    }

    [Fact]
    public void UnsupportedSchemesAreNamed()
    {
        BinaryRc4Encryption made = BinaryRc4Encryption.Create("pw", new byte[16], new byte[16], 128);
        byte[] good = made.ToBytes();
        byte[] aes = (byte[])good.Clone();
        BitConverter.TryWriteBytes(aes.AsSpan(12 + 8), 0x660Eu);
        Assert.Equal("AES encryption in an Office binary file", Assert.Throws<OfficeUnsupportedException>(() => BinaryRc4Encryption.Parse(aes, default)).Feature);
        byte[] version = (byte[])good.Clone();
        version[0] = 7;
        Assert.Equal("binary encryption header version 7.2", Assert.Throws<OfficeUnsupportedException>(() => BinaryRc4Encryption.Parse(version, default)).Feature);
        byte[] hash = (byte[])good.Clone();
        BitConverter.TryWriteBytes(hash.AsSpan(12 + 12), 0x8003u);
        Assert.StartsWith("CryptoAPI hash 0x8003", Assert.Throws<OfficeUnsupportedException>(() => BinaryRc4Encryption.Parse(hash, default)).Feature, StringComparison.Ordinal);
    }

    [Fact]
    public void XorVerifiersRoundTripAndRejectOtherPasswords()
    {
        XorObfuscation m1 = XorObfuscation.CreateMethod1("secret", out ushort key, out ushort ver);
        Assert.NotNull(XorObfuscation.TryUnlockMethod1("secret", key, ver));
        Assert.Null(XorObfuscation.TryUnlockMethod1("Secret", key, ver));
        Assert.Null(XorObfuscation.TryUnlockMethod1("secret", (ushort)(key ^ 1), ver));

        byte[] data = [1, 2, 3, 250, 0, 7, 9, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43];
        byte[] copy = (byte[])data.Clone();
        m1.EncryptMethod1(copy, 5);
        Assert.NotEqual(data, copy);
        XorObfuscation.TryUnlockMethod1("secret", key, ver)!.DecryptMethod1(copy, 5);
        Assert.Equal(data, copy);

        XorObfuscation m2 = XorObfuscation.CreateMethod2("secret", out uint v2);
        Assert.NotNull(XorObfuscation.TryUnlockMethod2("secret", v2));
        Assert.Null(XorObfuscation.TryUnlockMethod2("other", v2));
        byte[] two = (byte[])data.Clone();
        m2.TransformMethod2(two, 3);
        XorObfuscation.TryUnlockMethod2("secret", v2)!.TransformMethod2(two, 3);
        Assert.Equal(data, two);

        // Passwords are cut to 15 characters ([MS-OFFCRYPTO] §2.3.7.4).
        _ = XorObfuscation.CreateMethod2("0123456789abcdefXYZ", out uint longVerifier);
        Assert.NotNull(XorObfuscation.TryUnlockMethod2("0123456789abcdefQRS", longVerifier));
    }

    /// <summary>Builds an encrypted summary stream the way [MS-OFFCRYPTO] §2.3.5.4 writes it: data first, then the descriptor array, then the two offsets.</summary>
    public static byte[] BuildEncryptedSummary(BinaryRc4Encryption enc, params (string Name, byte[] Data)[] streams)
    {
        var body = new List<byte>();
        body.AddRange(new byte[8]);
        var descriptors = new List<byte>();
        ushort block = 0;
        foreach ((string name, byte[] data) in streams)
        {
            byte[] cipher = (byte[])data.Clone();
            enc.CreateCipher(block).Process(cipher);
            int offset = body.Count;
            body.AddRange(cipher);
            descriptors.AddRange(BitConverter.GetBytes((uint)offset));
            descriptors.AddRange(BitConverter.GetBytes((uint)data.Length));
            descriptors.AddRange(BitConverter.GetBytes(block));
            descriptors.Add((byte)name.Length);
            descriptors.Add(1);
            descriptors.AddRange(new byte[4]);
            descriptors.AddRange(System.Text.Encoding.Unicode.GetBytes(name + "\0"));
            block++;
        }

        byte[] array = [.. BitConverter.GetBytes((uint)streams.Length), .. descriptors];
        int arrayOffset = body.Count;
        enc.CreateCipher(0).Process(array);
        body.AddRange(array);
        byte[] head = [.. BitConverter.GetBytes((uint)arrayOffset), .. BitConverter.GetBytes((uint)array.Length)];
        enc.CreateCipher(0).Process(head);
        byte[] result = body.ToArray();
        head.CopyTo(result, 0);
        return result;
    }

    [Fact]
    public void AnEncryptedSummaryStreamGivesBackItsStreams()
    {
        // [MS-OFFCRYPTO] §2.3.5.4: each stream has its own block key; the descriptors and the two leading fields use block 0.
        BinaryRc4Encryption enc = BinaryRc4Encryption.Create("secret", new byte[16], new byte[16]);
        byte[] summary = [1, 2, 3, 4, 5, 6, 7];
        byte[] documentSummary = new byte[300];
        new Random(5).NextBytes(documentSummary);
        byte[] stream = BuildEncryptedSummary(enc, ("\u0005SummaryInformation", summary), ("\u0005DocumentSummaryInformation", documentSummary));

        Assert.True(enc.TryReadEncryptedSummary(stream, out IReadOnlyList<KeyValuePair<string, byte[]>> streams));
        Assert.Equal(["\u0005SummaryInformation", "\u0005DocumentSummaryInformation"], streams.Select(s => s.Key));
        Assert.Equal(summary, streams[0].Value);
        Assert.Equal(documentSummary, streams[1].Value);
    }

    [Fact]
    public void AnEncryptedSummaryStreamThatLiesAboutItsSizesIsRefused()
    {
        BinaryRc4Encryption enc = BinaryRc4Encryption.Create("secret", new byte[16], new byte[16]);
        byte[] stream = BuildEncryptedSummary(enc, ("a", new byte[10]));
        Assert.False(enc.TryReadEncryptedSummary(stream.AsSpan(0, stream.Length - 5), out _));
        Assert.False(enc.TryReadEncryptedSummary(new byte[5], out _));
        Assert.False(enc.TryReadEncryptedSummary(new byte[64], out IReadOnlyList<KeyValuePair<string, byte[]>> none));
        Assert.Empty(none);
    }
}
