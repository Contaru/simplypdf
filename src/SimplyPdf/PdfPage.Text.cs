using SimplyPdf.Fonts;
using SimplyPdf.Internal;
using SimplyPdf.Text;

namespace SimplyPdf;

public sealed partial class PdfPage
{
    /// <summary>Selects the font (and optionally the size) for subsequent text.</summary>
    public PdfPage Font(PdfFont font, double? size = null)
    {
        ArgumentNullException.ThrowIfNull(font);
        CurrentFont = font;
        return size is { } s ? FontSize(s) : this;
    }

    /// <summary>Selects one of the standard 14 fonts (and optionally the size) for subsequent text.</summary>
    public PdfPage Font(StandardFont font, double? size = null) => Font(PdfFont.Standard(font), size);

    /// <summary>Sets the font size in points for subsequent text.</summary>
    public PdfPage FontSize(double size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        CurrentFontSize = size;
        return this;
    }

    /// <summary>Advance width of <paramref name="text"/> in the current font and size, in points.</summary>
    public double WidthOfString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return CurrentFont.WidthOf(WinAnsiEncoding.Encode(text), CurrentFontSize);
    }

    /// <summary>Height of one line in the current font and size, optionally including the font's line gap.</summary>
    public double CurrentLineHeight(bool includeGap = false) => CurrentFont.LineHeight(CurrentFontSize, includeGap);

    /// <summary>Total height <paramref name="text"/> would occupy if drawn with the given options.</summary>
    public double HeightOfString(string text, TextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= TextOptions.Default;
        var lines = TextLayout.Wrap(text, WrapWidth(X, options), CurrentFont, CurrentFontSize);
        return lines.Count * (CurrentLineHeight(includeGap: true) + options.LineGap);
    }

    /// <summary>
    /// Draws <paramref name="text"/> with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>)
    /// and moves the cursor below it.
    /// </summary>
    public PdfPage Text(string text, double x, double y, TextOptions? options = null)
    {
        X = x;
        Y = y;
        return Text(text, options);
    }

    /// <summary>Draws <paramref name="text"/> inside a box of the given width with the given alignment.</summary>
    public PdfPage Text(string text, double x, double y, double width, TextAlign align = TextAlign.Left) =>
        Text(text, x, y, new TextOptions { Width = width, Align = align });

    /// <summary>
    /// Draws <paramref name="text"/> at the current cursor (<see cref="X"/>, <see cref="Y"/>) and moves
    /// the cursor below it, so consecutive calls flow down the page.
    /// </summary>
    public PdfPage Text(string text, TextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= TextOptions.Default;

        var x = X;
        var y = Y;
        var boxWidth = BoxWidth(x, options);
        var lines = TextLayout.Wrap(text, WrapWidth(x, options), CurrentFont, CurrentFontSize);
        var lineHeight = CurrentLineHeight(includeGap: true) + options.LineGap;
        var ascent = CurrentFont.Ascender * CurrentFontSize / 1000.0;

        // Fonts are registered on first use so the document only carries the ones it needs,
        // and the codes drawn are recorded so embedded fonts can be subsetted.
        var fontName = Document.RegisterFontUse(CurrentFont, []);
        _usedFonts.Add(fontName);

        // Undo the page flip locally so glyphs are upright, then position each line with Tm.
        _content.Append("q\n1 0 0 -1 0 ").Append(N(Height)).Append(" cm\nBT\n/")
            .Append(fontName).Append(' ').Append(N(CurrentFontSize)).Append(" Tf\n");

        foreach (var line in lines)
        {
            var lineWidth = CurrentFont.WidthOf(line.Bytes, CurrentFontSize);
            var lineX = options.Align switch
            {
                TextAlign.Center => x + (boxWidth - lineWidth) / 2,
                TextAlign.Right => x + boxWidth - lineWidth,
                _ => x,
            };

            if (options.Align == TextAlign.Justify)
            {
                var spaces = line.EndsParagraph ? 0 : line.Bytes.Count(b => b == (byte)' ');
                var wordSpacing = spaces > 0 ? (boxWidth - lineWidth) / spaces : 0;
                _content.Append(N(wordSpacing)).Append(" Tw\n");
            }

            _content.Append("1 0 0 1 ").Append(N(lineX)).Append(' ').Append(N(Height - y - ascent)).Append(" Tm\n");
            if (line.Bytes.Length > 0)
            {
                Document.RegisterFontUse(CurrentFont, line.Bytes);
                PdfFormat.AppendLiteral(_content, line.Bytes);
                _content.Append(" Tj\n");
            }

            y += lineHeight;
        }

        _content.Append("ET\nQ\n");
        Y = y;
        return this;
    }

    /// <summary>Width of the text box used for alignment: explicit, or from x to the right margin.</summary>
    private double BoxWidth(double x, TextOptions options) => options.Width ?? Math.Max(0, Width - x - Margins.Right);

    /// <summary>Width at which lines wrap; infinite when wrapping is disabled.</summary>
    private double WrapWidth(double x, TextOptions options) => options.LineBreak ? BoxWidth(x, options) : double.PositiveInfinity;
}
