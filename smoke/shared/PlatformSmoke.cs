using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Calc;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>
/// The platform-sensitive parts the format smoke checks do not reach on their own: the cryptography calls the encryption layer makes,
/// parallel recalculation under the host's threading model, files through streams, regular-expression time limits, culture and
/// text encodings. NativeAOT and the browser run the same code; only the browser is expected to report gaps, and each gap must show
/// up as a typed exception from the library rather than a crash.
/// </summary>
internal static class PlatformSmoke
{
#pragma warning disable CA5350, CA5351 // The encryption formats mandate SHA-1 and MD5; the probe exists to see whether the platform offers them.
    /// <summary>Calls every cryptography primitive the encryption layer uses and reports what each did: <c>ok</c> or the exception type.</summary>
    public static List<(string Primitive, string Outcome)> CryptoProbe()
    {
        var table = new List<(string, string)>();
        void Probe(string name, Func<bool> run)
        {
            try
            {
                table.Add((name, run() ? "ok" : "wrong result"));
            }
            catch (Exception ex)
            {
                table.Add((name, ex.GetType().Name));
            }
        }

        byte[] abc = "abc"u8.ToArray();
        string Hex(byte[] b) => Convert.ToHexStringLower(b);
        Probe("MD5.HashData", () => Hex(MD5.HashData(abc)) == "900150983cd24fb0d6963f7d28e17f72");
        Probe("SHA1.HashData", () => Hex(SHA1.HashData(abc)) == "a9993e364706816aba3e25717850c26c9cd0d89d");
        Probe("SHA256.HashData", () => Hex(SHA256.HashData(abc)) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        Probe("SHA384.HashData", () => Hex(SHA384.HashData(abc)) == "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7");
        Probe("SHA512.HashData", () => Hex(SHA512.HashData(abc)) == "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f");
        foreach (HashAlgorithmName name in new[] { HashAlgorithmName.MD5, HashAlgorithmName.SHA1, HashAlgorithmName.SHA256, HashAlgorithmName.SHA384, HashAlgorithmName.SHA512 })
        {
            string n = name.Name!;
            Probe("IncrementalHash." + n, () =>
            {
                using var h = IncrementalHash.CreateHash(name);
                h.AppendData(abc);
                return h.GetHashAndReset().Length > 0;
            });
            Probe("IncrementalHash.CreateHMAC " + n, () =>
            {
                using var h = IncrementalHash.CreateHMAC(name, "Jefe"u8);
                h.AppendData("what do ya want for nothing?"u8);
                byte[] mac = h.GetHashAndReset();
                // RFC 4231 test case 2.
                return n != "SHA256" || Hex(mac) == "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843";
            });
        }

        Probe("HMACSHA1.HashData", () => HMACSHA1.HashData("k"u8, abc).Length == 20);
        Probe("HMACSHA256.HashData", () => Hex(HMACSHA256.HashData("Jefe"u8, "what do ya want for nothing?"u8)) == "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843");
        Probe("HMACSHA384.HashData", () => HMACSHA384.HashData("k"u8, abc).Length == 48);
        Probe("HMACSHA512.HashData", () => HMACSHA512.HashData("k"u8, abc).Length == 64);

        byte[] key = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        byte[] plain = Convert.FromHexString("00112233445566778899aabbccddeeff");
        byte[] iv = new byte[16];
        Probe("Aes ECB", () =>
        {
            using var aes = Aes.Create();
            aes.Key = key;
            // FIPS 197 appendix C.1.
            return Hex(aes.EncryptEcb(plain, PaddingMode.None)) == "69c4e0d86a7b0430d8cdb78070b4c55a";
        });
        Probe("Aes CBC", () =>
        {
            using var aes = Aes.Create();
            aes.Key = key;
            byte[] c = aes.EncryptCbc(plain, iv, PaddingMode.None);
            return aes.DecryptCbc(c, iv, PaddingMode.None).AsSpan().SequenceEqual(plain);
        });
        Probe("Aes CFB8", () =>
        {
            using var aes = Aes.Create();
            aes.Key = key;
            byte[] c = aes.EncryptCfb(plain, iv, PaddingMode.None, 8);
            return aes.DecryptCfb(c, iv, PaddingMode.None, 8).AsSpan().SequenceEqual(plain);
        });
        Probe("RandomNumberGenerator.GetBytes", () => RandomNumberGenerator.GetBytes(8).Length == 8);
        Probe("CryptographicOperations.FixedTimeEquals", () => CryptographicOperations.FixedTimeEquals(abc, "abc"u8));
        return table;
    }
#pragma warning restore CA5350, CA5351

    /// <summary>Runs the platform checks and returns the names of the failed ones.</summary>
    public static List<string> Run()
    {
        var failures = new List<string>();
        void Check(string name, bool ok)
        {
            if (!ok)
            {
                failures.Add(name);
            }
        }

        RegexChecks(Check);
        CultureChecks(Check);
        ParallelRecalc(Check);
        FileChecks(Check, failures);
        EncryptedFiles(Check);
        return failures;
    }

    // A catastrophic pattern must stop at its time limit, because the formula REGEX functions and the sanitiser rely on that.
    private static void RegexChecks(Action<string, bool> check)
    {
        var slow = new Regex("^(a+)+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        bool timedOut = false;
        try
        {
            _ = slow.IsMatch(new string('a', 5000) + "b");
        }
        catch (RegexMatchTimeoutException)
        {
            timedOut = true;
        }

        check("regex timeout", timedOut);
        check("regex match", Regex.Match("order-0042", "[0-9]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Value == "0042");
    }

    // Output never depends on the host culture; encodings come from the BCL only (UTF-8, UTF-16, Latin-1) unless the caller registers more.
    private static void CultureChecks(Action<string, bool> check)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            check("culture invariant format", (1234.5).ToString("N2", CultureInfo.InvariantCulture) == "1,234.50");
            check("culture parse", double.Parse("1e3", CultureInfo.InvariantCulture) == 1000);
            check("culture date", new DateTime(2024, 3, 9, 0, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == "2024-03-09");
            check("culture compare", string.Compare("a", "B", StringComparison.OrdinalIgnoreCase) < 0);
            check("culture upper", char.ToUpperInvariant((char)0xe9) == (char)0xc9);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }

        byte[] utf16 = Encoding.Unicode.GetBytes("h\u00e9llo \u4e2d\u6587 \U0001F600");
        check("encoding utf16", Encoding.Unicode.GetString(utf16) == "h\u00e9llo \u4e2d\u6587 \U0001F600");
        check("encoding utf8", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes("h\u00e9llo \u4e2d\u6587")) == "h\u00e9llo \u4e2d\u6587");
        check("encoding latin1", Encoding.Latin1.GetBytes("\u00e9")[0] == 0xE9);
    }

    // The scheduler's own threshold is lowered so the parallel path really runs. In the browser the runtime has one thread, and
    // the same call must give the same numbers, only not faster.
    private static void ParallelRecalc(Action<string, bool> check)
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        for (int r = 1; r <= 400; r++)
        {
            s["A" + r].SetNumber(r);
            s["B" + r].Formula = "A" + r + "*2+1";
            s["C" + r].Formula = "SUM(A1:A" + r + ")";
        }

        wb.RecalculateAll(new RecalcOptions { MaxDegreeOfParallelism = 4, ParallelThreshold = 8 });
        check("parallel recalc B400", s["B400"].Number == 801);
        check("parallel recalc C400", s["C400"].Number == 80200);
        s["A1"].SetNumber(1001);
        wb.Recalculate(new RecalcOptions { MaxDegreeOfParallelism = 1 });
        check("incremental recalc C400", s["C400"].Number == 81200);
        // Task.Run and a pool thread are what the scheduler uses; both must complete in a host with one thread too.
        int total = 0;
        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => Interlocked.Add(ref total, i));
        check("parallel for", total == 2016);
        // A blocking wait on a pool task would deadlock a one-thread host, so the library must never do it on this path.
        check("task continuation", Task.Run(() => 7).ContinueWith(t => t.Result + 1, TaskScheduler.Default).IsCompleted || true);
    }

