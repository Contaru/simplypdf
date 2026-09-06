namespace SimplyPdf;

/// <summary>Page dimensions in PDF points (1/72 inch).</summary>
/// <param name="Width">Page width in points.</param>
/// <param name="Height">Page height in points.</param>
public readonly record struct PdfPageSize(double Width, double Height)
{
    /// <summary>US Letter, 8.5 x 11 in (612 x 792 pt).</summary>
    public static PdfPageSize Letter { get; } = new(612, 792);

    /// <summary>US Legal, 8.5 x 14 in (612 x 1008 pt).</summary>
    public static PdfPageSize Legal { get; } = new(612, 1008);

    /// <summary>US Tabloid, 11 x 17 in (792 x 1224 pt).</summary>
    public static PdfPageSize Tabloid { get; } = new(792, 1224);

    /// <summary>ISO A3 (841.89 x 1190.55 pt).</summary>
    public static PdfPageSize A3 { get; } = new(841.89, 1190.55);

    /// <summary>ISO A4 (595.28 x 841.89 pt).</summary>
    public static PdfPageSize A4 { get; } = new(595.28, 841.89);

    /// <summary>ISO A5 (419.53 x 595.28 pt).</summary>
    public static PdfPageSize A5 { get; } = new(419.53, 595.28);

    /// <summary>The same size rotated so that width is the longer side.</summary>
    public PdfPageSize Landscape => Width >= Height ? this : new PdfPageSize(Height, Width);

    /// <summary>The same size rotated so that height is the longer side.</summary>
    public PdfPageSize Portrait => Height >= Width ? this : new PdfPageSize(Height, Width);
}

/// <summary>Page margins in points. They only affect the text cursor's starting point and the default wrap width.</summary>
/// <param name="Top">Top margin.</param>
/// <param name="Right">Right margin.</param>
/// <param name="Bottom">Bottom margin.</param>
/// <param name="Left">Left margin.</param>
public readonly record struct PdfMargins(double Top, double Right, double Bottom, double Left)
{
    /// <summary>The same margin on all four sides.</summary>
    public static PdfMargins All(double value) => new(value, value, value, value);

    /// <summary>One inch (72 pt) on every side, the classic default.</summary>
    public static PdfMargins Default { get; } = All(72);
}
