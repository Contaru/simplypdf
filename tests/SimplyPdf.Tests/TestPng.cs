using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SimplyPdf.Tests;

/// <summary>
/// A small PNG *encoder* used only to fabricate test inputs: any colour type / bit depth, a
/// caller-chosen scanline filter per row, optional Adam7 interlacing. CRCs are written as zeros
/// because the decoder under test ignores them.
/// </summary>
internal static class TestPng
{
    private static readonly (int X0, int Y0, int Dx, int Dy)[] Adam7 =
    [
        (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2),
    ];

    /// <param name="samples">Raw sample values (0 .. 2^bitDepth-1), row-major, channels interleaved.</param>
    public static byte[] Encode(
        int width,
        int height,
        int colorType,
        int bitDepth,
        int[] samples,
        Func<int, int>? filterForRow = null,
        bool interlace = false,
        byte[]? palette = null,
        byte[]? trns = null)
    {
        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new ArgumentOutOfRangeException(nameof(colorType)) };
        filterForRow ??= _ => 0;

        using var raw = new MemoryStream();
        if (!interlace)
        {
            EncodePass(raw, width, height, 0, 0, 1, 1, width, channels, bitDepth, samples, filterForRow);
        }
        else
        {
            foreach (var (x0, y0, dx, dy) in Adam7)
            {
                var pw = width > x0 ? (width - x0 + dx - 1) / dx : 0;
                var ph = height > y0 ? (height - y0 + dy - 1) / dy : 0;
                if (pw > 0 && ph > 0)
                {
                    EncodePass(raw, pw, ph, x0, y0, dx, dy, width, channels, bitDepth, samples, filterForRow);
                }
            }
        }

        using var idat = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw.ToArray());
        }

        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = (byte)colorType;
        ihdr[12] = (byte)(interlace ? 1 : 0);
        Chunk(file, "IHDR", ihdr);

        if (palette is not null)
        {
            Chunk(file, "PLTE", palette);
        }

        if (trns is not null)
        {
            Chunk(file, "tRNS", trns);
        }

        // Split IDAT in two chunks to prove concatenation works.
        var idatBytes = idat.ToArray();
        var half = idatBytes.Length / 2;
        Chunk(file, "IDAT", idatBytes[..half]);
        Chunk(file, "IDAT", idatBytes[half..]);
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void EncodePass(
        Stream output,
        int passWidth,
        int passHeight,
        int x0,
        int y0,
        int dx,
        int dy,
        int imageWidth,
        int channels,
        int bitDepth,
        int[] samples,
        Func<int, int> filterForRow)
    {
        var stride = (passWidth * channels * bitDepth + 7) / 8;
        var bpp = Math.Max(1, channels * bitDepth / 8);
        var previous = new byte[stride];

        for (var row = 0; row < passHeight; row++)
        {
            var current = new byte[stride];
            var y = y0 + row * dy;
            for (var px = 0; px < passWidth; px++)
            {
                var x = x0 + px * dx;
                for (var c = 0; c < channels; c++)
                {
                    WriteSample(current, px * channels + c, bitDepth, samples[(y * imageWidth + x) * channels + c]);
                }
            }

            var filter = filterForRow(y);
            var filtered = new byte[stride];
            for (var i = 0; i < stride; i++)
            {
                int a = i >= bpp ? current[i - bpp] : 0;
                int b = previous[i];
                int c = i >= bpp ? previous[i - bpp] : 0;
                var predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => throw new ArgumentOutOfRangeException(nameof(filterForRow)),
                };
                filtered[i] = (byte)(current[i] - predictor);
            }

            output.WriteByte((byte)filter);
            output.Write(filtered);
            previous = current;
        }
    }

    private static void WriteSample(byte[] row, int sampleIndex, int bitDepth, int value)
    {
        switch (bitDepth)
        {
            case 8:
                row[sampleIndex] = (byte)value;
                break;
            case 16:
                row[sampleIndex * 2] = (byte)(value >> 8);
                row[sampleIndex * 2 + 1] = (byte)value;
                break;
            default:
                var bitOffset = sampleIndex * bitDepth;
                var shift = 8 - bitDepth - (bitOffset & 7);
                row[bitOffset >> 3] |= (byte)(value << shift);
                break;
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

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(new byte[4]); // CRC placeholder
    }
}
