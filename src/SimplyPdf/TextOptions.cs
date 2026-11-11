namespace SimplyPdf;

/// <summary>Horizontal alignment of text inside its box.</summary>
public enum TextAlign
{
    /// <summary>Flush left (default).</summary>
    Left,

    /// <summary>Centred inside the box width.</summary>
    Center,

    /// <summary>Flush right.</summary>
    Right,

    /// <summary>Stretched to the box width by widening spaces; the last line of each paragraph stays flush left.</summary>
    Justify,
}

/// <summary>
/// What the y coordinate of a text call refers to on the first line, like the canvas
/// <c>textBaseline</c> and pdfkit's <c>baseline</c>. The following lines and the cursor keep
/// the same reference, so text placed by its baseline flows from baseline to baseline.
/// </summary>
public enum TextBaseline
{
    /// <summary>y is the top of the line (default).</summary>
    Top,

    /// <summary>y is the middle of the line, halfway between its top and its bottom.</summary>
    Middle,

    /// <summary>y is the baseline the letters sit on, which is how CSS, SVG and design tools give positions.</summary>
    Alphabetic,

    /// <summary>y is the bottom of the line, below the descenders.</summary>
    Bottom,
}

/// <summary>Layout options for <see cref="PdfPage.Text(string, double, double, TextOptions?)"/>.</summary>
public sealed class TextOptions
{
    internal static TextOptions Default { get; } = new();

    /// <summary>What y refers to: the top of the line unless stated otherwise. Default <see cref="TextBaseline.Top"/>.</summary>
    public TextBaseline Baseline { get; init; } = TextBaseline.Top;

    /// <summary>
    /// How far below y the baseline of a line sits, given how far the line reaches above
    /// (<paramref name="ascent"/>) and below (<paramref name="descent"/>) that baseline.
    /// </summary>
    internal double BaselineOffset(double ascent, double descent) => Baseline switch
    {
        TextBaseline.Alphabetic => 0,
        TextBaseline.Middle => (ascent - descent) / 2,
        TextBaseline.Bottom => -descent,
        _ => ascent,
    };

    /// <summary>
    /// Width of the text box, used for wrapping and alignment. When omitted the box runs from the
    /// current x position to the right margin.
    /// </summary>
    public double? Width { get; init; }

    /// <summary>Horizontal alignment. Default <see cref="TextAlign.Left"/>.</summary>
    public TextAlign Align { get; init; } = TextAlign.Left;

    /// <summary>Extra space added below every line, in points. Default 0.</summary>
    public double LineGap { get; init; }

    /// <summary>
    /// Whether long lines wrap at word boundaries. Explicit newlines always start a new line.
    /// Default true.
    /// </summary>
    public bool LineBreak { get; init; } = true;
}
