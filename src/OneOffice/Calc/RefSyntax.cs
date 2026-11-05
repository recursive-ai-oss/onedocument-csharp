using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>Small scanners for the pieces of reference syntax shared by the parser and the printer.</summary>
internal static class RefSyntax
{
    public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '\\' || c > 127;

    // ECMA-376 Part 1 §18.17.2 (name syntax) and Excel's behaviour: names may contain letters, digits, '_', '.', '\\' and '?'.
    public static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '\\' || c == '?' || c > 127;

    public static string ColumnName(int column)
    {
        Span<char> buf = stackalloc char[3]; // Columns top out at XFD (16,384): three letters.
        int i = 3;
        while (column > 0)
        {
            column--;
            buf[--i] = (char)('A' + (column % 26));
            column /= 26;
        }

        return new string(buf[i..]);
    }

    /// <summary>Reads 1–3 letters as a column number; 0 when the letters are past XFD.</summary>
    public static int ColumnNumber(ReadOnlySpan<char> letters)
    {
        int n = 0;
        foreach (char c in letters)
        {
            n = (n * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return n is >= 1 and <= CellArea.MaxColumn ? n : 0;
    }

    /// <summary>Scans <c>$?letters$?digits</c> at <paramref name="start"/>. The reference must be a valid cell and not run into more name characters.</summary>
    public static bool TryScanCell(string s, int start, out int end, out RefCoord row, out RefCoord col)
    {
        row = default;
        col = default;
        end = start;
        int p = start;
        bool colAbs = false;
        if (p < s.Length && s[p] == '$')
        {
            colAbs = true;
            p++;
        }

        int ls = p;
        while (p < s.Length && p - ls < 4 && s[p] < 128 && char.IsAsciiLetter(s[p]))
        {
            p++;
        }

        int letters = p - ls;
        if (letters is < 1 or > 3)
        {
            return false;
        }

        bool rowAbs = false;
        if (p < s.Length && s[p] == '$')
        {
            rowAbs = true;
            p++;
        }

        int ds = p;
        long rowNum = 0;
        while (p < s.Length && char.IsAsciiDigit(s[p]) && p - ds < 9)
        {
            rowNum = (rowNum * 10) + (s[p] - '0');
            p++;
        }

        if (p == ds || rowNum is < 1 or > CellArea.MaxRow)
        {
            return false;
        }

        int colNum = ColumnNumber(s.AsSpan(ls, letters));
        if (colNum == 0 || (p < s.Length && (IsIdentChar(s[p]) || s[p] == '(' || s[p] == '!')))
        {
            return false;
        }

        end = p;
        row = new RefCoord((int)rowNum, rowAbs);
        col = new RefCoord(colNum, colAbs);
        return true;
    }

    /// <summary>Scans <c>$?letters</c> for a whole-column bound.</summary>
    public static bool TryScanColumn(string s, int start, out int end, out RefCoord col)
    {
        col = default;
        end = start;
        int p = start;
        bool abs = false;
        if (p < s.Length && s[p] == '$')
        {
            abs = true;
            p++;
        }

        int ls = p;
        while (p < s.Length && s[p] < 128 && char.IsAsciiLetter(s[p]) && p - ls < 4)
        {
            p++;
        }

        int colNum = p - ls is >= 1 and <= 3 ? ColumnNumber(s.AsSpan(ls, p - ls)) : 0;
        if (colNum == 0 || (p < s.Length && (IsIdentChar(s[p]) || s[p] == '(' || s[p] == '!' || s[p] == '$')))
        {
            return false;
        }

        end = p;
        col = new RefCoord(colNum, abs);
        return true;
    }

    /// <summary>Scans <c>$?digits</c> for a whole-row bound.</summary>
    public static bool TryScanRow(string s, int start, out int end, out RefCoord row)
    {
        row = default;
        end = start;
        int p = start;
        bool abs = false;
        if (p < s.Length && s[p] == '$')
        {
            abs = true;
            p++;
        }

        int ds = p;
        long n = 0;
        while (p < s.Length && char.IsAsciiDigit(s[p]) && p - ds < 9)
        {
            n = (n * 10) + (s[p] - '0');
            p++;
        }

        if (p == ds || n is < 1 or > CellArea.MaxRow || (p < s.Length && (IsIdentChar(s[p]) || s[p] == '(' || s[p] == '$' || s[p] == '.')))
        {
            return false;
        }

        end = p;
        row = new RefCoord((int)n, abs);
        return true;
    }

    public static bool LooksLikeCell(string name) => TryScanCell(name, 0, out int e, out _, out _) && e == name.Length;

    public static bool LooksLikeR1C1(string name)
    {
        return TryScanR1C1(name, 0, out int len, out _, out _, out _) && len == name.Length;
    }

    /// <summary>One R1C1 coordinate as written: absolute number, relative offset, or omitted (relative, offset 0).</summary>
    public readonly record struct Rc(bool Absolute, int Value);

    public enum R1C1Kind
    {
        Cell,
        Row,
        Column,
    }

    /// <summary>Scans <c>R1C1</c>, <c>R[-1]C[2]</c>, <c>RC</c>, <c>R5</c>, <c>C[1]</c> and the like (ECMA-376 Part 1 §18.17.2 allows R1C1 text in files only for <c>FormulaR1C1</c>-style APIs; Excel's object model defines the notation).</summary>
    public static bool TryScanR1C1(string s, int start, out int length, out R1C1Kind kind, out Rc row, out Rc col)
    {
        length = 0;
        kind = R1C1Kind.Cell;
        row = default;
        col = default;
        int p = start;
        bool hasRow = false;
        bool hasCol = false;
        if (p < s.Length && (s[p] == 'R' || s[p] == 'r'))
        {
            p++;
            hasRow = true;
            if (!ScanRc(s, ref p, out row))
            {
                return false;
            }
        }

        if (p < s.Length && (s[p] == 'C' || s[p] == 'c'))
        {
            p++;
            hasCol = true;
            if (!ScanRc(s, ref p, out col))
            {
                return false;
            }
        }

        if (!hasRow && !hasCol)
        {
            return false;
        }

        if (p < s.Length && (IsIdentChar(s[p]) || s[p] == '(' || s[p] == '!'))
        {
            return false;
        }

        kind = hasRow && hasCol ? R1C1Kind.Cell : hasRow ? R1C1Kind.Row : R1C1Kind.Column;
        length = p - start;
        return true;
    }

    private static bool ScanRc(string s, ref int p, out Rc value)
    {
        value = default;
        if (p < s.Length && s[p] == '[')
        {
            int q = p + 1;
            bool neg = false;
            if (q < s.Length && (s[q] == '-' || s[q] == '+'))
            {
                neg = s[q] == '-';
                q++;
            }

            int ds = q;
            long n = 0;
            while (q < s.Length && char.IsAsciiDigit(s[q]) && q - ds < 9)
            {
                n = (n * 10) + (s[q] - '0');
                q++;
            }

            if (q == ds || q >= s.Length || s[q] != ']')
            {
                return false;
            }

            value = new Rc(false, (int)(neg ? -n : n));
            p = q + 1;
            return true;
        }

        int d = p;
        long v = 0;
        while (p < s.Length && char.IsAsciiDigit(s[p]) && p - d < 9)
        {
            v = (v * 10) + (s[p] - '0');
            p++;
        }

        if (p == d)
        {
            value = new Rc(false, 0);
            return true;
        }

        if (v < 1)
        {
            return false;
        }

        value = new Rc(true, (int)v);
        return true;
    }

    /// <summary>Splits a table specifier into its items: <c>[[#This Row],[Col]]</c> gives <c>#This Row</c>, <c>Col</c>; <c>[@Col]</c> and <c>[@[Col]]</c> give <c>#This Row</c>, <c>Col</c>.</summary>
    public static List<string> SplitStructuredItems(string spec)
    {
        var items = new List<string>();
        var starts = new Stack<(int Start, int ItemsAtOpen, bool HasChildBracket)>();
        for (int i = 0; i < spec.Length; i++)
        {
            char c = spec[i];
            if (c == '\'' && i + 1 < spec.Length)
            {
                i++; // ECMA-376 Part 1 §18.17.2: a single quote escapes the next character in a column name.
                continue;
            }

            if (c == '[')
            {
                if (starts.Count > 0)
                {
                    (int st, int n, _) = starts.Pop();
                    starts.Push((st, n, true));
                }

                starts.Push((i, items.Count, false));
            }
            else if (c == ']' && starts.Count > 0)
            {
                (int st, int n, bool child) = starts.Pop();
                string content = spec.Substring(st + 1, i - st - 1);
                if (!child)
                {
                    if (content.Length > 0 && content[0] == '@')
                    {
                        items.Add("#This Row");
                        content = content[1..];
                    }

                    if (content.Length > 0)
                    {
                        items.Add(content.Replace("'", string.Empty, StringComparison.Ordinal));
                    }
                }
                else if (content.Length > 0 && content[0] == '@')
                {
                    items.Insert(n, "#This Row");
                }
            }
        }

        return items;
    }
}
