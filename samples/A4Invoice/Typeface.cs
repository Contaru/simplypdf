namespace SimplyPdf.Samples.A4Invoice;

/// <summary>The two faces the layout uses.</summary>
public sealed record Typeface(PdfFont Regular, PdfFont Bold)
{
    private const string RegularFile = "LiberationSans-Regular.ttf";
    private const string BoldFile = "LiberationSans-Bold.ttf";

    /// <summary>
    /// Helvetica shares its advance widths with Liberation Sans (both are metric-compatible with
    /// Arial), so the layout holds without any font file; only the letter shapes differ.
    /// </summary>
    public static Typeface Helvetica { get; } = new(PdfFont.Helvetica, PdfFont.HelveticaBold);

    /// <summary>
    /// Liberation Sans (SIL OFL 1.1), the font of the original, from the two .ttf files in
    /// <paramref name="directory"/>; <see langword="null"/> when they are not there.
    /// </summary>
    public static Typeface? LiberationSans(string directory)
    {
        var regular = Path.Combine(directory, RegularFile);
        var bold = Path.Combine(directory, BoldFile);
        if (!File.Exists(regular) || !File.Exists(bold))
        {
            return null;
        }

        return new Typeface(PdfFont.FromFile(regular), PdfFont.FromFile(bold));
    }
}
