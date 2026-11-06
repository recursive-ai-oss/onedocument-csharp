# Platforms

OneDocument targets `net10.0` and runs the same code on the desktop runtime, under NativeAOT and in browser WebAssembly. One set of
smoke checks (`smoke/shared`) is compiled into both the NativeAOT app (`smoke/aot`) and the browser app (`smoke/wasm`).

## What the smoke checks cover

25 groups: core contracts (sniffing, limits, registry, defects, packages), schema views, workbooks and recalculation, pivots, streaming,
XLSB, CSV, XLS, Word binary, 2003 XML, POCO binding, templating, Markdown, VBA read and removal, accessibility, documents, presentations,
rendering (layout, drawing, SmartArt, floats) to PDF and PNG with the bundled substitute fonts, number formats, calc, text extraction,
and a `platform` group for what differs between hosts:

- regular-expression time limits (a catastrophic pattern stops at its limit);
- culture and encodings (output never depends on the host culture; UTF-8, UTF-16 and Latin-1 only);
- parallel recalculation with the thread count forced above one, then an incremental pass;
- files through streams: save to a path, the atomic `Save()` (temp file and rename), reopen, CSV from a path;
- one password-protected file per encryption scheme (agile DOCX and XLSX, RC4 Word and Excel, XOR, RC4 CryptoAPI).

## Running the browser suite

```
build/ci.sh wasm-run
```

It needs the `wasm-tools` workload (`dotnet workload install wasm-tools`), `python3`, `curl` and `unzip`. The step:

1. publishes `smoke/wasm` with warnings as errors, so a trim or AOT warning fails it;
2. fetches the pinned Chrome for Testing build into `$ONEDOCUMENT_HOME/oracles/chromium-<version>/` (default `~/oneoffice`), checked
   against its SHA-256 (`build/wasm/fetch-chromium.sh`);
3. serves the app on 127.0.0.1 and opens it in headless Chromium, which is told to resolve nothing else, so the run uses no network
   (`build/wasm/run-browser.py`);
4. collects every console line the app prints and exits non-zero unless the app exits 0 and prints `SMOKE-RESULT ok`.

The log is `$CI_OUT/wasm-run.log`: `RUNTIME`, `CRYPTO` (the probe below), `GROUP` (one line per group with its time), `FAIL`,
`ENCRYPTED` (what each protected file did) and the final `SMOKE-RESULT`. The browser app runs on the Mono interpreter (no
ahead-of-time compilation) on one thread; the whole run takes about five seconds.

## Known browser gaps

The browser runtime has one thread (`Environment.ProcessorCount` is 1). Recalculation and layout run serially there; forcing the
parallel path still gives the same numbers. Blocking on a pool task from the main thread throws, so the library never does it on
these paths.

Cryptography in the browser runtime (probed by `PlatformSmoke.CryptoProbe`):

| Primitive | Browser |
|---|---|
| SHA-1, SHA-256, SHA-384, SHA-512 (`HashData`, `IncrementalHash`) | works |
| HMAC over SHA-1/256/384/512 (`HMACSHA*`, `IncrementalHash.CreateHMAC`) | works |
| `RandomNumberGenerator`, `CryptographicOperations.FixedTimeEquals` | works |
| MD5 (`MD5.HashData`, `IncrementalHash`, HMAC over MD5) | `CryptographicException` |
| `Aes` ECB, CBC and CFB | `PlatformNotSupportedException` |

The library turns both gaps into `OfficeUnsupportedException` that names the missing primitive. Consequences in the browser:

- reading agile or Standard encrypted OOXML (needs AES) and RC4 Word and Excel files with the MD5-derived key (needs MD5) raises
  `OfficeUnsupportedException`;
- RC4 CryptoAPI (SHA-1) and XOR obfuscation read normally, and the XLS RC4 CryptoAPI writer works (SHA-1 only);
- unencrypted files are unaffected.

This is a known limitation of 1.0: the library does not carry its own AES or MD5, and opening these encrypted files in the browser raises the typed error above. It will be revisited if users ask for it.

The browser build uses invariant globalization, as every build of the library does; nothing depends on ICU.

## Size

The published browser app (interpreter build, trimmed) is about 62 MB on disk including the precompressed copies the SDK writes next
to each file; the .NET assemblies and fonts are 35 MB uncompressed and 11.7 MiB when served Brotli-compressed. The bundled substitute
fonts are the largest part.
