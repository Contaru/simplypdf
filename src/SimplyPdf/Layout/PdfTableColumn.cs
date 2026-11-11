namespace SimplyPdf.Layout;

/// <summary>A column of a <see cref="PdfTable"/>: its caption, its width in points and how its cells align.</summary>
public sealed class PdfTableColumn
{
    /// <summary>Creates a column <paramref name="width"/> points wide.</summary>
    public PdfTableColumn(string header, double width, TextAlign align = TextAlign.Left)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        Header = header;
        Width = width;
        Align = align;
    }

    /// <summary>Caption drawn in the header row.</summary>
    public string Header { get; }

    /// <summary>Column width in points, borders included.</summary>
    public double Width { get; }

    /// <summary>Horizontal alignment of the cells (the header follows it too, unless <see cref="HeaderAlign"/> says otherwise).</summary>
    public TextAlign Align { get; }

    /// <summary>
    /// Horizontal alignment of the caption alone, or <see langword="null"/> to follow <see cref="Align"/>:
    /// a centred caption over right-aligned amounts, for instance.
    /// </summary>
    public TextAlign? HeaderAlign { get; init; }
}
