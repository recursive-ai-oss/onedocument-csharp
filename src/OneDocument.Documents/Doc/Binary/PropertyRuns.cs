using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Doc.Binary;

/// <summary>
/// The character or paragraph property runs of a Word binary file: every entry of every formatted disk page (FKP) the bin table
/// (<c>PlcBteChpx</c> or <c>PlcBtePapx</c>, [MS-DOC] 2.8.5 and 2.8.6) points at, flattened into one sorted list of byte ranges of
/// the <c>WordDocument</c> stream and the property list (<c>grpprl</c>) that applies to each.
/// </summary>
public sealed class PropertyRuns
{
    private readonly int[] _start;
    private readonly int[] _end;
    private readonly ReadOnlyMemory<byte>[] _grpprl;
    private readonly int[] _istd;

    private PropertyRuns(int[] start, int[] end, ReadOnlyMemory<byte>[] grpprl, int[] istd)
    {
        _start = start;
        _end = end;
        _grpprl = grpprl;
        _istd = istd;
    }

    /// <summary>Gets an index with no runs.</summary>
    public static PropertyRuns Empty { get; } = new([], [], [], []);

    /// <summary>Gets the number of runs.</summary>
    public int Count => _start.Length;

    /// <summary>Finds the run that holds a byte offset of the <c>WordDocument</c> stream.</summary>
    /// <param name="fc">The offset.</param>
    /// <param name="end">The offset just past the run, or the start of the next run when none holds <paramref name="fc"/>.</param>
    /// <param name="grpprl">The run's property list; empty if the run has none or none holds <paramref name="fc"/>.</param>
    /// <param name="istd">For paragraph runs, the style index stored with the properties; 0 otherwise.</param>
    /// <returns>True if a run holds <paramref name="fc"/>.</returns>
    public bool Find(long fc, out long end, out ReadOnlyMemory<byte> grpprl, out int istd)
    {
        int lo = 0;
        int hi = _start.Length - 1;
        int found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_start[mid] <= fc)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (found >= 0 && fc < _end[found])
        {
            end = _end[found];
            grpprl = _grpprl[found];
            istd = _istd[found];
            return true;
        }

