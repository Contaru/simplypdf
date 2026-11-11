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

    /// <summary>
    /// <paramref name="text"/> if it fits in <paramref name="maxWidth"/> points in the current font and
    /// size, otherwise the longest prefix that fits with an ellipsis. See <see cref="PdfFont.Fit"/>.
    /// </summary>
    public string FitString(string text, double maxWidth, string ellipsis = "…") =>
        CurrentFont.Fit(text, CurrentFontSize, maxWidth, ellipsis);

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
        var baselineOffset = options.BaselineOffset(CurrentFont.Ascent(CurrentFontSize), CurrentFont.Descent(CurrentFontSize));

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

            _content.Append("1 0 0 1 ").Append(N(lineX)).Append(' ').Append(N(Height - y - baselineOffset)).Append(" Tm\n");
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

    /// <summary>
    /// Draws <paramref name="runs"/> as one paragraph with its top-left corner at
    /// (<paramref name="x"/>, <paramref name="y"/>) and moves the cursor below it. See
    /// <see cref="Text(IEnumerable{TextRun}, TextOptions?)"/>.
    /// </summary>
    public PdfPage Text(IEnumerable<TextRun> runs, double x, double y, TextOptions? options = null)
    {
        X = x;
        Y = y;
        return Text(runs, options);
    }

    /// <summary>
    /// Draws <paramref name="runs"/> as one paragraph at the current cursor and moves the cursor
    /// below it. The runs follow one another on the line, each in its own font, size and colour,
    /// and they wrap and align together: a line may break inside any run. Runs of different sizes
    /// sit on a common baseline, and each line is as tall as its tallest run. Whatever a run leaves
    /// unset comes from the page; the page's own font, size and colour are not changed.
    /// </summary>
    public PdfPage Text(IEnumerable<TextRun> runs, TextOptions? options = null)
    {
        var styled = Resolve(runs);
        options ??= TextOptions.Default;

        var x = X;
        var y = Y;
        var boxWidth = BoxWidth(x, options);
        var lines = TextLayout.Wrap(styled, WrapWidth(x, options));

        // Undo the page flip locally so glyphs are upright. Every segment is its own text object,
        // because a coloured one has to be bracketed by q/Q and those cannot appear inside BT/ET.
        _content.Append("q\n1 0 0 -1 0 ").Append(N(Height)).Append(" cm\n");

        foreach (var line in lines)
        {
            var lineWidth = line.Width;
            var segmentX = options.Align switch
            {
                TextAlign.Center => x + (boxWidth - lineWidth) / 2,
                TextAlign.Right => x + boxWidth - lineWidth,
                _ => x,
            };

            var wordSpacing = 0.0;
            if (options.Align == TextAlign.Justify)
            {
                var spaces = line.EndsParagraph ? 0 : line.Spaces;
                wordSpacing = spaces > 0 ? (boxWidth - lineWidth) / spaces : 0;
                _content.Append(N(wordSpacing)).Append(" Tw\n");
            }

            var baseline = Height - y - options.BaselineOffset(line.Ascent, line.Descent);
            foreach (var segment in line.Segments)
            {
                var fontName = Document.RegisterFontUse(segment.Font, segment.Bytes);
                _usedFonts.Add(fontName);

                if (segment.Color is { } color)
                {
                    _content.Append("q\n").Append(color.ToOperands()).Append(" rg\n");
                }

                _content.Append("BT\n/").Append(fontName).Append(' ').Append(N(segment.Size)).Append(" Tf\n1 0 0 1 ")
                    .Append(N(segmentX)).Append(' ').Append(N(baseline)).Append(" Tm\n");
                PdfFormat.AppendLiteral(_content, segment.Bytes);
                _content.Append(" Tj\nET\n");

                if (segment.Color is not null)
                {
                    _content.Append("Q\n");
                }

                segmentX += segment.Width + wordSpacing * segment.Spaces;
            }

            y += line.Height + options.LineGap;
        }

        _content.Append("Q\n");
        Y = y;
        return this;
    }

    /// <summary>Advance width of <paramref name="runs"/> laid end to end on one line, in points.</summary>
    public double WidthOfString(IEnumerable<TextRun> runs) =>
        Resolve(runs).Sum(run => run.Font.WidthOf(WinAnsiEncoding.Encode(run.Text), run.Size));

    /// <summary>Total height <paramref name="runs"/> would occupy if drawn as one paragraph with the given options.</summary>
    public double HeightOfString(IEnumerable<TextRun> runs, TextOptions? options = null)
    {
        var styled = Resolve(runs);
        options ??= TextOptions.Default;
        return TextLayout.Wrap(styled, WrapWidth(X, options)).Sum(line => line.Height + options.LineGap);
    }

    /// <summary>Fills in the font and size each run leaves to the page. No runs at all behave like an empty string.</summary>
    private List<TextLayout.StyledRun> Resolve(IEnumerable<TextRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var styled = new List<TextLayout.StyledRun>();
        foreach (var run in runs)
        {
            if (run?.Text is null)
            {
                throw new ArgumentException("A run and its text cannot be null.", nameof(runs));
            }

            if (run.Size is <= 0 or double.NaN)
            {
                throw new ArgumentOutOfRangeException(nameof(runs), run.Size, "The size of a run must be positive.");
            }

            styled.Add(new TextLayout.StyledRun(run.Text, run.Font ?? CurrentFont, run.Size ?? CurrentFontSize, run.Color));
        }

        if (styled.Count == 0)
        {
            styled.Add(new TextLayout.StyledRun(string.Empty, CurrentFont, CurrentFontSize, null));
        }

        return styled;
    }

    /// <summary>Width of the text box used for alignment: explicit, or from x to the right margin.</summary>
    private double BoxWidth(double x, TextOptions options) => options.Width ?? Math.Max(0, Width - x - Margins.Right);

    /// <summary>Width at which lines wrap; infinite when wrapping is disabled.</summary>
    private double WrapWidth(double x, TextOptions options) => options.LineBreak ? BoxWidth(x, options) : double.PositiveInfinity;
}
