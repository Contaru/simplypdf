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

/// <summary>Layout options for <see cref="PdfPage.Text(string, double, double, TextOptions?)"/>.</summary>
public sealed class TextOptions
{
    internal static TextOptions Default { get; } = new();

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
