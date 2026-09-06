using SimplyPdf.Images;

namespace SimplyPdf;

/// <summary>
/// A raster image ready to be placed on a page. Load one with <see cref="FromFile"/>,
/// <see cref="FromBytes"/> or <see cref="FromStream"/> (PNG and JPEG are detected from their
/// signature), or build one from raw pixels with <see cref="FromRgb"/> / <see cref="FromGray"/>.
/// The same instance can be drawn many times and on many pages; it is stored once in the file.
/// </summary>
public sealed class PdfImage
{
    private PdfImage(int width, int height, int components, byte[] data, byte[]? alpha, bool isJpeg)
    {
        Width = width;
        Height = height;
        Components = components;
        Data = data;
        Alpha = alpha;
        IsJpeg = isJpeg;
    }

    /// <summary>Pixel width.</summary>
    public int Width { get; }

    /// <summary>Pixel height.</summary>
    public int Height { get; }

    /// <summary>1 = grey, 3 = RGB, 4 = CMYK (JPEG only).</summary>
    internal int Components { get; }

    /// <summary>The JPEG file itself, or raw 8-bit interleaved samples for decoded images.</summary>
    internal byte[] Data { get; }

    /// <summary>8-bit alpha plane, or <see langword="null"/> when the image is opaque.</summary>
    internal byte[]? Alpha { get; }

    /// <summary>True when <see cref="Data"/> is a JPEG stream to be embedded verbatim with /DCTDecode.</summary>
    internal bool IsJpeg { get; }

    /// <summary>Loads a PNG or JPEG file.</summary>
    public static PdfImage FromFile(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>Loads a PNG or JPEG from a stream, reading it to the end.</summary>
    public static PdfImage FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return FromBytes(buffer.ToArray());
    }

    /// <summary>Loads a PNG or JPEG from its encoded bytes.</summary>
    public static PdfImage FromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (PngDecoder.IsPng(data))
        {
            var png = PngDecoder.Decode(data);
            return new PdfImage(png.Width, png.Height, png.Channels, png.Color, png.Alpha, isJpeg: false);
        }

        if (JpegInfo.IsJpeg(data))
        {
            var info = JpegInfo.Parse(data);
            return new PdfImage(info.Width, info.Height, info.Components, data, alpha: null, isJpeg: true);
        }

        throw new NotSupportedException("Unrecognised image format: only PNG and JPEG are supported.");
    }

    /// <summary>
    /// Creates an image from raw 8-bit RGB samples (3 bytes per pixel, row-major) and an optional
    /// 8-bit alpha plane (1 byte per pixel).
    /// </summary>
    public static PdfImage FromRgb(int width, int height, byte[] rgb, byte[]? alpha = null)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        ValidateRaw(width, height, 3, rgb, alpha);
        return new PdfImage(width, height, 3, rgb, alpha, isJpeg: false);
    }

    /// <summary>
    /// Creates an image from raw 8-bit grey samples (1 byte per pixel, row-major) and an optional
    /// 8-bit alpha plane.
    /// </summary>
    public static PdfImage FromGray(int width, int height, byte[] gray, byte[]? alpha = null)
    {
        ArgumentNullException.ThrowIfNull(gray);
        ValidateRaw(width, height, 1, gray, alpha);
        return new PdfImage(width, height, 1, gray, alpha, isJpeg: false);
    }

    private static void ValidateRaw(int width, int height, int components, byte[] data, byte[]? alpha)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        var expected = (long)width * height * components;
        if (data.Length != expected)
        {
            throw new ArgumentException($"Expected {expected} sample bytes for a {width}x{height} image, got {data.Length}.", nameof(data));
        }

        if (alpha is not null && alpha.Length != (long)width * height)
        {
            throw new ArgumentException($"Expected {(long)width * height} alpha bytes, got {alpha.Length}.", nameof(alpha));
        }
    }
}
