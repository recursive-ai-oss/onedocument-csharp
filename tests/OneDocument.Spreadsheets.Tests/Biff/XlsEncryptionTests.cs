using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Encryption;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests.Biff;

public class XlsEncryptionTests
{
    private static string Dir([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "testdata");

    private static string Dump(Workbook w)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Worksheet sh in w.Sheets)
        {
            sb.Append(sh.Name).Append(':');
            foreach (CellRowView r in sh.Store.EnumerateRows())
            {
                for (int i = 0; i < r.Count; i++)
                {
                    sb.Append(r.Row).Append('/').Append(r.ColumnAt(i)).Append('=').Append(sh.GetText(r.ValueAt(i))).Append(',');
                }
            }

            sb.Append('|');
        }

        return sb.ToString();
    }

    // The names of the parts that differ between two zips; empty when every part is byte-identical.
    private static string DifferingParts(byte[] a, byte[] b)
    {
        using var za = new System.IO.Compression.ZipArchive(new MemoryStream(a));
        using var zb = new System.IO.Compression.ZipArchive(new MemoryStream(b));
        var diff = new List<string>();
        foreach (var ea in za.Entries)
        {
            var eb = zb.GetEntry(ea.FullName);
            if (eb is null || Read(ea) != Read(eb))
            {
                diff.Add(ea.FullName);
            }
        }

        return string.Join(",", diff);
    }

    private static string Read(System.IO.Compression.ZipArchiveEntry e)
    {
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    private static byte[] Saved(Workbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        return ms.ToArray();
    }

    [Fact]
    public void LibreOfficeEncryptedXlsOpensWithThePasswordAndMatchesThePlainConversion()
    {
        string path = Path.Combine(Dir(), "encryption", "secret-basic.xls");
        using Workbook plain = XlsConverter.ToWorkbook(ByteSource.FromFile(Path.Combine(Dir(), "encryption", "plain-basic.xls")), new OpenContext());
        var ctx = new OpenContext(password: "secret");
        using Workbook opened = XlsConverter.ToWorkbook(ByteSource.FromFile(path), ctx);
        Assert.Equal(Dump(plain), Dump(opened));
        Assert.Equal(string.Empty, DifferingParts(Saved(plain), Saved(opened)));
        
    }

    private static byte[] PlainWorkbookStream(string name) =>
        RecursiveAI.OneDocument.Cfb.CfbPackage.Open(ByteSource.FromFile(Path.Combine(Dir(), "encryption", name))).FindPart("/Workbook")!.GetBytes().ToArray();

    private static byte[] WithWorkbookStream(string sourceName, byte[] stream)
    {
        using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(ByteSource.FromFile(Path.Combine(Dir(), "encryption", sourceName)));
        cfb.FindPart("/Workbook")!.SetBytes(stream);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    // Files this test builds can be written out for other programs to read (tools/Encryption/README.md): set ONEDOCUMENT_ENCRYPTION_OUT to a directory.
    private static void Keep(string name, byte[] bytes)
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_ENCRYPTION_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
        }
    }

    [Theory]
    [InlineData("plain-basic.xls")]
    [InlineData("features.xls")]
    public void OurEncryptedWorkbookOpensWithOurReader(string fixture)
    {
        string source = fixture == "features.xls" ? Path.Combine("..", "xls", fixture) : fixture;
        byte[] stream = PlainWorkbookStream(source);
        byte[] encrypted = XlsEncryption.Encrypt(stream, "p@ss word", 7);
        Assert.Equal(encrypted, XlsEncryption.Encrypt(stream, "p@ss word", 7));
        byte[] file = WithWorkbookStream(source, encrypted);
        Keep("cryptoapi-" + Path.GetFileName(fixture), file);

        using Workbook plain = XlsConverter.ToWorkbook(ByteSource.FromFile(Path.Combine(Dir(), "encryption", source)), new OpenContext());
        using Workbook opened = XlsConverter.ToWorkbook(ByteSource.FromMemory(file), new OpenContext(password: "p@ss word"));
        Assert.Equal(Dump(plain), Dump(opened));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, Assert.Throws<OfficeEncryptedException>(() => XlsConverter.ToWorkbook(ByteSource.FromMemory(file), new OpenContext(password: "x"))).Problem);
    }

    [Fact]
    public void PropertiesOfAPasswordSavedWorkbookAreReadAfterDecryption()
    {
        using Workbook wb = Workbook.Create();
        var props = new RecursiveAI.OneDocument.Properties.DocumentProperties(wb.Package);
        props.Core.Title = "Quarterly";
        props.Extended.Company = "Analytical Engines";
        props.Commit();
        using var secret = new MemoryStream();
        wb.SaveAs(secret, OfficeFormat.Xls, "p@ss word");

        // The property streams stay in the clear next to the encrypted workbook stream.
        using RecursiveAI.OneDocument.Cfb.CfbPackage cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(ByteSource.FromMemory(secret.ToArray()));
        Assert.NotNull(cfb.FindPart("/" + RecursiveAI.OneDocument.Ole.OlePropertySets.SummaryInformationStreamName));

        using Workbook back = XlsConverter.ToWorkbook(ByteSource.FromMemory(secret.ToArray()), new OpenContext(password: "p@ss word"));
        var read = new RecursiveAI.OneDocument.Properties.DocumentProperties(back.Package);
        Assert.Equal("Quarterly", read.Core.Title);
        Assert.Equal("Analytical Engines", read.Extended.Company);
    }

    /// <summary>Builds an encrypted summary stream the way [MS-OFFCRYPTO] §2.3.5.4 writes it: data first, then the descriptor array, then the two offsets.</summary>
    private static byte[] BuildEncryptedSummary(RecursiveAI.OneDocument.Encryption.BinaryRc4Encryption enc, params (string Name, byte[] Data)[] streams)
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
    public void PropertiesInAnEncryptedSummaryStreamAreReadWhenTheHeaderClearsFDocProps()
    {
        using Workbook wb = Workbook.Create();
        var props = new RecursiveAI.OneDocument.Properties.DocumentProperties(wb.Package);
        props.Core.Title = "Hidden title";
        props.Extended.Company = "Hidden company";
        props.Commit();
        using var secret = new MemoryStream();
        wb.SaveAs(secret, OfficeFormat.Xls, "pw");

        // [MS-OFFCRYPTO] §2.3.5.4: move the property streams into an encrypted summary stream and clear fDocProps in the FilePass header.
        using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(ByteSource.FromMemory(secret.ToArray()));
        string summaryName = RecursiveAI.OneDocument.Ole.OlePropertySets.SummaryInformationStreamName;
        string documentSummaryName = RecursiveAI.OneDocument.Ole.OlePropertySets.DocumentSummaryInformationStreamName;
        byte[] summary = cfb.FindPart("/" + summaryName)!.GetBytes().ToArray();
        byte[] documentSummary = cfb.FindPart("/" + documentSummaryName)!.GetBytes().ToArray();
        byte[] stream = cfb.FindPart("/Workbook")!.GetBytes().ToArray();
        Assert.True(XlsEncryption.TryFindFilePass(stream, out int fp));
        int flags = fp + 4 + 2 + 4;
        Assert.NotEqual(0, stream[flags] & 0x08);
        stream[flags] &= unchecked((byte)~0x08);
        stream[flags + 8] &= unchecked((byte)~0x08); // the copy in the EncryptionHeader itself, which the reader goes by
        var header = RecursiveAI.OneDocument.Encryption.BinaryRc4Encryption.Parse(stream.AsSpan(fp + 6, BitConverter.ToUInt16(stream, fp + 2) - 2), default);
        Assert.True(header.TryUnlock("pw"));
        Assert.False(header.DocumentPropertiesClear);
        cfb.FindPart("/Workbook")!.SetBytes(stream);
        cfb.RemovePart("/" + summaryName);
        cfb.RemovePart("/" + documentSummaryName);
        cfb.AddPart("/EncryptedSummary", string.Empty, BuildEncryptedSummary(header, (summaryName, summary), (documentSummaryName, documentSummary)));
        using var file = new MemoryStream();
        cfb.WriteTo(file);

        using Workbook back = XlsConverter.ToWorkbook(ByteSource.FromMemory(file.ToArray()), new OpenContext(password: "pw"));
        var read = new RecursiveAI.OneDocument.Properties.DocumentProperties(back.Package);
        Assert.Equal("Hidden title", read.Core.Title);
        Assert.Equal("Hidden company", read.Extended.Company);
    }

    [Fact]
    public void ADefaultPasswordIsNotTriedAfterAWrongOne()
    {
        byte[] file = WithWorkbookStream("plain-basic.xls", XlsEncryption.Encrypt(PlainWorkbookStream("plain-basic.xls"), PackageEncryption.DefaultPassword, 3));
        var wrong = Assert.Throws<OfficeEncryptedException>(() => XlsConverter.ToWorkbook(ByteSource.FromMemory(file), new OpenContext(password: "not it")));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, wrong.Problem);
    }

    [Fact]
    public void EncryptingKeepsSheetPositionsPointingAtTheirBof()
    {
        byte[] encrypted = XlsEncryption.Encrypt(PlainWorkbookStream("plain-basic.xls"), "pw", 1);
        Assert.True(XlsEncryption.TryFindFilePass(encrypted, out int fp));
        byte[] plainAgain = XlsEncryption.Decrypt(encrypted, fp, new OpenContext(password: "pw"), "/Workbook");
        int pos = 0;
        int checkedSheets = 0;
        while (pos + 4 <= plainAgain.Length)
        {
            int type = plainAgain[pos] | (plainAgain[pos + 1] << 8);
            int len = plainAgain[pos + 2] | (plainAgain[pos + 3] << 8);
            if (type == 0x0085)
            {
                int target = BitConverter.ToInt32(plainAgain, pos + 4);
                Assert.Equal(0x0809, plainAgain[target] | (plainAgain[target + 1] << 8));
                checkedSheets++;
            }

            pos += 4 + len;
        }

        Assert.True(checkedSheets > 0);
    }

    // [MS-OFFCRYPTO] does not say which array index a record's first byte uses. The index of the first data byte plus the record length is what
    // LibreOffice reads (tools/Encryption/README.md records the other candidates that it rejected), and what XlsEncryption uses.
    [Fact]
    public void XorObfuscatedWorkbookRoundTripsAndIsKeptForOtherReaders()
    {
        byte[] stream = PlainWorkbookStream("plain-basic.xls");
        XorObfuscation xor = XorObfuscation.CreateMethod1("pw12", out ushort key, out ushort verifier);
        byte[] payload = new byte[6];
        BitConverter.TryWriteBytes(payload.AsSpan(2), key);
        BitConverter.TryWriteBytes(payload.AsSpan(4), verifier);
        byte[] output = XlsEncryption.InsertFilePass(stream, payload, out int insertAt);
        int pos = 0;
        while (pos + 4 <= output.Length)
        {
            int type = output[pos] | (output[pos + 1] << 8);
            int len = output[pos + 2] | (output[pos + 3] << 8);
            bool clear = pos <= insertAt || type is 0x0809 or 0x0409 or 0x0209 or 0x0009 or 0x002F or 0x0194 or 0x0195 or 0x00E1 or 0x0196 or 0x0138;
            if (!clear)
            {
                int skip = type == 0x0085 ? 4 : 0;
                int data = pos + 4;
                xor.EncryptMethod1(output.AsSpan(data + skip, len - skip), data + len + skip);
            }

            pos += 4 + len;
        }

        byte[] file = WithWorkbookStream("plain-basic.xls", output);
        Keep("xor-v0-basic.xls", file);
        using Workbook plain = XlsConverter.ToWorkbook(ByteSource.FromFile(Path.Combine(Dir(), "encryption", "plain-basic.xls")), new OpenContext());
        using Workbook opened = XlsConverter.ToWorkbook(ByteSource.FromMemory(file), new OpenContext(password: "pw12"));
        Assert.Equal(Dump(plain), Dump(opened));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, Assert.Throws<OfficeEncryptedException>(() => XlsConverter.ToWorkbook(ByteSource.FromMemory(file), new OpenContext(password: "pw13"))).Problem);
    }

    [Fact]
    public void WorkbookProtectedAgainstEditingOnlyOpensWithoutAPassword()
    {
        // [MS-OFFCRYPTO] §2.4.2.2: Excel encrypts such a workbook with a fixed password.
        byte[] file = WithWorkbookStream("plain-basic.xls", XlsEncryption.Encrypt(PlainWorkbookStream("plain-basic.xls"), PackageEncryption.DefaultPassword, 3));
        using Workbook plain = XlsConverter.ToWorkbook(ByteSource.FromFile(Path.Combine(Dir(), "encryption", "plain-basic.xls")), new OpenContext());
        using Workbook opened = XlsConverter.ToWorkbook(ByteSource.FromMemory(file), new OpenContext());
        Assert.Equal(Dump(plain), Dump(opened));
    }

    [Fact]
    public void DamagedFilePassRecordsAreTypedWithTheirLocation()
    {
        byte[] encrypted = XlsEncryption.Encrypt(PlainWorkbookStream("plain-basic.xls"), "pw", 1);
        Assert.True(XlsEncryption.TryFindFilePass(encrypted, out int fp));

        byte[] unknownType = (byte[])encrypted.Clone();
        unknownType[fp + 4] = 7;
        var unsupported = Assert.Throws<OfficeUnsupportedException>(() => XlsConverter.ToWorkbook(ByteSource.FromMemory(Wrap(unknownType)), new OpenContext(password: "pw")));
        Assert.Contains("FilePass encryption type 7", unsupported.Feature, StringComparison.Ordinal);
        Assert.Equal(fp, unsupported.Location.Offset);

        byte[] shortRecord = (byte[])encrypted.Clone();
        shortRecord[fp + 2] = 1; // the record claims one byte: no type field
        shortRecord[fp + 3] = 0;
        var tiny = Assert.Throws<OfficeFormatException>(() => XlsConverter.ToWorkbook(ByteSource.FromMemory(Wrap(shortRecord)), new OpenContext(password: "pw")));
        Assert.Equal("encryption.malformed-header", tiny.Defect.Code);
        Assert.Equal(fp, tiny.Location.Offset);

        byte[] shortHeader = (byte[])encrypted.Clone();
        shortHeader[fp + 2] = 20;
        var cut = Assert.Throws<OfficeFormatException>(() => XlsConverter.ToWorkbook(ByteSource.FromMemory(Wrap(shortHeader)), new OpenContext(password: "pw")));
        Assert.Equal("encryption.malformed-header", cut.Defect.Code);

        byte[] xorShort = (byte[])encrypted.Clone();
        xorShort[fp + 4] = 0;
        xorShort[fp + 2] = 4;
        Assert.Equal("encryption.malformed-header", Assert.Throws<OfficeFormatException>(() => XlsConverter.ToWorkbook(ByteSource.FromMemory(Wrap(xorShort)), new OpenContext(password: "pw"))).Defect.Code);
    }

    private static byte[] Wrap(byte[] stream) => WithWorkbookStream("plain-basic.xls", stream);

    [Fact]
    public void BareBiffStreamsAreDecryptedToo()
    {
        byte[] encrypted = XlsEncryption.Encrypt(PlainWorkbookStream("plain-basic.xls"), "pw", 1);
        using Workbook plain = XlsConverter.ToWorkbook(ByteSource.FromMemory(PlainWorkbookStream("plain-basic.xls")), new OpenContext());
        using Workbook opened = XlsConverter.ToWorkbook(ByteSource.FromMemory(encrypted), new OpenContext(password: "pw"));
        Assert.Equal(Dump(plain), Dump(opened));
        Assert.Throws<ArgumentException>(() => XlsEncryption.Encrypt(encrypted, "pw", 1));
        Assert.Throws<ArgumentException>(() => XlsEncryption.Encrypt(new byte[] { 1, 2, 3, 4 }, "pw", 1));
    }

    [Fact]
    public void WorkbookSavedWithAPasswordReopensWithItAndNotWithout()
    {
        using Workbook wb = Workbook.Open(Path.Combine(Dir(), "encryption", "plain-basic.xlsx"));
        using var plainStream = new MemoryStream();
        wb.SaveAs(plainStream, OfficeFormat.Xls);
        using var secret = new MemoryStream();
        wb.SaveAs(secret, OfficeFormat.Xls, "p@ss word");
        using var again = new MemoryStream();
        wb.SaveAs(again, OfficeFormat.Xls, "p@ss word");
        Assert.Equal(secret.ToArray(), again.ToArray());
        Assert.NotEqual(plainStream.ToArray(), secret.ToArray());
        Keep("written-secret.xls", secret.ToArray());

        using Workbook expected = XlsConverter.ToWorkbook(ByteSource.FromMemory(plainStream.ToArray()), new OpenContext());
        using Workbook opened = XlsConverter.ToWorkbook(ByteSource.FromMemory(secret.ToArray()), new OpenContext(password: "p@ss word"));
        Assert.Equal(Dump(expected), Dump(opened));
        Assert.Equal(EncryptionProblem.PasswordRequired, Assert.Throws<OfficeEncryptedException>(() => XlsConverter.ToWorkbook(ByteSource.FromMemory(secret.ToArray()), new OpenContext())).Problem);
        Assert.Equal(EncryptionProblem.PasswordIncorrect, Assert.Throws<OfficeEncryptedException>(() => XlsConverter.ToWorkbook(ByteSource.FromMemory(secret.ToArray()), new OpenContext(password: "nope"))).Problem);

        // The ordinary entry points open it too, and a file path save works.
        string path = Path.Combine(Path.GetTempPath(), "xls-encryption-" + Guid.NewGuid().ToString("N") + ".xls");
        try
        {
            wb.SaveAs(path, OfficeFormat.Xls, "p@ss word");
            using Workbook viaOpen = Workbook.Open(path, new OpenContext(password: "p@ss word"));
            Assert.Equal(Dump(expected), Dump(viaOpen));
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Throws<OfficeUnsupportedException>(() => wb.SaveAs(new MemoryStream(), OfficeFormat.Xlsx, "pw"));
    }

    [Fact]
    public void NoPasswordAndWrongPasswordAreTyped()
    {
        string path = Path.Combine(Dir(), "encryption", "secret-basic.xls");
        var none = Assert.Throws<OfficeEncryptedException>(() => XlsConverter.ToWorkbook(ByteSource.FromFile(path), new OpenContext()));
        Assert.Equal(EncryptionProblem.PasswordRequired, none.Problem);
        var wrong = Assert.Throws<OfficeEncryptedException>(() => XlsConverter.ToWorkbook(ByteSource.FromFile(path), new OpenContext(password: "nope")));
        Assert.Equal(EncryptionProblem.PasswordIncorrect, wrong.Problem);
        Assert.Equal(DefectKind.Encryption, wrong.Defect.Kind);
    }
}

