namespace SimplyPdf.Layout;

/// <summary>
/// A body cell of a <see cref="PdfTable"/>: its text and whatever sets it apart from the table's
/// <see cref="PdfTableStyle"/>. A plain string converts to a cell in the table's style, so only
/// the cells that differ need one: a bold total, a highlighted amount, a label that spans columns.
/// </summary>
/// <param name="Text">The text of the cell; it wraps inside the cell like any other.</param>
public sealed record PdfTableCell(string Text)
{
    /// <summary>Font of this cell, or <see langword="null"/> for the table's.</summary>
    public PdfFont? Font { get; init; }

    /// <summary>Font size of this cell in points, or <see langword="null"/> for the table's.</summary>
    public double? FontSize { get; init; }

    /// <summary>Colour of this cell's text, or <see langword="null"/> for the table's.</summary>
    public PdfColor? TextColor { get; init; }

    /// <summary>Colour painted behind this cell, over any stripe, or <see langword="null"/> for none.</summary>
    public PdfColor? Background { get; init; }

    /// <summary>Horizontal alignment of this cell, or <see langword="null"/> for that of the column it starts at.</summary>
    public TextAlign? Align { get; init; }

    /// <summary>
    /// Number of columns the cell covers, starting at its own. The spans of a row must add up to the
    /// table's column count; the grid draws no line across a spanning cell. Default 1.
    /// </summary>
    public int ColumnSpan { get; init; } = 1;

    /// <summary>Lets a plain string stand for a cell in the table's style; <see langword="null"/> is an empty cell.</summary>
    public static implicit operator PdfTableCell(string? text) => new(text ?? string.Empty);

    /// <summary>Equivalent of the implicit string conversion, for languages without operator support.</summary>
    public static PdfTableCell FromString(string? text) => new(text ?? string.Empty);
}
