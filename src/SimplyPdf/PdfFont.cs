using SimplyPdf.Fonts;
using SimplyPdf.Fonts.TrueType;
using SimplyPdf.Internal;

namespace SimplyPdf;

/// <summary>
/// A font usable with <see cref="PdfPage.Font(PdfFont, double?)"/>: either one of the standard 14
/// (never embedded, see <see cref="Standard"/> and the static properties) or a TrueType file
/// loaded with <see cref="FromFile"/> / <see cref="FromBytes"/>, which is embedded subsetted.
/// Text is always WinAnsi-encoded; characters the font lacks render as its .notdef glyph.
/// Instances are immutable and safe to share across documents — reuse them, since every
/// distinct instance becomes a separate font object in the file.
/// </summary>
public abstract class PdfFont
{
    private static readonly PdfFont[] Standards =
        Enum.GetValues<StandardFont>().Select(f => (PdfFont)new StandardPdfFont(f)).ToArray();

    private protected PdfFont()
    {
    }

    /// <summary>PostScript name, e.g. <c>Helvetica-Bold</c> or <c>ShareTechMono-Regular</c>.</summary>
    public abstract string Name { get; }

    /// <summary>True when the font program is written into the PDF.</summary>
    public abstract bool IsEmbedded { get; }

    /// <summary>Helvetica (regular).</summary>
    public static PdfFont Helvetica => Standard(StandardFont.Helvetica);

    /// <summary>Helvetica-Bold.</summary>
    public static PdfFont HelveticaBold => Standard(StandardFont.HelveticaBold);

    /// <summary>Helvetica-Oblique.</summary>
    public static PdfFont HelveticaOblique => Standard(StandardFont.HelveticaOblique);

    /// <summary>Helvetica-BoldOblique.</summary>
    public static PdfFont HelveticaBoldOblique => Standard(StandardFont.HelveticaBoldOblique);

    /// <summary>Times-Roman.</summary>
    public static PdfFont TimesRoman => Standard(StandardFont.TimesRoman);

    /// <summary>Times-Bold.</summary>
    public static PdfFont TimesBold => Standard(StandardFont.TimesBold);

    /// <summary>Times-Italic.</summary>
    public static PdfFont TimesItalic => Standard(StandardFont.TimesItalic);

    /// <summary>Times-BoldItalic.</summary>
    public static PdfFont TimesBoldItalic => Standard(StandardFont.TimesBoldItalic);

    /// <summary>Courier (monospaced).</summary>
    public static PdfFont Courier => Standard(StandardFont.Courier);

    /// <summary>Courier-Bold.</summary>
    public static PdfFont CourierBold => Standard(StandardFont.CourierBold);

    /// <summary>Courier-Oblique.</summary>
    public static PdfFont CourierOblique => Standard(StandardFont.CourierOblique);

    /// <summary>Courier-BoldOblique.</summary>
    public static PdfFont CourierBoldOblique => Standard(StandardFont.CourierBoldOblique);

    /// <summary>The shared instance of a standard font.</summary>
    public static PdfFont Standard(StandardFont font)
    {
        var index = (int)font;
        if (index < 0 || index >= Standards.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(font), font, "Unknown standard font.");
        }

        return Standards[index];
    }

    /// <summary>
    /// Loads a TrueType (.ttf) font for embedding. The font's OS/2 embedding permissions are
    /// honoured: fonts flagged "restricted licence" or "bitmap only" are rejected, and fonts
    /// flagged "no subsetting" are embedded whole.
    /// </summary>
    public static PdfFont FromFile(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>Loads a TrueType font from a stream, reading it to the end.</summary>
    public static PdfFont FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return FromBytes(buffer.ToArray());
    }

    /// <summary>Loads a TrueType font from its file bytes.</summary>
    public static PdfFont FromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new TrueTypePdfFont(TrueTypeFont.Parse(data));
    }

    /// <summary>Advance width of <paramref name="text"/> at <paramref name="size"/> points, without needing a page.</summary>
    public double WidthOfString(string text, double size)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WidthOf(WinAnsiEncoding.Encode(text), size);
    }

    /// <summary>
    /// Height <paramref name="text"/> takes when wrapped into <paramref name="width"/> points at
    /// <paramref name="size"/>: the number of lines times the line height (gap included) plus
    /// <paramref name="lineGap"/> per line. The same measurement <see cref="PdfPage.HeightOfString"/> makes.
    /// </summary>
    public double HeightOfString(string text, double size, double width, double lineGap = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Text.TextLayout.Wrap(text, width, this, size).Count * (LineHeight(size, includeGap: true) + lineGap);
    }

    /// <summary>
    /// Returns <paramref name="text"/> when it fits in <paramref name="maxWidth"/> points at
    /// <paramref name="size"/>; otherwise the longest prefix that fits together with
    /// <paramref name="ellipsis"/>, for single-line cells that must not wrap.
    /// </summary>
    public string Fit(string text, double size, double maxWidth, string ellipsis = "…")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(ellipsis);
        if (WidthOfString(text, size) <= maxWidth)
        {
            return text;
        }

        var room = maxWidth - WidthOfString(ellipsis, size);
        for (var length = text.Length - 1; length > 0; length--)
        {
            var prefix = text[..length].TrimEnd();
            if (prefix.Length > 0 && WidthOfString(prefix, size) <= room)
            {
                return prefix + ellipsis;
            }
        }

        return room >= 0 ? ellipsis : string.Empty;
    }

    /// <summary>
    /// Height of one line at <paramref name="size"/> points: ascender minus descender, plus the
    /// font's line gap when <paramref name="includeGap"/> is true (the advance used between lines).
    /// </summary>
    public double LineHeight(double size, bool includeGap = false)
    {
        var gap = includeGap ? LineGap : 0;
        return (Ascender + gap - Descender) * size / 1000.0;
    }

    /// <summary>Ascender in 1/1000 em.</summary>
    internal abstract double Ascender { get; }

    /// <summary>Descender in 1/1000 em (negative).</summary>
    internal abstract double Descender { get; }

    /// <summary>Extra leading in 1/1000 em.</summary>
    internal abstract double LineGap { get; }

    /// <summary>Advance width of a WinAnsi code in 1/1000 em.</summary>
    internal abstract double WidthOfCode(byte code);

    /// <summary>Writes the font dictionary (and whatever it references) and returns its reference.</summary>
    internal abstract PdfObjectRef WriteTo(PdfWriter writer, IReadOnlySet<byte> usedCodes);

    internal double WidthOf(ReadOnlySpan<byte> codes, double size)
    {
        var units = 0.0;
        foreach (var code in codes)
        {
            units += WidthOfCode(code);
        }

        return units * size / 1000.0;
    }
}
