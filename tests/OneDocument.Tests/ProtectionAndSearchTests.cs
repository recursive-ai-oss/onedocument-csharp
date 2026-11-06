using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Protection;

namespace RecursiveAI.OneDocument.Tests;

public class ProtectionAndSearchTests
{
    [Fact]
    public void TheLegacyWordKeyMatchesTheSpecificationsExample()
    {
        // ECMA-376 Part 4 §14.8.2.3 works the password "Example" through to the key 0x64CEED7E.
        Assert.Equal(0x64CEED7Eu, PasswordVerifier.WordKey("Example"));
    }

    [Fact]
    public void TheIteratedHashFollowsTheSpecificationText()
    {
        // ECMA-376 Part 1 §17.15.1.29: H0 = H(salt + UTF-16LE password); Hn = H(Hn-1 + 4-byte little-endian iteration number).
        PasswordVerifier v = PasswordVerifier.Create("pässword", ProtectionHash.Sha512, 7, [1, 2, 3], "ctx");
        byte[] h = SHA512.HashData([.. v.Salt.ToArray(), .. Encoding.Unicode.GetBytes("pässword")]);
        for (int i = 0; i < 7; i++)
        {
            h = SHA512.HashData([.. h, .. BitConverter.GetBytes(i)]);
        }

        Assert.Equal(h, v.Hash.ToArray());
        Assert.Equal(16, v.Salt.Length);
        Assert.Equal("SHA-512", v.AlgorithmName);
        Assert.Equal(PasswordCheck.Match, v.Check("pässword"));
        Assert.Equal(PasswordCheck.Mismatch, v.Check("password"));
        // a leading byte order mark is dropped before hashing
        Assert.Equal(PasswordCheck.Match, v.Check("\uFEFFpässword"));
        Assert.Equal(PasswordVerifier.Create("pässword", ProtectionHash.Sha512, 7, [1, 2, 3], "ctx").HashBase64, v.HashBase64);
        Assert.NotEqual(PasswordVerifier.Create("pässword", ProtectionHash.Sha512, 7, [1, 2, 4], "ctx").SaltBase64, v.SaltBase64);
        Assert.NotEqual(PasswordVerifier.Create("pässword", ProtectionHash.Sha512, 7, [1, 2, 3], "other").SaltBase64, v.SaltBase64);
    }

    [Fact]
    public void AttributesReadBackAndBadOnesAreRefused()
    {
        PasswordVerifier v = PasswordVerifier.Create("x", ProtectionHash.Sha256, 3);
        Assert.True(PasswordVerifier.TryRead(v.AlgorithmName, v.HashBase64, v.SaltBase64, "3", out PasswordVerifier? back));
        Assert.Equal(PasswordCheck.Match, back!.Check("x"));
        Assert.False(PasswordVerifier.TryRead("SHA-256", "***", null, null, out _));
        Assert.False(PasswordVerifier.TryRead(null, "AAAA", null, null, out _));
        Assert.Throws<ArgumentException>(() => PasswordVerifier.Create(string.Empty));
        Assert.Throws<OfficeLimitException>(() => new PasswordVerifier("SHA-1", new byte[20], [], 20_000_000).Check("x"));
    }

    [Fact]
    public void SearchModesAndReplacementExpansion()
    {
        TextSearch lit = TextSearch.Create("a.c");
        Assert.Equal(2, lit.Find("A.C a.c abc").Count);
        TextSearch word = TextSearch.Create("cat", new SearchOptions { WholeWord = true, MatchCase = true });
        Assert.Equal(2, word.Find("cat cat concat Cat cat_").Count);
        TextSearch rx = TextSearch.Create("(?<k>\\w+)=(?<v>\\d+)", new SearchOptions { UseRegex = true });
        TextMatch m = rx.Find("a=1, b=22")[1];
        Assert.Equal("b=22", m.Value);
        Assert.Equal("22:b", rx.Expand(m, "${v}:${k}"));
        // literal searches take the replacement as written
        Assert.Equal("$1", lit.Expand(lit.Find("a.c")[0], "$1"));
        // empty matches are skipped
        Assert.Empty(TextSearch.Create("x*", new SearchOptions { UseRegex = true }).Find("abc"));
        TextSearch codes = TextSearch.Create("a^tb^^", new SearchOptions { SpecialCodes = true });
        TextMatch cm = Assert.Single(codes.Find("xa\tb^y"));
        Assert.Equal("a\tb^", cm.Value);
        Assert.Equal("[a\tb^]\n", codes.Expand(cm, "[^&]^l"));
        Assert.Throws<ArgumentException>(() => TextSearch.Create("a^p", new SearchOptions { SpecialCodes = true }));
        Assert.Throws<ArgumentException>(() => TextSearch.Create(string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => TextSearch.Create("(", new SearchOptions { UseRegex = true }));
    }
}