        end = found + 1 < _start.Length ? _start[found + 1] : long.MaxValue;
        grpprl = default;
        istd = 0;
        return false;
    }

    /// <summary>Reads a bin table and the formatted disk pages it lists.</summary>
    /// <param name="file">The file.</param>
    /// <param name="pairIndex">Which bin table: <c>DocFib.PlcfBteChpx</c> or <c>DocFib.PlcfBtePapx</c>.</param>
    /// <param name="paragraph">True for paragraph pages (13-byte entries with an <c>istd</c>), false for character pages.</param>
    /// <returns>The runs.</returns>
    internal static PropertyRuns Read(DocFile file, int pairIndex, bool paragraph)
    {
        (uint fc, uint lcb) = file.Fib.Pair(pairIndex);
        IDefectSink sink = file.Context.Defects;
        byte[] table = file.Table;
        if (lcb < 12 || fc >= table.Length)
        {
            return Empty;
        }

        long end = Math.Min((long)fc + lcb, table.Length);
        int len = (int)(end - fc);
        // [MS-DOC] 2.8.5: n+1 file offsets (4 bytes) then n page numbers (4 bytes; 22 bits used).
        const int PnSize = 4;
        int n = (len - 4) / (4 + PnSize);
        var starts = new List<int>();
        var ends = new List<int>();
        var grps = new List<ReadOnlyMemory<byte>>();
        var istds = new List<int>();
        string path = file.TablePath;
        for (int i = 0; i < n; i++)
        {
            int pnAt = (int)fc + ((n + 1) * 4) + (i * PnSize);
            long pn = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(pnAt)) & 0x3FFFFF;
            long page = pn * 512;
            if (page + 512 > file.Word.Length)
            {
                sink.Report(new Defect(DefectKind.Record, "doc.fkp-out-of-range", DefectSeverity.Dropped, $"The formatted disk page {pn} lies outside the WordDocument stream.", DefectLocation.InRecord(path, pnAt)));
                continue;
            }

            ReadPage(file.Word.AsMemory((int)page, 512), paragraph, (int)page, starts, ends, grps, istds, sink);
        }

        // The pages are in file-offset order for a well-formed file; sort anyway so lookups never depend on it.
        int[] idx = new int[starts.Count];
        for (int i = 0; i < idx.Length; i++)
        {
            idx[i] = i;
        }

        int[] sa = [.. starts];
        Array.Sort(idx, (a, b) => sa[a].CompareTo(sa[b]));
        var s = new int[idx.Length];
        var e = new int[idx.Length];
        var g = new ReadOnlyMemory<byte>[idx.Length];
        var t = new int[idx.Length];
        for (int i = 0; i < idx.Length; i++)
        {
            s[i] = starts[idx[i]];
            e[i] = ends[idx[i]];
            g[i] = grps[idx[i]];
            t[i] = istds[idx[i]];
        }

        return new PropertyRuns(s, e, g, t);
    }

    private static void ReadPage(
        ReadOnlyMemory<byte> page, bool paragraph, int pageOffset,
        List<int> starts, List<int> ends, List<ReadOnlyMemory<byte>> grps, List<int> istds, IDefectSink sink)
    {
        ReadOnlySpan<byte> p = page.Span;
        int crun = p[511];
        int entry = paragraph ? 13 : 1;
        // [MS-DOC] 2.9.33 ChpxFkp and 2.9.171 PapxFkp: crun+1 offsets, then crun entries, then the property data, crun last.
        if (crun == 0 || ((crun + 1) * 4) + (crun * entry) > 511)
        {
            sink.Report(new Defect(DefectKind.Record, "doc.fkp-bad", DefectSeverity.Dropped, $"A formatted disk page holds {crun} runs, which cannot fit in 512 bytes.", DefectLocation.InRecord(DocFile.WordPath, pageOffset + 511)));
            return;
        }

        for (int i = 0; i < crun; i++)
        {
            int f0 = BinaryPrimitives.ReadInt32LittleEndian(p[(i * 4)..]);
            int f1 = BinaryPrimitives.ReadInt32LittleEndian(p[((i + 1) * 4)..]);
            if (f1 <= f0)
            {
                continue;
            }

            int w = p[((crun + 1) * 4) + (i * entry)] * 2;
            ReadOnlyMemory<byte> grpprl = default;
            int istd = 0;
            if (w != 0 && w < 511)
            {
                if (!paragraph)
                {
                    int cb = p[w];
                    if (w + 1 + cb <= 511)
                    {
                        grpprl = page.Slice(w + 1, cb);
                    }
                }
                else
                {
                    // [MS-DOC] 2.9.172 PapxInFkp: a size byte (in words, less one byte), or zero and a second size byte.
                    int cb = p[w];
                    int start;
                    int size;
                    if (cb == 0)
                    {
                        cb = w + 1 < 512 ? p[w + 1] : 0;
                        start = w + 2;
                        size = cb * 2;
                    }
                    else
                    {
                        start = w + 1;
                        size = (cb * 2) - 1;
                    }

                    if (size >= 2 && start + size <= 512)
                    {
                        istd = BinaryPrimitives.ReadUInt16LittleEndian(p[start..]);
                        grpprl = page.Slice(start + 2, size - 2);
                    }
                    else
                    {
                        sink.Report(new Defect(DefectKind.Record, "doc.fkp-bad", DefectSeverity.Dropped, "A paragraph property record runs past its formatted disk page.", DefectLocation.InRecord(DocFile.WordPath, pageOffset + w)));
                    }
                }
            }

            starts.Add(f0);
            ends.Add(f1);
            grps.Add(grpprl);
            istds.Add(istd);
        }

    }
}
