using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Encryption;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Tests.Encryption;

public class PackageEncryptionTests
{
    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "testdata", "encryption", name));

    private const string SpecExample = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<encryption xmlns=\"http://schemas.microsoft.com/office/2006/encryption\" xmlns:p=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\"><keyData saltSize=\"16\" blockSize=\"16\" keyBits=\"128\" hashSize=\"20\" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA1\" saltValue=\"/a4iWqPyIvE2cUolJMKrIw==\"/><dataIntegrity encryptedHmacKey=\"uwpAEFW1hQyD2O01kz1lhjevNw0ECyAA0u2OxDygsfY=\" encryptedHmacValue=\"uf6HbJjtryJOjSFqrkqkNQY9NjNQUPI+xck8Q8y4mko=\"/><keyEncryptors><keyEncryptor uri=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\"><p:encryptedKey spinCount=\"100000\" saltSize=\"16\" blockSize=\"16\" keyBits=\"128\" hashSize=\"20\" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA1\" saltValue=\"pps6B1bmqCFXgopsm1rWnQ==\" encryptedVerifierHashInput=\"JYU4Q0u2BhqzQA5D4J/voA==\" encryptedVerifierHashValue=\"eB2jX5mvhBJ+9O7ffC+6X2Mydz2glHOXx0T9Pn6nK+w=\" encryptedKeyValue=\"2F86HG+xV3nGa27DElgqgw==\"/></keyEncryptor></keyEncryptors></encryption>";

    private static byte[] SpecInfo(string? xml = null)
    {
        byte[] x = System.Text.Encoding.UTF8.GetBytes(xml ?? SpecExample);
        byte[] info = new byte[8 + x.Length];
        info[0] = 4;
        info[2] = 4;
        info[4] = 0x40;
        x.CopyTo(info, 8);
        return info;
    }

    // A one-block package of 16 plain bytes is enough for the verifier tests: the key is only proved by the verifier.
    private static readonly byte[] TinyPackage = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void TheExampleInTheSpecificationOpensWithItsPassword()
    {
        // [MS-OFFCRYPTO] §3.11: an agile descriptor (AES-128, SHA-1, 100,000 iterations). The specification does not give its password; it is "a".
        byte[] plain = PackageEncryption.Decrypt(SpecInfo(), TinyPackage, new OpenContext(password: "a"));
        Assert.Single(plain);
        var wrong = Assert.Throws<OfficeEncryptedException>(() => PackageEncryption.Decrypt(SpecInfo(), TinyPackage, new OpenContext(password: "b")));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, wrong.Problem);
        var none = Assert.Throws<OfficeEncryptedException>(() => PackageEncryption.Decrypt(SpecInfo(), TinyPackage, new OpenContext()));
        Assert.Equal(EncryptionProblem.PasswordRequired, none.Problem);
        PackageEncryptionInfo d = PackageEncryption.Describe(SpecInfo());
        Assert.Equal(new PackageEncryptionInfo(PackageEncryptionScheme.Agile, 4, 4, 128, "SHA1", 100000), d);
    }

    [Fact]
    public void AgilePackageFromAComparatorLibraryDecryptsAndItsIntegrityCheckPasses()
    {
        var defects = new DefectCollection();
        using var source = ByteSource.FromFile(Fixture("secret-epplus.xlsx"));
        byte[] zip = PackageEncryption.DecryptCompoundFile(source, new OpenContext(defects: defects, password: "Epp lus-1"));
        Assert.Equal((byte)'P', zip[0]);
        Assert.Equal((byte)'K', zip[1]);
        Assert.DoesNotContain(defects, x => x.Code == "encryption.integrity-mismatch");
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.Equal(PackageEncryptionScheme.Agile, Describe("secret-epplus.xlsx").Scheme);
        Assert.Equal("SHA512", Describe("secret-epplus.xlsx").HashAlgorithm);
        Assert.Equal(256, Describe("secret-epplus.xlsx").KeyBits);
    }

    private static PackageEncryptionInfo Describe(string name)
    {
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromFile(Fixture(name)), new OpenContext());
        return PackageEncryption.Describe(cfb.FindPart("/EncryptionInfo")!.GetBytes().Span);
    }

    [Fact]
    public void ADamagedAgilePackageIsStillReturnedWithAnIntegrityDefect()
    {
        byte[] file = File.ReadAllBytes(Fixture("secret-epplus.xlsx"));
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(file), new OpenContext());
        byte[] encrypted = cfb.FindPart("/EncryptedPackage")!.GetBytes().ToArray();
        encrypted[encrypted.Length / 2] ^= 0x40;
        byte[] info = cfb.FindPart("/EncryptionInfo")!.GetBytes().ToArray();
        var defects = new DefectCollection();
        byte[] plain = PackageEncryption.Decrypt(info, encrypted, new OpenContext(defects: defects, password: "Epp lus-1"));
        Assert.NotEmpty(plain);
        Defect d = Assert.Single(defects, x => x.Code == "encryption.integrity-mismatch");
        Assert.Equal(DefectKind.Encryption, d.Kind);
    }

    [Fact]
    public void StandardPackageFromAWordProcessorDecrypts()
    {
        using var source = ByteSource.FromFile(Fixture("secret-basic.docx"));
        byte[] zip = PackageEncryption.DecryptCompoundFile(source, new OpenContext(password: "secret"));
        Assert.Equal((byte)'P', zip[0]);
        Assert.Equal(PackageEncryptionScheme.Standard, Describe("secret-basic.docx").Scheme);
        using var again = ByteSource.FromFile(Fixture("secret-basic.docx"));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, Assert.Throws<OfficeEncryptedException>(() => PackageEncryption.DecryptCompoundFile(again, new OpenContext(password: "Secret"))).Problem);
    }

    [Theory]
    [InlineData("cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA1\" saltValue=\"/a4i", "cipherAlgorithm=\"3DES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA1\" saltValue=\"/a4i", "agile encryption cipher 3DES")]
    [InlineData("hashAlgorithm=\"SHA1\" saltValue=\"/a4i", "hashAlgorithm=\"WHIRLPOOL\" saltValue=\"/a4i", "agile encryption hash WHIRLPOOL")]
    [InlineData("cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA1\" saltValue=\"/a4i", "cipherChaining=\"ChainingModeXYZ\" hashAlgorithm=\"SHA1\" saltValue=\"/a4i", "agile encryption chaining mode ChainingModeXYZ")]
    public void UnsupportedAlgorithmsAreNamed(string find, string replace, string feature)
    {
        string xml = SpecExample.Replace(find, replace, StringComparison.Ordinal);
        Assert.NotEqual(SpecExample, xml);
        var ex = Assert.Throws<OfficeUnsupportedException>(() => PackageEncryption.Describe(SpecInfo(xml)));
        Assert.Equal(feature, ex.Feature);
        Assert.Equal("unsupported.feature", ex.Defect.Code);
    }

    [Fact]
    public void ASpinCountAboveTheLimitIsRefusedBeforeAnyHashing()
    {
        string xml = SpecExample.Replace("spinCount=\"100000\"", "spinCount=\"10000001\"", StringComparison.Ordinal);
        var ex = Assert.Throws<OfficeLimitException>(() => PackageEncryption.Decrypt(SpecInfo(xml), TinyPackage, new OpenContext(password: "a")));
        Assert.Equal(nameof(OfficeLimits.MaxEncryptionSpinCount), ex.LimitName);
        var low = new OpenContext(new OfficeLimits { MaxEncryptionSpinCount = 1000 }, password: "a");
        Assert.Throws<OfficeLimitException>(() => PackageEncryption.Decrypt(SpecInfo(), TinyPackage, low));
        Assert.Throws<OfficeLimitException>(() => PackageEncryption.Decrypt(SpecInfo(xml.Replace("10000001", "99999999999", StringComparison.Ordinal)), TinyPackage, new OpenContext(password: "a")));
    }

    [Fact]
    public void DamagedDescriptorsAreTypedWithALocation()
    {
        byte[] info = SpecInfo();
        foreach (byte[] bad in new[]
        {
            info.AsSpan(0, 4).ToArray(),
            info.AsSpan(0, 40).ToArray(),
            SpecInfo(SpecExample.Replace("saltSize=\"16\" blockSize=\"16\" keyBits=\"128\" hashSize=\"20\" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA1\" saltValue=\"/a4i", "saltSize=\"16\" blockSize=\"16\" keyBits=\"128\" hashSize=\"20\" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA1\" saltValue=\"!!!!", StringComparison.Ordinal)),
            SpecInfo("<notEncryption/>"),
            SpecInfo("<encryption xmlns=\"http://schemas.microsoft.com/office/2006/encryption\"/>"),
        })
        {
            var ex = Assert.Throws<OfficeFormatException>(() => PackageEncryption.Describe(bad));
            Assert.Equal("encryption.malformed-header", ex.Defect.Code);
            Assert.Equal(DefectKind.Encryption, ex.Defect.Kind);
            Assert.Equal("/EncryptionInfo", ex.Location.Container);
        }

        var extensible = Assert.Throws<OfficeUnsupportedException>(() => PackageEncryption.Describe(new byte[] { 3, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.Equal("extensible (third-party) encryption", extensible.Feature);
        var version = Assert.Throws<OfficeUnsupportedException>(() => PackageEncryption.Describe(new byte[] { 9, 0, 9, 0, 0, 0, 0, 0 }));
        Assert.Equal("encryption version 9.9", version.Feature);
    }

    [Fact]
    public void ASizeThatDoesNotFitTheStreamIsTyped()
    {
        byte[] package = (byte[])TinyPackage.Clone();
        package[0] = 200;
        var ex = Assert.Throws<OfficeFormatException>(() => PackageEncryption.Decrypt(SpecInfo(), package, new OpenContext(password: "a")));
        Assert.Equal("encryption.malformed-data", ex.Defect.Code);
        Assert.Throws<OfficeFormatException>(() => PackageEncryption.Decrypt(SpecInfo(), package.AsSpan(0, 4), new OpenContext(password: "a")));
    }

    private const string SpecStandard = "0300020024000000A400000024000000000000000E660000048000008000000018000000E0BC3B07000000004D006900630072006F0073006F0066007400200045006E00680061006E006300650064002000520053004100200061006E00640020004100450053002000430072007900700074006F0067007200610070006800690063002000500072006F007600690064006500720020002800500072006F0074006F0074007900700065002900000010000000922550F6B64FFE5BD396DF5EE917DA3ABF86E18F649D17D0A541D945CEFD960C1400000012FFDC88A1BD26235932271F730B8F794E45DAB3AB0804F40BB95046D3914184";

    [Fact]
    public void TheStandardExampleInTheSpecificationOpensWithItsPassword()
    {
        // [MS-OFFCRYPTO] §3.8: a standard descriptor (AES-128, SHA-1). The specification does not give its password; it is "secret".
        byte[] info = Convert.FromHexString(SpecStandard);
        byte[] package = new byte[24];
        Assert.Empty(PackageEncryption.Decrypt(info, package, new OpenContext(password: "secret")));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, Assert.Throws<OfficeEncryptedException>(() => PackageEncryption.Decrypt(info, package, new OpenContext(password: "Secret"))).Problem);
        Assert.Equal(new PackageEncryptionInfo(PackageEncryptionScheme.Standard, 3, 2, 128, "SHA-1", 50000), PackageEncryption.Describe(info));
    }

    [Fact]
    public void ADefaultPasswordOpensAFileProtectedAgainstEditingOnly()
    {
        // [MS-OFFCRYPTO] §2.4.1: Excel protects such a workbook with a fixed password, so it opens with none; a wrong password the caller gave is refused.
        using (var source = ByteSource.FromFile(Fixture("velvet-epplus.xlsx")))
        {
            Assert.Equal((byte)'P', PackageEncryption.DecryptCompoundFile(source, new OpenContext())[0]);
        }

        using (var wrong = ByteSource.FromFile(Fixture("velvet-epplus.xlsx")))
        {
            Assert.Equal(EncryptionProblem.PasswordIncorrect, Assert.Throws<OfficeEncryptedException>(() => PackageEncryption.DecryptCompoundFile(wrong, new OpenContext(password: "not it"))).Problem);
        }

        using var other = ByteSource.FromFile(Fixture("secret-epplus.xlsx"));
        Assert.Throws<OfficeEncryptedException>(() => PackageEncryption.DecryptCompoundFile(other, new OpenContext()));
    }

    // Encrypts bytes under the standard descriptor of the specification's example (password "secret") with the key derived as [MS-OFFCRYPTO] §2.3.4.7
    // describes, written out again here so the reader's derivation is checked against a second one.
    private static byte[] Sha1(byte[] data) => OfficeHash.Compute(HashKind.Sha1, data);

    private static byte[] StandardEncrypt(byte[] plain)
    {
        byte[] info = Convert.FromHexString(SpecStandard);
        byte[] salt = info.AsSpan(12 + 164 + 4, 16).ToArray();
        byte[] h = Sha1([.. salt, .. System.Text.Encoding.Unicode.GetBytes("secret")]);
        for (uint i = 0; i < 50000; i++)
        {
            h = Sha1([.. BitConverter.GetBytes(i), .. h]);
        }

        byte[] hfinal = Sha1([.. h, 0, 0, 0, 0]);
        byte[] x1 = new byte[64];
        byte[] x2 = new byte[64];
        Array.Fill(x1, (byte)0x36);
        Array.Fill(x2, (byte)0x5C);
        for (int i = 0; i < 20; i++)
        {
            x1[i] ^= hfinal[i];
            x2[i] ^= hfinal[i];
        }

        byte[] key = Sha1(x1).AsSpan(0, 16).ToArray();
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;
        byte[] padded = new byte[(plain.Length + 15) & ~15];
        plain.CopyTo(padded, 0);
        byte[] encrypted = aes.EncryptEcb(padded, System.Security.Cryptography.PaddingMode.None);
        byte[] package = new byte[8 + encrypted.Length];
        BitConverter.TryWriteBytes(package.AsSpan(), (ulong)plain.Length);
        encrypted.CopyTo(package, 8);
        return package;
    }

    private static byte[] CompoundFile(byte[] info, byte[] package)
    {
        using CfbPackage cfb = CfbPackage.Create();
        cfb.AddPart("/EncryptionInfo", string.Empty, info);
        cfb.AddPart("/EncryptedPackage", string.Empty, package);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void StandardEncryptionMatchesAnIndependentDerivation()
    {
        byte[] plain = Enumerable.Range(0, 1000).Select(i => (byte)(i * 7)).ToArray();
        byte[] decrypted = PackageEncryption.Decrypt(Convert.FromHexString(SpecStandard), StandardEncrypt(plain), new OpenContext(password: "secret"));
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void ADecryptedPackageThatIsNoKnownFormatIsTyped()
    {
        byte[] file = CompoundFile(Convert.FromHexString(SpecStandard), StandardEncrypt("this is not a package"u8.ToArray()));
        var ex = Assert.Throws<OfficeFormatException>(() => Office.Open(ByteSource.FromMemory(file), new OpenContext(password: "secret")));
        Assert.Equal("encryption.inner-format", ex.Defect.Code);
        Assert.Equal(DefectKind.Encryption, ex.Defect.Kind);
        using CfbPackage onlyInfo = CfbPackage.Create();
        onlyInfo.AddPart("/EncryptionInfo", string.Empty, Convert.FromHexString(SpecStandard));
        using var ms = new MemoryStream();
        onlyInfo.WriteTo(ms);
        var missing = Assert.Throws<OfficeFormatException>(() => PackageEncryption.DecryptCompoundFile(ByteSource.FromMemory(ms.ToArray()), new OpenContext()));
        Assert.Equal("encryption.malformed-header", missing.Defect.Code);
    }
}
