namespace SimplyPdf;

/// <summary>
/// A stretch of text in one font, size and colour. Several of them passed to
/// <see cref="PdfPage.Text(IEnumerable{TextRun}, double, double, TextOptions?)"/> form a single
/// paragraph that wraps and aligns as a whole, which is how a bold label and its value, or a
/// highlighted sentence, share a line.
/// </summary>
/// <param name="Text">The text of the run. Newlines start a new line, as in plain text.</param>
/// <param name="Font">Font of the run, or <see langword="null"/> for the page's current font.</param>
/// <param name="Size">Size in points, or <see langword="null"/> for the page's current size.</param>
/// <param name="Color">Colour of the run, or <see langword="null"/> for the current fill colour.</param>
public sealed record TextRun(string Text, PdfFont? Font = null, double? Size = null, PdfColor? Color = null)
{
    /// <summary>Lets a plain string stand for a run in the page's current style.</summary>
    public static implicit operator TextRun(string text) => new(text);

    /// <summary>Equivalent of the implicit string conversion, for languages without operator support.</summary>
    public static TextRun FromString(string text) => new(text);
}
