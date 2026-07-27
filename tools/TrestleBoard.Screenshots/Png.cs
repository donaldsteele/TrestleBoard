using System.Buffers.Binary;
using System.Text;

namespace TrestleBoard.Screenshots;

/// <summary>
/// PNG chunk surgery, for one reason only: <b>encoders stamp file paths into ancillary text
/// chunks</b>, and a path on this machine bears the maintainer's account name. That is real
/// personal data in a public repository (PLAN.md §0 rule 6), so every byte array that leaves this
/// tool goes through <see cref="Sanitise"/> first, whatever the encoder did or did not write.
///
/// <see cref="HasTextChunks"/> is the same walk without the rewrite; <c>DocsTests</c> asserts it
/// over the committed images, so the guarantee is checked by CI even though the tool is not run by
/// it.
/// </summary>
internal static class Png
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The three chunk types that carry text, and therefore paths and user names.</summary>
    private static readonly string[] TextChunks = ["tEXt", "iTXt", "zTXt"];

    /// <summary>Returns the same image with every text chunk removed.</summary>
    public static byte[] Sanitise(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (!HasTextChunks(png))
        {
            return png;
        }

        var output = new MemoryStream(png.Length);
        output.Write(Signature);

        foreach ((int start, int length, string type) in Chunks(png))
        {
            if (!IsTextChunk(type))
            {
                output.Write(png, start, length);
            }
        }

        return output.ToArray();
    }

    /// <summary>True if the image carries a <c>tEXt</c>, <c>iTXt</c> or <c>zTXt</c> chunk.</summary>
    public static bool HasTextChunks(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        return Chunks(png).Any(c => IsTextChunk(c.Type));
    }

    private static bool IsTextChunk(string type) =>
        TextChunks.Contains(type, StringComparer.Ordinal);

    /// <summary>
    /// Walks the chunk stream: 8-byte signature, then length/type/data/CRC records. Stops at
    /// <c>IEND</c> or at the first malformed record rather than reading past the buffer.
    /// </summary>
    private static IEnumerable<(int Start, int Length, string Type)> Chunks(byte[] png)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            yield break;
        }

        int offset = Signature.Length;
        while (offset + 12 <= png.Length)
        {
            int dataLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (dataLength < 0 || offset + 12 + dataLength > png.Length)
            {
                yield break;
            }

            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            yield return (offset, dataLength + 12, type);

            offset += dataLength + 12;
            if (string.Equals(type, "IEND", StringComparison.Ordinal))
            {
                yield break;
            }
        }
    }
}
