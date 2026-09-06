using SimplyPdf.Images;

namespace SimplyPdf.Tests;

public class ImageTests
{
    // A deterministic 9x7 RGBA image: enough pixels to exercise every Adam7 pass.
    private const int W = 9;
    private const int H = 7;

    private static int[] Rgba() =>
        Enumerable.Range(0, W * H).SelectMany(i => new[] { (i * 7) % 256, (i * 13) % 256, (i * 29) % 256, i % 3 == 0 ? 0 : 255 - i }).ToArray();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Rgba_png_round_trips_with_every_filter(int filter)
    {
        var samples = Rgba();
        var png = TestPng.Encode(W, H, colorType: 6, bitDepth: 8, samples, _ => filter);

        var decoded = PngDecoder.Decode(png);

        AssertRgba(samples, decoded);
    }

    [Fact]
    public void Rgba_png_round_trips_with_mixed_filters_and_adam7_interlacing()
    {
        var samples = Rgba();
        var png = TestPng.Encode(W, H, colorType: 6, bitDepth: 8, samples, row => row % 5, interlace: true);

        var decoded = PngDecoder.Decode(png);

        AssertRgba(samples, decoded);
    }

    [Fact]
    public void Sixteen_bit_rgb_keeps_the_high_byte()
    {
        var samples = Enumerable.Range(0, 2 * 2 * 3).Select(i => i * 4000 % 65536).ToArray();
        var png = TestPng.Encode(2, 2, colorType: 2, bitDepth: 16, samples, row => 4);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(3, decoded.Channels);
        Assert.Null(decoded.Alpha);
        Assert.Equal(samples.Select(s => (byte)(s >> 8)), decoded.Color);
    }

    [Fact]
    public void One_bit_grey_is_scaled_to_full_range()
    {
        int[] samples = [1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 1]; // 12x1
        var png = TestPng.Encode(12, 1, colorType: 0, bitDepth: 1, samples);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(1, decoded.Channels);
        Assert.Equal(samples.Select(s => (byte)(s * 255)), decoded.Color);
    }

