namespace SimplyPdf.Layout;

/// <summary>
/// Fonts, colours and spacing shared by every cell of a <see cref="PdfTable"/>. Immutable once built;
/// reuse one instance across the tables of a document.
/// </summary>
public sealed class PdfTableStyle
{
    /// <summary>Helvetica 8 pt cells, Helvetica-Bold 8 pt header on a light grey band, light grey grid.</summary>
    public static PdfTableStyle Default { get; } = new();

    /// <summary>Font of the body cells.</summary>
    public PdfFont Font { get; init; } = PdfFont.Helvetica;

    /// <summary>Size of the body cells in points.</summary>
    public double FontSize { get; init; } = 8;

    /// <summary>Font of the header row.</summary>
    public PdfFont HeaderFont { get; init; } = PdfFont.HelveticaBold;

    /// <summary>Size of the header row in points.</summary>
    public double HeaderFontSize { get; init; } = 8;

    /// <summary>Colour of the body text.</summary>
    public PdfColor TextColor { get; init; } = PdfColor.Black;

    /// <summary>Colour of the header text.</summary>
    public PdfColor HeaderTextColor { get; init; } = PdfColor.Black;

    /// <summary>Band painted behind the header row, or null for none.</summary>
    public PdfColor? HeaderBackground { get; init; } = PdfColor.FromRgb(227, 227, 227);

    /// <summary>Band painted behind every other body row (the second, fourth…), or null for none.</summary>
    public PdfColor? StripeBackground { get; init; }

    /// <summary>Colour of the grid (outer frame, row and column separators), or null to draw no lines.</summary>
    public PdfColor? BorderColor { get; init; } = PdfColor.FromRgb(227, 227, 227);

    /// <summary>Width of the grid lines in points.</summary>
    public double BorderWidth { get; init; } = 0.5;

    /// <summary>Horizontal space between a cell's border and its text.</summary>
    public double PaddingX { get; init; } = 3;

    /// <summary>Vertical space above and below a cell's text.</summary>
    public double PaddingY { get; init; } = 2;

    /// <summary>Extra space between the lines of a wrapped cell, in points.</summary>
    public double LineGap { get; init; }

    /// <summary>Whether every slice drawn on a new page starts with the header row again. Default true.</summary>
    public bool RepeatHeader { get; init; } = true;
}
