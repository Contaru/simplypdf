using System.Buffers.Binary;
using System.Text;

namespace SimplyPdf.Internal;

/// <summary>
/// A small sRGB ICC profile built in code, for the output intent PDF/A asks for. It is a version 2
/// display profile of the matrix/curve kind: the sRGB primaries adapted to D50 and the sRGB
/// transfer curve sampled at 1024 points, shared by the three channels. Building it here keeps
/// the library free of binary resources and of anyone else's copyright.
/// </summary>
internal static class SrgbProfile
{
    /// <summary>The name the output intent declares for this colour space.</summary>
    public const string Name = "sRGB IEC61966-2.1";

    private static byte[]? _bytes;

    /// <summary>The profile; the same bytes on every call.</summary>
    public static byte[] Bytes => _bytes ??= Build();

    private static byte[] Build()
    {
        // Tag data, each padded to four bytes. The three curve tags point at the same block.
        var description = TextDescription(Name);
        var copyright = Text("No copyright, use freely");
        var whitePoint = Xyz(0.95045, 1.0, 1.08905); // D65, the white of the display
        var red = Xyz(0.43607, 0.22249, 0.01392);
        var green = Xyz(0.38515, 0.71687, 0.09708);
        var blue = Xyz(0.14307, 0.06061, 0.71410);
        var curve = Curve();

        (string Signature, byte[] Data)[] blocks =
        [
            ("desc", description), ("cprt", copyright), ("wtpt", whitePoint),
            ("rXYZ", red), ("gXYZ", green), ("bXYZ", blue), ("rTRC", curve),
        ];
        (string Signature, int Block)[] tags =
        [
            ("desc", 0), ("cprt", 1), ("wtpt", 2), ("rXYZ", 3), ("gXYZ", 4), ("bXYZ", 5),
            ("rTRC", 6), ("gTRC", 6), ("bTRC", 6),
        ];

        const int headerLength = 128;
        var tableLength = 4 + tags.Length * 12;
        var offsets = new int[blocks.Length];
        var length = headerLength + tableLength;
        for (var i = 0; i < blocks.Length; i++)
        {
            offsets[i] = length;
            length += Align4(blocks[i].Data.Length);
        }

        var profile = new byte[length];
        var span = profile.AsSpan();

        // Header.
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 0x02100000); // version 2.1
        Ascii("mntr", span[12..]);  // display device
        Ascii("RGB ", span[16..]);  // colour space
        Ascii("XYZ ", span[20..]);  // connection space
        BinaryPrimitives.WriteUInt16BigEndian(span[24..], 2026); // a fixed date keeps the output reproducible
        BinaryPrimitives.WriteUInt16BigEndian(span[26..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(span[28..], 1);
        Ascii("acsp", span[36..]);
        Fixed(0.9642, span[68..]);  // D50, the illuminant of the connection space
        Fixed(1.0, span[72..]);
        Fixed(0.8249, span[76..]);

        // Tag table.
        BinaryPrimitives.WriteUInt32BigEndian(span[headerLength..], (uint)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            var entry = span[(headerLength + 4 + i * 12)..];
            Ascii(tags[i].Signature, entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offsets[tags[i].Block]);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)blocks[tags[i].Block].Data.Length);
        }

        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i].Data.CopyTo(span[offsets[i]..]);
        }

        return profile;
    }

    /// <summary>A version 2 description: ASCII text, then empty Unicode and ScriptCode forms.</summary>
    private static byte[] TextDescription(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text);
        var data = new byte[12 + ascii.Length + 1 + 4 + 4 + 2 + 1 + 67];
        Ascii("desc", data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)ascii.Length + 1);
        ascii.CopyTo(data, 12);
        return data;
    }

    private static byte[] Text(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text);
        var data = new byte[8 + ascii.Length + 1];
        Ascii("text", data);
        ascii.CopyTo(data, 8);
        return data;
    }

    private static byte[] Xyz(double x, double y, double z)
    {
        var data = new byte[20];
        Ascii("XYZ ", data);
        Fixed(x, data.AsSpan(8));
        Fixed(y, data.AsSpan(12));
        Fixed(z, data.AsSpan(16));
        return data;
    }

    /// <summary>The sRGB transfer curve, from encoded value to linear light, as 16-bit samples.</summary>
    private static byte[] Curve()
    {
        const int samples = 1024;
        var data = new byte[12 + samples * 2];
        Ascii("curv", data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), samples);
        for (var i = 0; i < samples; i++)
        {
            var encoded = i / (double)(samples - 1);
            var linear = encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12 + i * 2), (ushort)Math.Round(linear * 65535));
        }

        return data;
    }

    private static void Ascii(string text, Span<byte> destination) => Encoding.ASCII.GetBytes(text, destination);

    /// <summary>A signed 15.16 fixed-point number, the way ICC stores XYZ values.</summary>
    private static void Fixed(double value, Span<byte> destination) =>
        BinaryPrimitives.WriteInt32BigEndian(destination, (int)Math.Round(value * 65536));

    private static int Align4(int length) => (length + 3) & ~3;
}