    [Fact]
    public void Palette_png_expands_to_rgb_and_uses_trns_as_alpha()
    {
        byte[] palette = [255, 0, 0, 0, 255, 0, 0, 0, 255, 9, 9, 9];
        byte[] trns = [255, 128, 0]; // 4th entry omitted -> opaque
        int[] indices = [0, 1, 2, 3, 3, 2, 1, 0]; // 4x2, 2-bit
        var png = TestPng.Encode(4, 2, colorType: 3, bitDepth: 2, indices, palette: palette, trns: trns);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(3, decoded.Channels);
        Assert.Equal(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 9, 9, 9, 9, 9, 9, 0, 0, 255, 0, 255, 0, 255, 0, 0 }, decoded.Color);
        Assert.Equal(new byte[] { 255, 128, 0, 255, 255, 0, 128, 255 }, decoded.Alpha);
    }

    [Fact]
    public void Colour_key_transparency_on_rgb_becomes_alpha()
    {
        int[] samples = [10, 20, 30, 1, 2, 3, 10, 20, 30, 0, 0, 0]; // 2x2
        byte[] trns = [0, 10, 0, 20, 0, 30];
        var png = TestPng.Encode(2, 2, colorType: 2, bitDepth: 8, samples, trns: trns);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, decoded.Alpha);
    }

    [Fact]
    public void Grey_alpha_png_is_split_into_planes()
    {
        int[] samples = [50, 255, 100, 0, 150, 128, 200, 255];
        var png = TestPng.Encode(2, 2, colorType: 4, bitDepth: 8, samples, row => 1);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(new byte[] { 50, 100, 150, 200 }, decoded.Color);
        Assert.Equal(new byte[] { 255, 0, 128, 255 }, decoded.Alpha);
    }

    [Fact]
    public void Fully_opaque_alpha_is_dropped()
    {
        var samples = Enumerable.Range(0, 3 * 3).SelectMany(i => new[] { i, i, i, 255 }).ToArray();
        var png = TestPng.Encode(3, 3, colorType: 6, bitDepth: 8, samples);

        var decoded = PngDecoder.Decode(png);

        Assert.Null(decoded.Alpha);
    }

    [Fact]
    public void Truncated_png_is_rejected()
    {
        var png = TestPng.Encode(4, 4, colorType: 2, bitDepth: 8, new int[4 * 4 * 3]);

        Assert.ThrowsAny<Exception>(() => PngDecoder.Decode(png[..(png.Length - 20)]));
    }

    [Fact]
    public void Jpeg_frame_header_is_parsed()
    {
        // SOI, APP0 (skipped), SOF0 with 8-bit 640x480x3, SOS.
        byte[] jpeg =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xFF, 0xDA,
        ];

        var frame = JpegInfo.Parse(jpeg);

        Assert.Equal(640, frame.Width);
        Assert.Equal(480, frame.Height);
        Assert.Equal(3, frame.Components);
        Assert.Equal(8, frame.Precision);
    }

    [Fact]
    public void Png_image_is_embedded_as_rgb_xobject_with_smask()
    {
        var png = TestPng.Encode(W, H, colorType: 6, bitDepth: 8, Rgba());
        var image = PdfImage.FromBytes(png);
        var doc = new PdfDocument();
        doc.AddPage().Image(image, 10, 20, width: 90);
        doc.AddPage().Image(image, 0, 0); // second use, same XObject

        var probe = new PdfProbe(doc.ToArray());
        var objects = probe.XrefOffsets().Keys.Select(n => (Number: n, Body: probe.Object(n))).ToList();

        var xobjects = objects.Where(o => o.Body.Contains("/Subtype /Image", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, xobjects.Count); // colour + soft mask, stored once

        var colour = xobjects.Single(o => o.Body.Contains("/DeviceRGB", StringComparison.Ordinal));
        Assert.Contains($"/Width {W}", colour.Body, StringComparison.Ordinal);
        Assert.Contains($"/Height {H}", colour.Body, StringComparison.Ordinal);
        Assert.Contains("/SMask", colour.Body, StringComparison.Ordinal);
        Assert.Equal(image.Data, probe.StreamData(colour.Number));

        var mask = xobjects.Single(o => o.Body.Contains("/DeviceGray", StringComparison.Ordinal));
        Assert.Equal(image.Alpha, probe.StreamData(mask.Number));

        // 90pt wide keeps the aspect ratio: height = 90 * 7 / 9 = 70; placed at (10, 20) -> cm y = 20 + 70
        Assert.Contains("q\n90 0 0 -70 10 90 cm\n/I1 Do\nQ", probe.FirstPageContent(), StringComparison.Ordinal);
    }

    [Fact]
    public void Jpeg_bytes_are_embedded_verbatim_with_dct_decode()
    {
        byte[] jpeg =
        [
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x02, 0x00, 0x03, 0x01, 0x01, 0x11, 0x00,
            0xFF, 0xDA, 0xAA, 0xBB, 0xFF, 0xD9,
        ];
        var image = PdfImage.FromBytes(jpeg);
        var doc = new PdfDocument();
        doc.AddPage().Image(image, 0, 0, scale: 10);

        var probe = new PdfProbe(doc.ToArray());
        var number = probe.XrefOffsets().Keys.Single(n => probe.Object(n).Contains("/DCTDecode", StringComparison.Ordinal));

        Assert.Equal(1, image.Components);
        Assert.Contains("/DeviceGray", probe.Object(number), StringComparison.Ordinal);
        Assert.Equal(jpeg, probe.StreamData(number));
        Assert.Contains("q\n30 0 0 -20 0 20 cm\n/I1 Do\nQ", probe.FirstPageContent(), StringComparison.Ordinal);
    }

    [Fact]
    public void Raw_pixel_factories_validate_sizes()
    {
        Assert.Throws<ArgumentException>(() => PdfImage.FromRgb(2, 2, new byte[11]));
        Assert.Throws<ArgumentException>(() => PdfImage.FromGray(2, 2, new byte[4], alpha: new byte[3]));

        var image = PdfImage.FromGray(2, 2, [0, 255, 255, 0]);
        Assert.Equal(2, image.Width);
        Assert.Null(image.Alpha);
    }

    private static void AssertRgba(int[] samples, PngDecoder.DecodedPng decoded)
    {
        Assert.Equal(W, decoded.Width);
        Assert.Equal(H, decoded.Height);
        Assert.Equal(3, decoded.Channels);
        Assert.NotNull(decoded.Alpha);

        var expectedColor = new List<byte>();
        var expectedAlpha = new List<byte>();
        for (var i = 0; i < W * H; i++)
        {
            expectedColor.Add((byte)samples[i * 4]);
            expectedColor.Add((byte)samples[i * 4 + 1]);
            expectedColor.Add((byte)samples[i * 4 + 2]);
            expectedAlpha.Add((byte)samples[i * 4 + 3]);
        }

        Assert.Equal(expectedColor, decoded.Color);
        Assert.Equal(expectedAlpha, decoded.Alpha);
    }
}
