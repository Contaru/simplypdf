using System.Buffers.Binary;

namespace SimplyPdf.Images;

/// <summary>
/// Reads the frame header of a JPEG file. PDF viewers decode JPEG natively (/DCTDecode), so the
/// file is embedded as-is; we only need its dimensions and component count.
/// </summary>
internal static class JpegInfo
{
    public readonly record struct Frame(int Width, int Height, int Components, int Precision);

    public static bool IsJpeg(ReadOnlySpan<byte> data) =>
        data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    public static Frame Parse(ReadOnlySpan<byte> data)
    {
        if (!IsJpeg(data))
        {
            throw new InvalidDataException("Not a JPEG file (missing SOI marker).");
        }

        var pos = 2;
        while (pos + 4 <= data.Length)
        {
            if (data[pos] != 0xFF)
            {
                throw new InvalidDataException($"Malformed JPEG: expected a marker at offset {pos}.");
            }

            var marker = data[pos + 1];
            if (marker == 0xFF)
            {
                pos++; // fill byte
                continue;
            }

            pos += 2;

            // Standalone markers carry no length.
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                continue;
            }

            if (marker == 0xD9 || marker == 0xDA)
            {
                break; // EOI or start of scan without a frame header
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[pos..]);
            if (length < 2 || pos + length > data.Length)
            {
                throw new InvalidDataException("Malformed JPEG: segment length out of range.");
            }

            // SOF0..SOF15, except the DHT/JPG/DAC markers that share the 0xC0 block.
            var isFrameHeader = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isFrameHeader)
            {
                if (length < 8)
                {
                    throw new InvalidDataException("Malformed JPEG: frame header too short.");
                }

                var precision = data[pos + 2];
                var height = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 3)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 5)..]);
                var components = data[pos + 7];

                if (width == 0 || height == 0)
                {
                    throw new NotSupportedException("JPEG files whose height is defined by a DNL marker are not supported.");
                }

                if (components is not (1 or 3 or 4))
                {
                    throw new NotSupportedException($"Unsupported JPEG component count: {components}.");
                }

                return new Frame(width, height, components, precision);
            }

            pos += length;
        }

        throw new InvalidDataException("Malformed JPEG: no frame header (SOF) found.");
    }
}
