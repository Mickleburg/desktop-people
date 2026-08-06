namespace DesktopPeople.Core;

public enum PhotoFormat
{
    Unknown,
    Png,
    Jpeg,
    WebP,
}

/// <summary>What the intake made of a file: either an accepted still photo with its real size,
/// or a refusal with the reason to show the user.</summary>
public readonly record struct PhotoIntakeResult(
    bool Accepted,
    PhotoFormat Format,
    int Width,
    int Height,
    string? Rejection)
{
    public static PhotoIntakeResult Reject(string reason) =>
        new(false, PhotoFormat.Unknown, 0, 0, reason);
}

/// <summary>
/// The front door for a user's photograph: it decides whether a file is a still image this
/// application will work with, and how big that image claims to be, by reading its header alone.
/// <para>
/// Header-only, deliberately. The size a file declares is what a decoder will allocate for, so
/// checking it before decoding is what keeps a 200-megapixel image (a few hundred kilobytes on
/// disk) from turning into gigabytes of memory. The format is taken from the file's own bytes
/// rather than its extension, because the extension is the one part of a file anybody can
/// rename.
/// </para>
/// <para>
/// What this is NOT: protection against a malicious file that exploits the decoder itself. It
/// bounds resource use and rejects malformed and unsupported input; a hardened decoder is a
/// separate concern from a plausible header.
/// </para>
/// </summary>
public static class PhotoIntake
{
    public const int MaximumFileBytes = 40 * 1024 * 1024;
    public const int MaximumDimension = 12_000;
    public const long MaximumPixels = 60_000_000;
    public const int MinimumDimension = 64;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static PhotoIntakeResult Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumFileBytes)
        {
            return PhotoIntakeResult.Reject("Файл больше 40 МБ.");
        }

        if (bytes.Length < 16)
        {
            return PhotoIntakeResult.Reject("Файл слишком мал, чтобы быть изображением.");
        }

        PhotoIntakeResult header =
            TryReadPng(bytes) ?? TryReadJpeg(bytes) ?? TryReadWebP(bytes) ??
            PhotoIntakeResult.Reject("Поддерживаются только PNG, JPEG и WebP.");

        return header.Accepted ? WithinLimits(header) : header;
    }

    private static PhotoIntakeResult WithinLimits(PhotoIntakeResult header)
    {
        if (header.Width < MinimumDimension || header.Height < MinimumDimension)
        {
            return PhotoIntakeResult.Reject(
                $"Изображение меньше {MinimumDimension}×{MinimumDimension} — из него не собрать персонажа.");
        }

        if (header.Width > MaximumDimension || header.Height > MaximumDimension)
        {
            return PhotoIntakeResult.Reject($"Сторона изображения больше {MaximumDimension} пикселей.");
        }

        // Checked separately from the per-side limit: 12000×12000 passes both sides and is 144
        // megapixels, more than half a gigabyte once decoded to RGBA.
        if ((long)header.Width * header.Height > MaximumPixels)
        {
            return PhotoIntakeResult.Reject("Изображение больше 60 мегапикселей.");
        }

        return header;
    }

    private static PhotoIntakeResult? TryReadPng(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith(PngSignature))
        {
            return null;
        }

        // IHDR is required by the format to be the first chunk, so width and height sit at fixed
        // offsets: 8 signature + 4 length + 4 type.
        if (bytes.Length < 24 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return PhotoIntakeResult.Reject("PNG повреждён: нет заголовка IHDR.");
        }

        return new PhotoIntakeResult(
            true, PhotoFormat.Png, ReadBigEndian32(bytes, 16), ReadBigEndian32(bytes, 20), null);
    }

    private static PhotoIntakeResult? TryReadJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF)
        {
            return null;
        }

        // JPEG keeps its dimensions in a start-of-frame segment that can sit behind any number of
        // other segments (EXIF, colour profiles, thumbnails), so the chain has to be walked.
        int offset = 2;
        while (offset + 3 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                return PhotoIntakeResult.Reject("JPEG повреждён: сбита структура сегментов.");
            }

            byte marker = bytes[offset + 1];

            // Padding between segments is legal and is written as repeated 0xFF bytes.
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            if (IsStartOfFrame(marker))
            {
                if (offset + 9 >= bytes.Length)
                {
                    return PhotoIntakeResult.Reject("JPEG обрывается на заголовке кадра.");
                }

                return new PhotoIntakeResult(
                    true,
                    PhotoFormat.Jpeg,
                    (bytes[offset + 7] << 8) | bytes[offset + 8],
                    (bytes[offset + 5] << 8) | bytes[offset + 6],
                    null);
            }

            // Start of scan: the compressed data begins here and no frame header follows it.
            if (marker == 0xDA)
            {
                break;
            }

            int length = (bytes[offset + 2] << 8) | bytes[offset + 3];
            if (length < 2)
            {
                return PhotoIntakeResult.Reject("JPEG повреждён: некорректная длина сегмента.");
            }

            offset += 2 + length;
        }

        return PhotoIntakeResult.Reject("JPEG повреждён: не найден размер изображения.");
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

    private static PhotoIntakeResult? TryReadWebP(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith("RIFF"u8) || bytes.Length < 30 || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return null;
        }

        ReadOnlySpan<byte> chunk = bytes.Slice(12, 4);

        if (chunk.SequenceEqual("VP8X"u8))
        {
            // The extended form carries the canvas size directly, and is also the only form that
            // can be animated — an animation is not a photograph to build a character from.
            if ((bytes[20] & 0x02) != 0)
            {
                return PhotoIntakeResult.Reject("Анимированный WebP не подходит — нужна фотография.");
            }

            return new PhotoIntakeResult(
                true, PhotoFormat.WebP, ReadLittleEndian24(bytes, 24) + 1, ReadLittleEndian24(bytes, 27) + 1, null);
        }

        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (bytes.Length < 30 || bytes[23] != 0x9D || bytes[24] != 0x01 || bytes[25] != 0x2A)
            {
                return PhotoIntakeResult.Reject("WebP повреждён: нет стартового кода кадра.");
            }

            // Both sides are 14 bits; the top two bits are a scaling hint, not part of the size.
            return new PhotoIntakeResult(
                true,
                PhotoFormat.WebP,
                ((bytes[27] << 8) | bytes[26]) & 0x3FFF,
                ((bytes[29] << 8) | bytes[28]) & 0x3FFF,
                null);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (bytes.Length < 25 || bytes[20] != 0x2F)
            {
                return PhotoIntakeResult.Reject("WebP повреждён: нет сигнатуры lossless-кадра.");
            }

            // Two 14-bit fields packed across four little-endian bytes, each stored as size − 1.
            uint packed = (uint)(bytes[21] | (bytes[22] << 8) | (bytes[23] << 16) | (bytes[24] << 24));
            return new PhotoIntakeResult(
                true,
                PhotoFormat.WebP,
                (int)(packed & 0x3FFF) + 1,
                (int)((packed >> 14) & 0x3FFF) + 1,
                null);
        }

        return PhotoIntakeResult.Reject("WebP повреждён: неизвестный тип кадра.");
    }

    private static int ReadBigEndian32(ReadOnlySpan<byte> bytes, int offset)
    {
        long value = ((long)bytes[offset] << 24) | ((long)bytes[offset + 1] << 16) |
            ((long)bytes[offset + 2] << 8) | bytes[offset + 3];

        // A PNG may legally declare a size up to 2^31-1, which does not fit the int the rest of
        // this works in; the limit check downstream rejects it either way, so clamping here just
        // keeps it from wrapping into a small, innocent-looking number first.
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static int ReadLittleEndian24(ReadOnlySpan<byte> bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
}
