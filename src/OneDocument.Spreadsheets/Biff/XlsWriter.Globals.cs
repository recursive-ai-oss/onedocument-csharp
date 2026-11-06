namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsWriter
{
    private sealed class SheetOut
    {
        public int BoundSheetPatchAt;
        public int StreamPosition;
    }

    private void Bof(int type)
    {
        var b = new BiffBuffer();
        b.U16(0x0600);
        b.U16(type);
        b.U16(0x0DBB);
        b.U16(0x07CC);
        b.U32(0x00000009); // fWin, fWinAny ([MS-XLS] §2.4.21)
        b.U32(0x00000006); // verLowestBiff
        _s.Record(0x0809, b);
    }

    private void U16Record(int type, int v)
    {
        var b = new BiffBuffer();
        b.U16(v);
        _s.Record(type, b);
    }

    private int ActiveSheet()
    {
        for (int i = 0; i < _sheets.Count; i++)
        {
            if (_sheets[i].State == SheetState.Visible)
            {
                return i;
            }
        }

        return 0;
    }

    // [MS-XLS] §2.1.7.20.3 Globals Substream.
    private void WriteGlobals()
    {
        Bof(0x0005);
        U16Record(0x00E1, 0x04B0); // InterfaceHdr
        U16Record(0x00C1, 0); // Mms
        _s.Record(0x00E2); // InterfaceEnd
        var wa = new BiffBuffer();
        wa.String16("OneDocument");
        wa.Zeros(112 - wa.Length);
        _s.Record(0x005C, wa); // WriteAccess: 112 bytes, space padded in Excel but ignored
        U16Record(0x0042, 0x04B0); // CodePage: UTF-16
        U16Record(0x0161, 0); // DSF
        var tab = new BiffBuffer();
        for (int i = 0; i < _sheets.Count; i++)
        {
            tab.U16(i + 1);
        }

        _s.Record(0x013D, tab); // RRTabId
        U16Record(0x0019, 0); // WinProtect
        U16Record(0x0012, 0); // Protect
        U16Record(0x0013, 0); // Password
        U16Record(0x01AF, 0); // Prot4Rev
        U16Record(0x01BC, 0); // Prot4RevPass
        var w1 = new BiffBuffer();
        w1.U16(0x0078);
        w1.U16(0x0078);
        w1.U16(0x4000);
        w1.U16(0x2400);
        w1.U16(0x0038);
        w1.U16(ActiveSheet());
        w1.U16(0);
        w1.U16(1);
        w1.U16(0x0258);
        _s.Record(0x003D, w1);
        U16Record(0x0040, 0); // Backup
        U16Record(0x008D, 0); // HideObj
        U16Record(0x0022, _wb.DateSystem == DateSystem.Date1904 ? 1 : 0);
        U16Record(0x000E, 1); // CalcPrecision
        U16Record(0x01B7, 0); // RefreshAll
        U16Record(0x00DA, 0); // BookBool
        _styles.Emit(_s);
        U16Record(0x0160, 0); // UsesELFs
        _sheetOut.Clear();
        for (int i = 0; i < _sheets.Count; i++)
        {
            var o = new SheetOut();
            _sheetOut.Add(o);
            var b = new BiffBuffer();
            o.BoundSheetPatchAt = _s.Position + 4;
            b.U32(0);
            b.U8(_sheets[i].State switch { SheetState.Hidden => 1, SheetState.VeryHidden => 2, _ => 0 });
            b.U8(0);
            b.String8(BiffSheetName(_sheets[i].Name));
            _s.Record(0x0085, b);
        }

        var country = new BiffBuffer();
        country.U16(1);
        country.U16(1);
        _s.Record(0x008C, country);
        WriteSupBooksAndNames();
        if (TotalNotes() > 0)
        {
            WriteDrawingGroup();
        }

        WriteSst();
        Eof();
    }

    private void Eof() => _s.Record(0x000A);

    private void WriteSupBooksAndNames()
    {
        foreach (SupBook sb in _fctx.Books)
        {
            if (!sb.IsInternal)
            {
                continue;
            }

            var b = new BiffBuffer();
            b.U16(_sheets.Count);
            b.U16(0x0401);
            _s.Record(0x01AE, b);
        }

        if (_fctx.Xti.Count > 0)
        {
            var b = new BiffBuffer();
            b.U16(_fctx.Xti.Count);
            foreach ((int book, int first, int last) in _fctx.Xti)
            {
                b.U16(book);
                b.U16(first);
                b.U16(last);
            }

            _s.Record(0x0017, b);
        }

        foreach (DefinedNameInfo n in _fctx.Names)
        {
            if (!n.IsBuiltIn && n.Formula is null && !n.IsFunction)
            {
                continue;
            }

            var b = new BiffBuffer();
            int grbit = (n.Hidden ? 1 : 0) | (n.IsBuiltIn ? 0x20 : 0);
            b.U16(grbit);
            b.U8(0);
            if (n.IsBuiltIn)
            {
                b.U8(1);
            }
            else
            {
                b.U8(n.Name.Length);
            }

            b.U16(n.Rgce.Length);
            b.U16(0);
            b.U16(n.SheetScope + 1);
            b.U8(0);
            b.U8(n.Comment.Length > 255 ? 255 : n.Comment.Length);
            b.U8(0);
            b.U8(0);
            if (n.IsBuiltIn)
            {
                b.U8(0);
                b.U8(n.BuiltIn);
            }
            else if (n.Name.Length > 0)
            {
                b.StringNoCount(n.Name);
            }

            b.Bytes(n.Rgce);
            if (n.Comment.Length > 0)
            {
                string c = n.Comment.Length > 255 ? n.Comment[..255] : n.Comment;
                b.StringNoCount(c);
            }

            _s.Record(0x0018, b);
        }
    }

    // [MS-XLS] §2.4.265 SST: a string is never cut inside its header; when its characters run past the end of a record the rest
    // continues in a CONTINUE record that restarts with a new flag byte.
    private void WriteSst()
    {
        var bucketPositions = new List<(int Position, int Offset)>();
        int perBucket = Math.Max(_sstList.Count / 128 + 1, 8);
        var cur = new BiffBuffer();
        cur.U32((uint)_sstReferences);
        cur.U32((uint)_sstList.Count);
        int recordStart = _s.Position;
        int type = 0x00FC;

        void Flush()
        {
            _s.Exact(type, cur.Span);
            cur.Clear();
            type = 0x003C;
            recordStart = _s.Position;
        }

        for (int k = 0; k < _sstList.Count; k++)
        {
            string s = _sstList[k];
            bool wide = !BiffBuffer.IsCompressible(s);
            int width = wide ? 2 : 1;
            if (BiffSink.MaxData - cur.Length < 4 + width && s.Length > 0 || BiffSink.MaxData - cur.Length < 3)
            {
                Flush();
            }

            if (k % perBucket == 0)
            {
                // ISSTInf: the string's stream position and its offset from the start of the record that holds it, header included.
                int headerLength = 4;
                bucketPositions.Add((recordStart + headerLength + cur.Length, headerLength + cur.Length));
            }

            cur.U16(s.Length);
            cur.U8(wide ? 1 : 0);
            int done = 0;
            while (done < s.Length)
            {
                int room = BiffSink.MaxData - cur.Length;
                int fit = room / width;
                if (fit == 0)
                {
                    Flush();
                    cur.U8(wide ? 1 : 0);
                    continue;
                }

                int n = Math.Min(fit, s.Length - done);
                cur.Chars(s.Substring(done, n), wide);
                done += n;
                if (done < s.Length)
                {
                    Flush();
                    cur.U8(wide ? 1 : 0);
                }
            }
        }

        Flush();
        var ext = new BiffBuffer();
        ext.U16(perBucket);
        foreach ((int pos, int off) in bucketPositions)
        {
            ext.U32((uint)pos);
            ext.U16(off);
            ext.U16(0);
        }

        _s.Record(0x00FF, ext);
    }
}
