using System.Buffers.Binary;

namespace RecursiveAI.OneDocument.Doc.Binary;

/// <summary>
/// One property modifier with its operand: a <c>Prl</c> ([MS-DOC] 2.6, "Single Property Modifiers"). The operand is kept as the bytes
/// found in the file, so a modifier the reader does not know is carried through unchanged.
/// </summary>
/// <param name="Sprm">The 16-bit modifier: ispmd (9 bits), fSpec (1), sgc (3: 1 paragraph, 2 character, 3 picture, 4 section, 5 table) and spra (3).</param>
/// <param name="Operand">The operand bytes. For the variable-length forms this includes the leading size byte (or the 16-bit size of <c>sprmTDefTable</c>).</param>
public readonly record struct Prl(ushort Sprm, ReadOnlyMemory<byte> Operand)
{
    /// <summary>Gets the modifier's group: 1 paragraph, 2 character, 3 picture, 4 section, 5 table.</summary>
    public int Sgc => (Sprm >> 10) & 7;

    /// <summary>Gets the operand-size class (spra).</summary>
    public int Spra => Sprm >> 13;

    /// <summary>Gets the name [MS-DOC] gives the modifier, or <c>null</c> for one it does not list.</summary>
    public string? Name => SprmNames.NameOf(Sprm);

    /// <summary>Gets the first operand byte, or 0 when there is none.</summary>
    public byte OperandByte => Operand.Length > 0 ? Operand.Span[0] : (byte)0;

    /// <summary>Gets the operand read as an unsigned 16-bit integer, or 0 when it is shorter.</summary>
    public int OperandU16 => Operand.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(Operand.Span) : 0;

    /// <summary>Gets the operand read as a signed 16-bit integer, or 0 when it is shorter.</summary>
    public int OperandI16 => Operand.Length >= 2 ? BinaryPrimitives.ReadInt16LittleEndian(Operand.Span) : 0;

    /// <summary>Gets the operand read as a signed 32-bit integer, or 0 when it is shorter.</summary>
    public int OperandI32 => Operand.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(Operand.Span) : 0;
}

/// <summary>Reads the list of property modifiers (<c>grpprl</c>) that character, paragraph, section and table formatting is stored as.</summary>
public static class SprmReader
{
    /// <summary>
    /// Splits a <c>grpprl</c> into its modifiers. A modifier whose operand runs past the end of the data ends the list; what was read
    /// before it is returned and <paramref name="truncated"/> is set.
    /// </summary>
    /// <param name="grpprl">The modifier list.</param>
    /// <param name="truncated">True if the data ended inside a modifier.</param>
    /// <returns>The modifiers in file order, operands as views of <paramref name="grpprl"/>.</returns>
    public static List<Prl> Read(ReadOnlyMemory<byte> grpprl, out bool truncated)
    {
        var list = new List<Prl>();
        truncated = false;
        ReadOnlySpan<byte> s = grpprl.Span;
        int pos = 0;
        while (pos + 2 <= s.Length)
        {
            ushort sprm = BinaryPrimitives.ReadUInt16LittleEndian(s[pos..]);
            int start = pos + 2;
            int size = OperandSize(sprm, s, start);
            if (size < 0 || start + size > s.Length)
            {
                truncated = true;
                break;
            }

            list.Add(new Prl(sprm, grpprl.Slice(start, size)));
            pos = start + size;
        }

        if (pos < s.Length && !truncated && s.Length - pos == 1)
        {
            // A single stray byte: the list was padded to an even length.
            return list;
        }

        return list;
    }

    // [MS-DOC] 2.6: spra gives the size class. Class 6 is a size byte, except for sprmTDefTable (a 16-bit size that counts itself) and
    // sprmPChgTabs/sprmPChgTabsPapx (a size byte whose value 255 means the size is found by walking the structure).
    internal static int OperandSize(ushort sprm, ReadOnlySpan<byte> data, int start)
    {
        switch (sprm >> 13)
        {
            case 0:
            case 1:
                return 1;
            case 2:
            case 4:
            case 5:
                return 2;
            case 3:
                return 4;
            case 7:
                return 3;
        }

        if (start >= data.Length)
        {
            return -1;
        }

        if (sprm == 0xD608)
        {
            return start + 2 <= data.Length ? BinaryPrimitives.ReadUInt16LittleEndian(data[start..]) + 1 : -1;
        }

        int cb = data[start];
        if ((sprm == 0xC615 || sprm == 0xC60D) && cb == 255)
        {
            // [MS-DOC] PChgTabsOperand: itbdDelMax, rgdxaDel, rgdxaClose, itbdAddMax, rgdxaAdd, rgtbdAdd.
            int p = start + 1;
            if (p >= data.Length)
            {
                return -1;
            }

            int del = data[p];
            p += 1 + (del * 4);
            if (p >= data.Length)
            {
                return -1;
            }

            int add = data[p];
            p += 1 + (add * 3);
            return p - start;
        }

        return 1 + cb;
    }
}

/// <summary>The names [MS-DOC] gives the property modifiers.</summary>
public static class SprmNames
{
    /// <summary>Gets the name of a modifier.</summary>
    /// <param name="sprm">The 16-bit modifier value.</param>
    /// <returns>The name, for example <c>sprmCFBold</c>; <c>null</c> if [MS-DOC] section 2.6 does not list it.</returns>
    public static string? NameOf(ushort sprm)
    {
        var table = SprmTable.Names;
        int lo = 0;
        int hi = table.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            ushort v = table[mid].Opcode;
            if (v == sprm)
            {
                return table[mid].Name;
            }

            if (v < sprm)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return null;
    }
}
