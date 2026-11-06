using System.Numerics;

namespace RecursiveAI.OneDocument.Vba;

/// <summary>
/// The run-length and dictionary compression VBA uses for the <c>dir</c> stream and for module source ([MS-OVBA] §2.4.1). Only the
/// decompressor is public: nothing here writes a VBA project.
/// </summary>
/// <remarks>Every length is checked. Damaged input never throws: <see cref="TryDecompress"/> returns what could be decoded and says why it stopped.</remarks>
public static class VbaCompression
{
    // [MS-OVBA] §2.4.1.1.4: a chunk decompresses to at most 4096 bytes.
    private const int ChunkSize = 4096;

    /// <summary>Decompresses a <c>CompressedContainer</c> ([MS-OVBA] §2.4.1.1.1).</summary>
    /// <param name="compressed">The container: a 0x01 signature byte, then chunks.</param>
    /// <param name="maxOutput">The most bytes to produce; the decode stops with a problem when it would exceed this. At least 0.</param>
    /// <param name="decompressed">Everything decoded before the first problem (all of it when <paramref name="problem"/> is null).</param>
    /// <param name="problem">Null on success; otherwise a short statement of what was wrong and at which input offset.</param>
    /// <returns>True when the whole container decoded cleanly.</returns>
    public static bool TryDecompress(ReadOnlySpan<byte> compressed, long maxOutput, out byte[] decompressed, out string? problem)
    {
        problem = null;
        var output = new List<byte>((int)Math.Min(compressed.Length * 3L, 1L << 20));
        if (compressed.IsEmpty)
        {
            decompressed = [];
            problem = "The container is empty (offset 0).";
            return false;
        }

        if (compressed[0] != 0x01)
        {
            decompressed = [];
            problem = "The container signature byte is " + compressed[0].ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + ", not 01 (offset 0).";
            return false;
        }

        int pos = 1;
        // Streams written by Office can end with zero padding after the last chunk (seen in PowerPoint files); a zero header cannot be a chunk.
        int lastData = compressed.LastIndexOfAnyExcept((byte)0);
        while (pos <= lastData)
        {
            if (pos + 2 > compressed.Length)
            {
                problem = "A chunk header is cut short (offset " + pos + ").";
                break;
            }

            int header = compressed[pos] | (compressed[pos + 1] << 8);
            int chunkEnd = pos + (header & 0x0FFF) + 3; // CompressedChunkSize counts the header and the data, minus 3.
            bool isCompressed = (header & 0x8000) != 0;
            if (((header >> 12) & 0x7) != 0x3)
            {
                problem = "A chunk header has the wrong signature (offset " + pos + ").";
                break;
            }

            int start = output.Count;
            int at = pos + 2;
            if (!isCompressed)
            {
                // A raw chunk is exactly 4096 bytes of data; a last chunk may be shorter when the writer stopped early, so take what is there.
                int available = Math.Min(chunkEnd, compressed.Length) - at;
                if (chunkEnd > compressed.Length)
                {
                    problem = "A raw chunk runs past the end of the data (offset " + pos + ").";
                }

                available = Math.Min(available, ChunkSize);
                if (output.Count + (long)available > maxOutput)
                {
                    problem = "The decompressed size passes the limit (offset " + pos + ").";
                    break;
                }

                for (int i = 0; i < available; i++)
                {
                    output.Add(compressed[at + i]);
                }

                if (problem is not null)
                {
                    break;
                }

                pos = chunkEnd;
                continue;
            }

            int limit = Math.Min(chunkEnd, compressed.Length);
            bool failed = false;
            while (at < limit && !failed)
            {
                int flags = compressed[at++];
                for (int bit = 0; bit < 8 && at < limit; bit++)
                {
                    if ((flags & (1 << bit)) == 0)
                    {
                        output.Add(compressed[at++]);
                    }
                    else
                    {
                        if (at + 2 > limit)
                        {
                            problem = "A copy token is cut short (offset " + at + ").";
                            failed = true;
                            break;
                        }

                        int token = compressed[at] | (compressed[at + 1] << 8);
                        int difference = output.Count - start;
                        if (difference == 0)
                        {
                            problem = "A copy token comes before any data in its chunk (offset " + at + ").";
                            failed = true;
                            break;
                        }

                        // [MS-OVBA] §2.4.1.3.19.1: the offset field is as wide as the position in the chunk needs, at least 4 bits.
                        int bitCount = Math.Max(difference > 1 ? 32 - BitOperations.LeadingZeroCount((uint)(difference - 1)) : 0, 4);
                        int lengthMask = 0xFFFF >> bitCount;
                        int offset = ((token & ~lengthMask & 0xFFFF) >> (16 - bitCount)) + 1;
                        int length = (token & lengthMask) + 3;
                        if (offset > difference)
                        {
                            problem = "A copy token points before the start of its chunk (offset " + at + ").";
                            failed = true;
                            break;
                        }

                        if (difference + (long)length > ChunkSize)
                        {
                            // The spec stops a copy at the end of the chunk; a longer one is damage, and the rest of the chunk is dropped.
                            length = ChunkSize - difference;
                            problem = "A copy token runs past the end of its chunk (offset " + at + ").";
                            failed = true;
                        }

                        if (output.Count + (long)length > maxOutput)
                        {
                            problem = "The decompressed size passes the limit (offset " + at + ").";
                            failed = true;
                            break;
                        }

                        for (int i = 0; i < length; i++)
                        {
                            output.Add(output[output.Count - offset]);
                        }

                        at += 2;
                        if (failed)
                        {
                            break;
                        }
                    }

                    if (output.Count - start > ChunkSize)
                    {
                        problem = "A chunk decompresses to more than 4096 bytes (offset " + pos + ").";
                        failed = true;
                        break;
                    }
                }
            }

            if (failed)
            {
                break;
            }

            if (chunkEnd > compressed.Length)
            {
                problem = "A chunk runs past the end of the data (offset " + pos + ").";
                break;
            }

            if (output.Count > maxOutput)
            {
                problem = "The decompressed size passes the limit (offset " + pos + ").";
                break;
            }

            pos = chunkEnd;
        }

        decompressed = [.. output];
        return problem is null;
    }

    /// <summary>Decompresses a container, discarding any damage: the bytes decoded before the first problem are returned.</summary>
    /// <param name="compressed">The container.</param>
    /// <param name="maxOutput">The most bytes to produce.</param>
    /// <returns>The decoded bytes, possibly fewer than the container held.</returns>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, long maxOutput = int.MaxValue)
    {
        TryDecompress(compressed, maxOutput, out byte[] data, out _);
        return data;
    }
}
