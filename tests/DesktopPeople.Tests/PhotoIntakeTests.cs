using DesktopPeople.Core;

namespace DesktopPeople.Tests;

/// <summary>
/// The front door for a user's photograph. Every case here is about a file that is not what it
/// says it is, or is a size nothing should try to decode.
/// </summary>
internal static class PhotoIntakeTests
{
    public static TestCase[] All =>
    [
        new("intake reads the size of a PNG", ReadsPng),
        new("intake reads the size of a JPEG behind other segments", ReadsJpegPastSegments),
        new("intake reads the size of each WebP frame type", ReadsWebPVariants),
        new("intake refuses an animated WebP", RefusesAnimatedWebP),
        new("intake refuses a file renamed to look like an image", RefusesByContentNotExtension),
        new("intake refuses a decompression bomb", RefusesBomb),
        new("intake refuses an image too small to build a character from", RefusesTiny),
        new("intake refuses a truncated header", RefusesTruncated),
    ];

    private static void ReadsPng()
    {
        PhotoIntakeResult result = PhotoIntake.Inspect(Png(1024, 768));

        AssertEx.True(result.Accepted);
        AssertEx.Equal(PhotoFormat.Png, result.Format);
        AssertEx.Equal(1024, result.Width);
        AssertEx.Equal(768, result.Height);
    }

    /// <summary>A photo out of a phone carries EXIF, a colour profile and often a thumbnail
    /// before the frame header, so the size cannot be read at a fixed offset.</summary>
    private static void ReadsJpegPastSegments()
    {
        PhotoIntakeResult result = PhotoIntake.Inspect(Jpeg(4032, 3024, precedingSegments: 3));

        AssertEx.True(result.Accepted);
        AssertEx.Equal(PhotoFormat.Jpeg, result.Format);
        AssertEx.Equal(4032, result.Width);
        AssertEx.Equal(3024, result.Height);
    }

    private static void ReadsWebPVariants()
    {
        foreach ((byte[] file, string variant) in new[]
        {
            (WebPLossy(800, 600), "VP8 "),
            (WebPLossless(800, 600), "VP8L"),
            (WebPExtended(800, 600, animated: false), "VP8X"),
        })
        {
            PhotoIntakeResult result = PhotoIntake.Inspect(file);
            AssertEx.True(result.Accepted);
            AssertEx.Equal(PhotoFormat.WebP, result.Format);
            AssertEx.Equal(800, result.Width);
            AssertEx.Equal(600, result.Height);
        }
    }

    private static void RefusesAnimatedWebP()
    {
        PhotoIntakeResult result = PhotoIntake.Inspect(WebPExtended(800, 600, animated: true));

        AssertEx.False(result.Accepted);
        AssertEx.True(result.Rejection is not null);
    }

    /// <summary>The extension is the one part of a file anyone can change, so the decision has to
    /// come from the bytes.</summary>
    private static void RefusesByContentNotExtension()
    {
        byte[] notAnImage = new byte[4096];
        "MZ"u8.CopyTo(notAnImage);

        PhotoIntakeResult result = PhotoIntake.Inspect(notAnImage);

        AssertEx.False(result.Accepted);
        AssertEx.Equal(PhotoFormat.Unknown, result.Format);
    }

    /// <summary>A few hundred bytes on disk that ask a decoder for gigabytes of memory. The header
    /// is the only place this can be caught — by the time it is decoded the damage is done.</summary>
    private static void RefusesBomb()
    {
        AssertEx.False(PhotoIntake.Inspect(Png(50_000, 50_000)).Accepted);

        // Both sides within the per-side limit, and still 144 megapixels together.
        AssertEx.False(PhotoIntake.Inspect(Png(12_000, 12_000)).Accepted);
    }

    private static void RefusesTiny()
    {
        AssertEx.False(PhotoIntake.Inspect(Png(32, 32)).Accepted);
    }

    private static void RefusesTruncated()
    {
        AssertEx.False(PhotoIntake.Inspect(Png(1024, 768).AsSpan(0, 20).ToArray()).Accepted);
        AssertEx.False(PhotoIntake.Inspect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]).Accepted);
    }

    private static byte[] Png(int width, int height)
    {
        var file = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(file, 0);
        file[11] = 13;                       // IHDR length
        "IHDR"u8.CopyTo(file.AsSpan(12));
        WriteBigEndian32(file, 16, width);
        WriteBigEndian32(file, 20, height);
        return file;
    }

    private static byte[] Jpeg(int width, int height, int precedingSegments)
    {
        var file = new List<byte> { 0xFF, 0xD8 };
        for (int segment = 0; segment < precedingSegments; segment++)
        {
            file.AddRange([0xFF, 0xE1, 0x00, 0x20]);
            file.AddRange(new byte[30]);
        }

        file.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        file.AddRange([(byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width]);
        file.AddRange(new byte[8]);
        return [.. file];
    }

    private static byte[] WebPLossy(int width, int height)
    {
        byte[] file = WebPContainer("VP8 ");
        file[23] = 0x9D;
        file[24] = 0x01;
        file[25] = 0x2A;
        file[26] = (byte)(width & 0xFF);
        file[27] = (byte)(width >> 8);
        file[28] = (byte)(height & 0xFF);
        file[29] = (byte)(height >> 8);
        return file;
    }

    private static byte[] WebPLossless(int width, int height)
    {
        byte[] file = WebPContainer("VP8L");
        file[20] = 0x2F;
        uint packed = (uint)((width - 1) & 0x3FFF) | ((uint)((height - 1) & 0x3FFF) << 14);
        file[21] = (byte)packed;
        file[22] = (byte)(packed >> 8);
        file[23] = (byte)(packed >> 16);
        file[24] = (byte)(packed >> 24);
        return file;
    }

    private static byte[] WebPExtended(int width, int height, bool animated)
    {
        byte[] file = WebPContainer("VP8X");
        file[20] = (byte)(animated ? 0x02 : 0x00);
        WriteLittleEndian24(file, 24, width - 1);
        WriteLittleEndian24(file, 27, height - 1);
        return file;
    }

    private static byte[] WebPContainer(string chunk)
    {
        var file = new byte[32];
        "RIFF"u8.CopyTo(file);
        "WEBP"u8.CopyTo(file.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(file, 12);
        return file;
    }

    private static void WriteBigEndian32(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static void WriteLittleEndian24(byte[] target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
    }
}
