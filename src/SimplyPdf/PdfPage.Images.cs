namespace SimplyPdf;

public sealed partial class PdfPage
{
    /// <summary>
    /// Draws <paramref name="image"/> with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).
    /// Give <paramref name="width"/> and/or <paramref name="height"/> to resize (the aspect ratio is
    /// kept when only one is given), or <paramref name="scale"/> to multiply the pixel size. With no
    /// size, one pixel becomes one point.
    /// </summary>
    public PdfPage Image(PdfImage image, double x, double y, double? width = null, double? height = null, double? scale = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (w, h) = ResolveSize(image, width, height, scale);

        var name = Document.ImageResourceName(image);
        _usedImages.Add(name);

        // The unit square maps to the target box; the negative height flips the image back upright.
        _content.Append("q\n").Append(N(w)).Append(" 0 0 ").Append(N(-h)).Append(' ')
            .Append(N(x)).Append(' ').Append(N(y + h)).Append(" cm\n/").Append(name).Append(" Do\nQ\n");
        return this;
    }

    /// <summary>Draws <paramref name="image"/> at the text cursor and moves the cursor below it.</summary>
    public PdfPage Image(PdfImage image, double? width = null, double? height = null, double? scale = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (_, h) = ResolveSize(image, width, height, scale);
        Image(image, X, Y, width, height, scale);
        Y += h;
        return this;
    }

    private static (double Width, double Height) ResolveSize(PdfImage image, double? width, double? height, double? scale)
    {
        if (scale is { } s)
        {
            return (image.Width * s, image.Height * s);
        }

        return (width, height) switch
        {
            ({ } w, { } h) => (w, h),
            ({ } w, null) => (w, w * image.Height / image.Width),
            (null, { } h) => (h * image.Width / image.Height, h),
            _ => (image.Width, image.Height),
        };
    }
}