public class EncryptedWorkbookPackageTests
{
    private static string Dir([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "testdata", "encryption");

    [Fact]
    public void AgileEncryptedWorkbookOpensWithThePassword()
    {
        using Workbook plain = Workbook.Open(Path.Combine(Dir(), "plain-basic.xlsx"));
        var defects = new DefectCollection();
        var ctx = new OpenContext(defects: defects, password: "secret");
        using Workbook opened = Workbook.Open(Path.Combine(Dir(), "secret-basic.xlsx"), ctx);
        Assert.Null(opened.FilePath);
        Assert.Equal(plain.Sheets.Count, opened.Sheets.Count);
        Assert.Equal(plain.Sheets[0].UsedRange(), opened.Sheets[0].UsedRange());
        Assert.DoesNotContain(defects, d => d.Code == "encryption.integrity-mismatch");
    }

    [Fact]
    public void WorkbookWithoutOrWithWrongPassword()
    {
        string path = Path.Combine(Dir(), "secret-basic.xlsx");
        Assert.Equal(EncryptionProblem.PasswordRequired, Assert.Throws<OfficeEncryptedException>(() => Workbook.Open(path)).Problem);
        Assert.Equal(EncryptionProblem.PasswordIncorrect, Assert.Throws<OfficeEncryptedException>(() => Workbook.Open(path, new OpenContext(password: "x"))).Problem);
    }
}
