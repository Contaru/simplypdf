namespace SimplyPdf.Fonts;

/// <summary>
/// AFM-derived metrics of one standard font, in 1/1000 em units. Widths are indexed by
/// WinAnsi code (0..255); codes below 32 are zero.
/// </summary>
internal sealed class FontMetricsData(
    string postScriptName,
    int ascender,
    int descender,
    int capHeight,
    int xHeight,
    int bboxLeft,
    int bboxBottom,
    int bboxRight,
    int bboxTop,
    ushort[] widths)
{
    public string PostScriptName { get; } = postScriptName;

    public int Ascender { get; } = ascender;

    public int Descender { get; } = descender;

    public int CapHeight { get; } = capHeight;

    public int XHeight { get; } = xHeight;

    public int BBoxLeft { get; } = bboxLeft;

    public int BBoxBottom { get; } = bboxBottom;

    public int BBoxRight { get; } = bboxRight;

    public int BBoxTop { get; } = bboxTop;

    public ushort[] Widths { get; } = widths;

    /// <summary>
    /// Extra leading pdfkit derives from the font bounding box, so that line heights match its
    /// output: bbox height minus (ascender - descender).
    /// </summary>
    public int LineGap => BBoxTop - BBoxBottom - (Ascender - Descender);
}

internal static partial class StandardFontMetrics
{
    public static FontMetricsData Get(StandardFont font)
    {
        var index = (int)font;
        if (index < 0 || index >= AllData.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(font), font, "Unknown standard font.");
        }

        return AllData[index];
    }
}
