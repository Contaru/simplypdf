namespace SimplyPdf.Samples.A4Invoice;

/// <summary>
/// The two faces the layout uses, and how far below the top of a line their baseline sits (in em).
/// CSS places text by its baseline while SimplyPdf places the top of the line, so the renderer
/// needs the ascent to convert one into the other.
/// </summary>
public sealed record Typeface(PdfFont Regular, PdfFont Bold, double Ascent)
{
    private const string RegularFile = "LiberationSans-Regular.ttf";
    private const string BoldFile = "LiberationSans-Bold.ttf";

    /// <summary>
    /// Helvetica shares its advance widths with Liberation Sans (both are metric-compatible with
    /// Arial), so the layout holds without any font file; only the letter shapes differ.
    /// </summary>
    public static Typeface Helvetica { get; } = new(PdfFont.Helvetica, PdfFont.HelveticaBold, 718 / 1000.0);

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

        // hhea ascender 1854 on a 2048-unit em.
        return new Typeface(PdfFont.FromFile(regular), PdfFont.FromFile(bold), 1854 / 2048.0);
    }
}
