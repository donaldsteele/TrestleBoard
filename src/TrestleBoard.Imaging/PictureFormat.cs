using System.Buffers.Binary;
using System.Text;

namespace TrestleBoard.Imaging;

/// <summary>What a file's own bytes say it is.</summary>
public enum PictureFormatKind
{
    /// <summary>Nothing recognised. Not necessarily broken — just not one of the few named here.</summary>
    Unknown,

    Jpeg,

    Png,

    Gif,

    Bmp,

    WebP,

    Tiff,

    /// <summary>
    /// An iPhone photograph. Skia cannot decode one, which is the whole reason this enum exists
    /// (PLAN.md §11 M80).
    /// </summary>
    Heic,
}

/// <summary>
/// Reads a picture's format out of its first few bytes (PLAN.md §11 M80).
///
/// <para><b>Why the bytes and not the file name.</b> "My phone photo will not open" is the sharpest
/// daily complaint this app produces, and it arrives by three routes — the file picker, a drag onto
/// the page, and the clipboard — only one of which has a file name attached. A photograph dropped
/// from a phone-sync folder is a HEIC whatever it is called, and one renamed to <c>.jpg</c> by a
/// well-meaning relative is still a HEIC. The bytes are the only thing all three routes share.</para>
///
/// <para><b>Pure and static</b>, so the sniffing can be asserted against a handful of real headers
/// with no disk, no Skia and no operating system in the way.</para>
/// </summary>
public static class PictureFormat
{
    /// <summary>The most bytes this ever needs to look at.</summary>
    public const int HeaderBytes = 32;

    /// <summary>
    /// What these bytes are. Never throws and never guesses beyond the signature: a file it does
    /// not recognise is <see cref="PictureFormatKind.Unknown"/>, which the caller already had to
    /// handle because a decode can fail for a dozen other reasons.
    /// </summary>
    public static PictureFormatKind Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
        {
            return PictureFormatKind.Unknown;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return PictureFormatKind.Jpeg;
        }

        ReadOnlySpan<byte> pngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes[..8].SequenceEqual(pngSignature))
        {
            return PictureFormatKind.Png;
        }

        if (bytes[..3].SequenceEqual("GIF"u8))
        {
            return PictureFormatKind.Gif;
        }

        if (bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return PictureFormatKind.Bmp;
        }

        if (bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return PictureFormatKind.WebP;
        }

        ReadOnlySpan<byte> tiffLittleEndian = [0x49, 0x49, 0x2A, 0x00];
        ReadOnlySpan<byte> tiffBigEndian = [0x4D, 0x4D, 0x00, 0x2A];
        if (bytes[..4].SequenceEqual(tiffLittleEndian) || bytes[..4].SequenceEqual(tiffBigEndian))
        {
            return PictureFormatKind.Tiff;
        }

        return IsHeic(bytes) ? PictureFormatKind.Heic : PictureFormatKind.Unknown;
    }

    /// <summary>
    /// Whether these bytes are an ISO base-media file whose brand says HEIF.
    ///
    /// <para>The structure is <c>[4-byte length][ftyp][major brand][minor version][compatible
    /// brands…]</c>. Both the major brand and the compatible brands are checked, because an iPhone
    /// writes <c>heic</c> as its major brand and an iPhone burst or a Live Photo writes <c>mif1</c>
    /// with <c>heic</c> further along — and to this app they are the same problem.</para>
    /// </summary>
    private static bool IsHeic(ReadOnlySpan<byte> bytes)
    {
        if (!bytes[4..8].SequenceEqual("ftyp"u8))
        {
            return false;
        }

        uint boxLength = BinaryPrimitives.ReadUInt32BigEndian(bytes[..4]);
        int end = (int)Math.Min(Math.Max(boxLength, 12u), (uint)bytes.Length);

        // Every brand is four bytes, starting at the major brand at offset 8.
        for (int at = 8; at + 4 <= end; at += 4)
        {
            if (HeifBrands.Contains(Encoding.ASCII.GetString(bytes[at..(at + 4)])))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The brands an iPhone actually writes. Deliberately a list rather than a prefix test: "hei"
    /// would also match brands that are not images at all, and this decides what the user is told.
    /// </summary>
    private static readonly HashSet<string> HeifBrands = new(StringComparer.Ordinal)
    {
        "heic", "heix", "heim", "heis", "hevc", "hevx", "hevm", "hevs", "mif1", "msf1",
    };
}
