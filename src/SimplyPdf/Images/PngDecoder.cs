using System.Buffers.Binary;
using System.IO.Compression;

namespace SimplyPdf.Images;

/// <summary>
/// Minimal PNG decoder: every colour type and bit depth, the five scanline filters and Adam7
/// interlacing. Output is always 8-bit grey or RGB samples plus an optional 8-bit alpha plane,
/// which is what a PDF image XObject (+ /SMask) needs. CRCs are not verified and ancillary
/// chunks (gamma, colour profiles) are ignored.
/// </summary>
internal static class PngDecoder
{
    public sealed record DecodedPng(int Width, int Height, int Channels, byte[] Color, byte[]? Alpha);

    // (startX, startY, stepX, stepY) of the seven Adam7 passes.
    private static readonly (int X0, int Y0, int Dx, int Dy)[] Adam7 =
    [
        (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2),
    ];

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    public static DecodedPng Decode(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data))
        {
            throw new InvalidDataException("Not a PNG file (bad signature).");
        }

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        byte[]? trns = null;
        var sawHeader = false;
        using var idat = new MemoryStream();

        var pos = 8;
        while (true)
        {
            if (pos + 12 > data.Length)
            {
                throw new InvalidDataException("Truncated PNG: missing IEND chunk.");
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
            if (length < 0 || pos + 12 + length > data.Length)
            {
                throw new InvalidDataException("Truncated PNG: chunk length exceeds file size.");
            }

            var type = data.Slice(pos + 4, 4);
            var chunk = data.Slice(pos + 8, length);
            pos += 12 + length; // 4 length + 4 type + data + 4 CRC

            if (type.SequenceEqual("IHDR"u8))
            {
                if (length != 13)
                {
                    throw new InvalidDataException("Malformed PNG: IHDR must be 13 bytes.");
                }

                width = BinaryPrimitives.ReadInt32BigEndian(chunk);
                height = BinaryPrimitives.ReadInt32BigEndian(chunk[4..]);
                bitDepth = chunk[8];
                colorType = chunk[9];
                var compression = chunk[10];
                var filter = chunk[11];
                interlace = chunk[12];
                sawHeader = true;

                if (width <= 0 || height <= 0)
                {
                    throw new InvalidDataException("Malformed PNG: zero or negative dimensions.");
                }

                if (compression != 0 || filter != 0 || interlace > 1)
                {
                    throw new NotSupportedException("Unsupported PNG compression, filter or interlace method.");
                }

                var validDepth = colorType switch
                {
                    0 => bitDepth is 1 or 2 or 4 or 8 or 16,
                    2 or 4 or 6 => bitDepth is 8 or 16,
                    3 => bitDepth is 1 or 2 or 4 or 8,
                    _ => false,
                };
                if (!validDepth)
                {
                    throw new InvalidDataException($"Malformed PNG: colour type {colorType} with bit depth {bitDepth}.");
                }
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = chunk.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                trns = chunk.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Write(chunk);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (!sawHeader)
        {
            throw new InvalidDataException("Malformed PNG: no IHDR chunk.");
        }

        if (colorType == 3 && palette is null)
        {
            throw new InvalidDataException("Malformed PNG: indexed image without a PLTE chunk.");
        }

        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var pixelCount = checked((long)width * height);
        if (pixelCount * channels > Array.MaxLength)
        {
            throw new NotSupportedException("PNG image is too large to decode.");
        }

        var raw = Inflate(idat, width, height, channels, bitDepth, interlace);

        // Colour-key transparency (tRNS on grey/RGB) needs the raw sample values, so it is
        // resolved while samples are extracted.
        int[]? transparentKey = null;
        if (trns is not null && colorType == 0 && trns.Length >= 2)
        {
            transparentKey = [BinaryPrimitives.ReadUInt16BigEndian(trns)];
        }
        else if (trns is not null && colorType == 2 && trns.Length >= 6)
        {
            transparentKey =
            [
                BinaryPrimitives.ReadUInt16BigEndian(trns),
                BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(2)),
                BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(4)),
            ];
        }

        var samples = new byte[pixelCount * channels];
        var keyAlpha = transparentKey is null ? null : new byte[pixelCount];
        var scaleSubByte = colorType != 3; // palette indices must not be rescaled

        var rawPos = 0;
        if (interlace == 0)
        {
            ExtractPass(raw, ref rawPos, width, height, 0, 0, 1, 1, width, channels, bitDepth, scaleSubByte, samples, transparentKey, keyAlpha);
        }
        else
        {
            foreach (var (x0, y0, dx, dy) in Adam7)
            {
                var passWidth = width > x0 ? (width - x0 + dx - 1) / dx : 0;
                var passHeight = height > y0 ? (height - y0 + dy - 1) / dy : 0;
                if (passWidth == 0 || passHeight == 0)
                {
                    continue;
                }

                ExtractPass(raw, ref rawPos, passWidth, passHeight, x0, y0, dx, dy, width, channels, bitDepth, scaleSubByte, samples, transparentKey, keyAlpha);
            }
        }

        return ToColorAndAlpha(width, height, colorType, samples, palette, trns, keyAlpha);
    }

    private static byte[] Inflate(MemoryStream idat, int width, int height, int channels, int bitDepth, int interlace)
    {
        long expected = 0;
        if (interlace == 0)
        {
            expected = (long)height * (1 + Stride(width, channels, bitDepth));
        }
        else
        {
            foreach (var (x0, y0, dx, dy) in Adam7)
            {
                var passWidth = width > x0 ? (width - x0 + dx - 1) / dx : 0;
                var passHeight = height > y0 ? (height - y0 + dy - 1) / dy : 0;
                if (passWidth > 0 && passHeight > 0)
                {
                    expected += (long)passHeight * (1 + Stride(passWidth, channels, bitDepth));
                }
            }
        }

        if (expected > Array.MaxLength)
        {
            throw new NotSupportedException("PNG image is too large to decode.");
        }

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true);
        var raw = new byte[expected];
        var read = 0;
        while (read < raw.Length)
        {
            var n = zlib.Read(raw, read, raw.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        if (read < raw.Length)
        {
            throw new InvalidDataException($"Truncated PNG image data: expected {expected} bytes, got {read}.");
        }

        return raw;
    }

    private static int Stride(int width, int channels, int bitDepth) => (int)(((long)width * channels * bitDepth + 7) / 8);

    private static void ExtractPass(
        byte[] raw,
        ref int rawPos,
        int passWidth,
        int passHeight,
        int startX,
        int startY,
        int stepX,
        int stepY,
        int imageWidth,
        int channels,
        int bitDepth,
        bool scaleSubByte,
        byte[] samples,
        int[]? transparentKey,
        byte[]? keyAlpha)
    {
        var bitsPerPixel = channels * bitDepth;
        var stride = Stride(passWidth, channels, bitDepth);
        var bpp = Math.Max(1, bitsPerPixel / 8);
        var previous = new byte[stride];
        var current = new byte[stride];
        var maxValue = (1 << bitDepth) - 1;

        for (var row = 0; row < passHeight; row++)
        {
            var filter = raw[rawPos++];
            Array.Copy(raw, rawPos, current, 0, stride);
            rawPos += stride;
            Unfilter(filter, current, previous, bpp);

            var y = startY + row * stepY;
            for (var px = 0; px < passWidth; px++)
            {
                var x = startX + px * stepX;
                var pixelIndex = (long)y * imageWidth + x;
                var matchesKey = transparentKey is not null;

                for (var c = 0; c < channels; c++)
                {
                    var value = ReadSample(current, px * channels + c, bitDepth);
                    if (transparentKey is not null && value != transparentKey[c])
                    {
                        matchesKey = false;
                    }

                    samples[pixelIndex * channels + c] = bitDepth switch
                    {
                        8 => (byte)value,
                        16 => (byte)(value >> 8),
                        _ => scaleSubByte ? (byte)(value * 255 / maxValue) : (byte)value,
                    };
                }

                if (keyAlpha is not null)
                {
                    keyAlpha[pixelIndex] = matchesKey ? (byte)0 : (byte)255;
                }
            }

            (previous, current) = (current, previous);
        }
    }

    private static int ReadSample(byte[] row, int sampleIndex, int bitDepth)
    {
        switch (bitDepth)
        {
            case 8:
                return row[sampleIndex];
            case 16:
                return (row[sampleIndex * 2] << 8) | row[sampleIndex * 2 + 1];
            default:
                var bitOffset = sampleIndex * bitDepth;
                var shift = 8 - bitDepth - (bitOffset & 7);
                return (row[bitOffset >> 3] >> shift) & ((1 << bitDepth) - 1);
        }
    }

    private static void Unfilter(byte filter, byte[] current, byte[] previous, int bpp)
    {
        var length = current.Length;
        switch (filter)
        {
            case 0:
                return;
            case 1: // Sub
                for (var i = bpp; i < length; i++)
                {
                    current[i] = (byte)(current[i] + current[i - bpp]);
                }

                return;
            case 2: // Up
                for (var i = 0; i < length; i++)
                {
                    current[i] = (byte)(current[i] + previous[i]);
                }

                return;
            case 3: // Average
                for (var i = 0; i < length; i++)
                {
                    var left = i >= bpp ? current[i - bpp] : 0;
                    current[i] = (byte)(current[i] + ((left + previous[i]) >> 1));
                }

                return;
            case 4: // Paeth
                for (var i = 0; i < length; i++)
                {
                    var a = i >= bpp ? current[i - bpp] : 0;
                    var b = previous[i];
                    var c = i >= bpp ? previous[i - bpp] : 0;
                    current[i] = (byte)(current[i] + Paeth(a, b, c));
                }

                return;
            default:
                throw new InvalidDataException($"Malformed PNG: unknown scanline filter {filter}.");
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    private static DecodedPng ToColorAndAlpha(int width, int height, int colorType, byte[] samples, byte[]? palette, byte[]? trns, byte[]? keyAlpha)
    {
        var pixelCount = width * height;
        byte[] color;
        byte[]? alpha;
        int channels;

        switch (colorType)
        {
            case 0: // grey
                color = samples;
                alpha = keyAlpha;
                channels = 1;
                break;
            case 2: // RGB
                color = samples;
                alpha = keyAlpha;
                channels = 3;
                break;
            case 3: // indexed
                color = new byte[pixelCount * 3];
                alpha = trns is null ? null : new byte[pixelCount];
                for (var i = 0; i < pixelCount; i++)
                {
                    var index = samples[i];
                    if (index * 3 + 2 >= palette!.Length)
                    {
                        throw new InvalidDataException("Malformed PNG: palette index out of range.");
                    }

                    color[i * 3] = palette[index * 3];
                    color[i * 3 + 1] = palette[index * 3 + 1];
                    color[i * 3 + 2] = palette[index * 3 + 2];
                    if (alpha is not null)
                    {
                        alpha[i] = index < trns!.Length ? trns[index] : (byte)255;
                    }
                }

                channels = 3;
                break;
            case 4: // grey + alpha
                color = new byte[pixelCount];
                alpha = new byte[pixelCount];
                for (var i = 0; i < pixelCount; i++)
                {
                    color[i] = samples[i * 2];
                    alpha[i] = samples[i * 2 + 1];
                }

                channels = 1;
                break;
            default: // 6: RGB + alpha
                color = new byte[pixelCount * 3];
                alpha = new byte[pixelCount];
                for (var i = 0; i < pixelCount; i++)
                {
                    color[i * 3] = samples[i * 4];
                    color[i * 3 + 1] = samples[i * 4 + 1];
                    color[i * 3 + 2] = samples[i * 4 + 2];
                    alpha[i] = samples[i * 4 + 3];
                }

                channels = 3;
                break;
        }

        // A fully opaque alpha plane would only add an SMask for nothing.
        if (alpha is not null && Array.TrueForAll(alpha, a => a == 255))
        {
            alpha = null;
        }

        return new DecodedPng(width, height, channels, color, alpha);
    }
}