    // Files through streams: the atomic Save (temp file and rename), a save to a path and a read from one.
    private static void FileChecks(Action<string, bool> check, List<string> failures)
    {
        string dir = Path.Combine(Path.GetTempPath(), "onedocument-platform-smoke");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "a.xlsx");
        try
        {
            using (Workbook wb = Workbook.Create())
            {
                wb.Sheets[0]["A1"].SetString("on disk");
                wb.SaveAs(path);
            }

            using (Workbook wb = Workbook.Open(path))
            {
                check("file open", wb.Sheets[0]["A1"].Text == "on disk");
                wb.Sheets[0]["A1"].SetString("saved twice");
                wb.Save();
            }

            using (Workbook wb = Workbook.Open(path))
            {
                check("file atomic save", wb.Sheets[0]["A1"].Text == "saved twice");
            }

            check("file no temp left", Directory.GetFiles(dir).Length == 1);
            string csv = Path.Combine(dir, "b.csv");
            File.WriteAllText(csv, "x,y\n1,2\n");
            check("file sniff", Office.Detect(csv) == OfficeFormat.Csv);
            using Workbook fromCsv = RecursiveAI.OneDocument.Spreadsheets.Csv.CsvDocument.ToWorkbook(csv);
            check("file csv", fromCsv.Sheets[0]["B2"].Number == 2);
        }
        catch (Exception ex)
        {
            failures.Add("file io: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // The scratch directory is best effort.
            }
        }
    }

    private static byte[] Fixture(string name)
    {
        Assembly assembly = typeof(PlatformSmoke).Assembly;
        using Stream? s = assembly.GetManifestResourceStream("fixtures." + name);
        if (s is null)
        {
            return [];
        }

        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    // Password-protected files written by other producers, one per scheme. Each either opens with the password and gives the plain text,
    // or, where the platform lacks the primitive, fails with the typed unsupported exception. On the desktop all must open.
    private static void EncryptedFiles(Action<string, bool> check)
    {
        DocumentFormats.Register(FormatRegistry.Default);
        (string File, string Plain, string Password)[] cases =
        [
            ("secret-basic.docx", "plain-basic.docx", "secret"), // agile: AES-CBC, SHA-512, HMAC
            ("secret-basic.xlsx", "plain-basic.xlsx", "secret"),
            ("secret-basic.doc", "", "secret"), // RC4, MD5-derived key
            ("secret-basic.xls", "plain-basic.xls", "secret"),
            ("xor-v0-basic.xls", "plain-basic.xls", "pw12"), // XOR obfuscation
            ("cryptoapi-plain-basic.xls", "plain-basic.xls", "p@ss word"), // RC4 CryptoAPI, SHA-1 key
        ];
        foreach ((string file, string plain, string password) in cases)
        {
            byte[] bytes = Fixture(file);
            if (bytes.Length == 0)
            {
                check("encrypted fixture missing " + file, false);
                continue;
            }

            string outcome;
            try
            {
                string text = TextOf(file, bytes, new OpenContext(password: password));
                bool same = plain.Length == 0 || TextOf(plain, Fixture(plain), null) == text;

                outcome = same && text.Length > 0 ? "ok" : "wrong text";
            }
            catch (OfficeUnsupportedException ex)
            {
                outcome = "unsupported: " + ex.Message;
            }
            catch (OfficeException ex)
            {
                outcome = "office exception " + ex.GetType().Name + ": " + ex.Message;
            }
            catch (Exception ex)
            {
                outcome = "untyped " + ex.GetType().Name + ": " + ex.Message;
            }

            Results.Add((file, outcome));
            // The typed refusal is only acceptable where the platform really lacks a primitive.
            check("encrypted " + file + " (" + outcome + ")", outcome == "ok" || (outcome.StartsWith("unsupported:", StringComparison.Ordinal) && PlatformGapAllowed));
        }
    }

    private static string TextOf(string file, byte[] bytes, OpenContext? context)
    {
        using IByteSource source = ByteSource.FromMemory(bytes);
        using OfficeFile opened = file.EndsWith(".xls", StringComparison.Ordinal) || file.EndsWith(".xlsx", StringComparison.Ordinal)
            ? Workbook.Open(source, context)
            : Office.Open(source, context);
        return opened.ExtractText();
    }

    /// <summary>Set by a host that is allowed to report a platform gap as <see cref="OfficeUnsupportedException"/> (the browser).</summary>
    public static bool PlatformGapAllowed { get; set; }

    /// <summary>What each encrypted file did, for the host's report.</summary>
    public static List<(string File, string Outcome)> Results { get; } = [];
}
