using System.Text;
using SimplyPdf.Internal;

namespace SimplyPdf;

/// <summary>
/// One page of a <see cref="PdfDocument"/>. All coordinates are in points with the origin at the
/// <b>top-left</b> corner and y growing downwards, like pdfkit and every screen API — the
/// bottom-left PDF origin is hidden behind an initial transform.
/// </summary>
/// <remarks>
/// Drawing follows the PDF model: path construction calls (<see cref="Rect"/>, <see cref="LineTo"/>…)
/// accumulate a path that the next painting call (<see cref="Fill"/>, <see cref="Stroke"/>…) consumes.
/// Text uses the current fill colour.
/// </remarks>
public sealed partial class PdfPage
{
    private readonly StringBuilder _content = new();
    private readonly HashSet<string> _usedFonts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedImages = new(StringComparer.Ordinal);
    private bool _hasPath;

    internal PdfPage(PdfDocument document, PdfPageSize size, PdfMargins margins)
    {
        Document = document;
        Width = size.Width;
        Height = size.Height;
        Margins = margins;
        X = margins.Left;
        Y = margins.Top;

        CurrentFont = PdfFont.Helvetica;
        CurrentFontSize = 12;

        // Flip the coordinate system so that the origin is at the top-left corner.
        _content.Append("1 0 0 -1 0 ").Append(N(Height)).Append(" cm\n");
    }

    /// <summary>The document this page belongs to.</summary>
    public PdfDocument Document { get; }

    /// <summary>Page width in points.</summary>
    public double Width { get; }

    /// <summary>Page height in points.</summary>
    public double Height { get; }

    /// <summary>Page margins; they only drive the text cursor's start and the default wrap width.</summary>
    public PdfMargins Margins { get; }

    /// <summary>Horizontal position of the text cursor, used by <see cref="Text(string, TextOptions?)"/>.</summary>
    public double X { get; set; }

    /// <summary>Vertical position of the text cursor; advances after each text call.</summary>
    public double Y { get; set; }

    /// <summary>Font used by subsequent text calls. Helvetica until <see cref="Font(PdfFont, double?)"/> is called.</summary>
    public PdfFont CurrentFont { get; private set; }

    /// <summary>Font size in points used by subsequent text calls.</summary>
    public double CurrentFontSize { get; private set; }

    internal IReadOnlyCollection<string> UsedFonts => _usedFonts;

    internal IReadOnlyCollection<string> UsedImages => _usedImages;

    internal byte[] GetContentBytes() => Encoding.ASCII.GetBytes(_content.ToString());

    // ----- Graphics state -----

    /// <summary>Pushes the graphics state (colours, line width, transforms, clip). Pair with <see cref="Restore"/>.</summary>
    public PdfPage Save()
    {
        _content.Append("q\n");
        return this;
    }

    /// <summary>Pops the graphics state saved by <see cref="Save"/>.</summary>
    public PdfPage Restore()
    {
        _content.Append("Q\n");
        return this;
    }

    /// <summary>Concatenates an arbitrary matrix [a b c d e f] with the current transformation.</summary>
    public PdfPage Transform(double a, double b, double c, double d, double e, double f)
    {
        _content.Append(N(a)).Append(' ').Append(N(b)).Append(' ').Append(N(c)).Append(' ')
            .Append(N(d)).Append(' ').Append(N(e)).Append(' ').Append(N(f)).Append(" cm\n");
        return this;
    }

    /// <summary>Moves the origin by (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public PdfPage Translate(double x, double y) => Transform(1, 0, 0, 1, x, y);

    /// <summary>Scales subsequent drawing around the given origin.</summary>
    public PdfPage Scale(double factorX, double factorY, double originX = 0, double originY = 0) =>
        Transform(factorX, 0, 0, factorY, originX - originX * factorX, originY - originY * factorY);

    /// <summary>
    /// Rotates subsequent drawing by <paramref name="degrees"/> (clockwise on the page, because y
    /// points down) around the given origin.
    /// </summary>
    public PdfPage Rotate(double degrees, double originX = 0, double originY = 0)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x1 = originX * cos - originY * sin;
        var y1 = originX * sin + originY * cos;
        return Transform(cos, sin, -sin, cos, originX - x1, originY - y1);
    }

    /// <summary>Sets the stroke line width in points (default 1).</summary>
    public PdfPage LineWidth(double width)
    {
        _content.Append(N(width)).Append(" w\n");
        return this;
    }

    /// <summary>Sets a dash pattern: <paramref name="length"/> on, <paramref name="space"/> off (defaults to the same length).</summary>
    public PdfPage Dash(double length, double? space = null, double phase = 0)
    {
        _content.Append('[').Append(N(length)).Append(' ').Append(N(space ?? length)).Append("] ").Append(N(phase)).Append(" d\n");
        return this;
    }

    /// <summary>Returns to solid strokes.</summary>
    public PdfPage Undash()
    {
        _content.Append("[] 0 d\n");
        return this;
    }

    /// <summary>Sets the colour used by <see cref="Fill"/> and by text.</summary>
    public PdfPage FillColor(PdfColor color)
    {
        _content.Append(color.ToOperands()).Append(" rg\n");
        return this;
    }

    /// <summary>Sets the colour used by <see cref="Stroke"/>.</summary>
    public PdfPage StrokeColor(PdfColor color)
    {
        _content.Append(color.ToOperands()).Append(" RG\n");
        return this;
    }

    // ----- Path construction -----

    /// <summary>Starts a new subpath at the given point.</summary>
    public PdfPage MoveTo(double x, double y)
    {
        _content.Append(N(x)).Append(' ').Append(N(y)).Append(" m\n");
        _hasPath = true;
        return this;
    }

    /// <summary>Appends a straight segment to the given point.</summary>
    public PdfPage LineTo(double x, double y)
    {
        _content.Append(N(x)).Append(' ').Append(N(y)).Append(" l\n");
        _hasPath = true;
        return this;
    }

    /// <summary>Appends a cubic Bézier segment.</summary>
    public PdfPage BezierCurveTo(double cp1X, double cp1Y, double cp2X, double cp2Y, double x, double y)
    {
        _content.Append(N(cp1X)).Append(' ').Append(N(cp1Y)).Append(' ').Append(N(cp2X)).Append(' ').Append(N(cp2Y))
            .Append(' ').Append(N(x)).Append(' ').Append(N(y)).Append(" c\n");
        _hasPath = true;
        return this;
    }

    /// <summary>Closes the current subpath with a straight segment back to its start.</summary>
    public PdfPage ClosePath()
    {
        _content.Append("h\n");
        return this;
    }

    /// <summary>Appends a rectangle whose top-left corner is (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public PdfPage Rect(double x, double y, double width, double height)
    {
        _content.Append(N(x)).Append(' ').Append(N(y)).Append(' ').Append(N(width)).Append(' ').Append(N(height)).Append(" re\n");
        _hasPath = true;
        return this;
    }

    /// <summary>Appends a rectangle with rounded corners.</summary>
    public PdfPage RoundedRect(double x, double y, double width, double height, double radius)
    {
        var r = Math.Min(radius, Math.Min(width, height) / 2);
        const double k = 0.5522847498; // Bézier approximation of a quarter circle
        var c = r * (1 - k);
        MoveTo(x + r, y);
        LineTo(x + width - r, y);
        BezierCurveTo(x + width - c, y, x + width, y + c, x + width, y + r);
        LineTo(x + width, y + height - r);
        BezierCurveTo(x + width, y + height - c, x + width - c, y + height, x + width - r, y + height);
        LineTo(x + r, y + height);
        BezierCurveTo(x + c, y + height, x, y + height - c, x, y + height - r);
        LineTo(x, y + r);
        BezierCurveTo(x, y + c, x + c, y, x + r, y);
        return ClosePath();
    }

    /// <summary>Appends an ellipse centred at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public PdfPage Ellipse(double x, double y, double radiusX, double radiusY)
    {
        const double k = 0.5522847498;
        var ox = radiusX * k;
        var oy = radiusY * k;
        MoveTo(x - radiusX, y);
        BezierCurveTo(x - radiusX, y - oy, x - ox, y - radiusY, x, y - radiusY);
        BezierCurveTo(x + ox, y - radiusY, x + radiusX, y - oy, x + radiusX, y);
        BezierCurveTo(x + radiusX, y + oy, x + ox, y + radiusY, x, y + radiusY);
        BezierCurveTo(x - ox, y + radiusY, x - radiusX, y + oy, x - radiusX, y);
        return ClosePath();
    }

    /// <summary>Appends a circle centred at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public PdfPage Circle(double x, double y, double radius) => Ellipse(x, y, radius, radius);

    /// <summary>Appends a closed polygon through the given points.</summary>
    public PdfPage Polygon(params ReadOnlySpan<(double X, double Y)> points)
    {
        if (points.Length == 0)
        {
            return this;
        }

        MoveTo(points[0].X, points[0].Y);
        for (var i = 1; i < points.Length; i++)
        {
            LineTo(points[i].X, points[i].Y);
        }

        return ClosePath();
    }

    // ----- Path painting -----

    /// <summary>
    /// Fills the current path, optionally setting the fill colour first. Calling it without a
    /// pending path only sets the colour, so <c>Fill("black")</c> can be used to pick the text colour.
    /// </summary>
    public PdfPage Fill(PdfColor? color = null)
    {
        if (color is { } c)
        {
            FillColor(c);
        }

        Paint("f");
        return this;
    }

    /// <summary>Strokes the current path, optionally setting the stroke colour first.</summary>
    public PdfPage Stroke(PdfColor? color = null)
    {
        if (color is { } c)
        {
            StrokeColor(c);
        }

        Paint("S");
        return this;
    }

    /// <summary>Fills then strokes the current path.</summary>
    public PdfPage FillAndStroke(PdfColor? fill = null, PdfColor? stroke = null)
    {
        if (fill is { } f)
        {
            FillColor(f);
        }

        if (stroke is { } s)
        {
            StrokeColor(s);
        }

        Paint("B");
        return this;
    }

    /// <summary>Uses the current path as a clipping region for subsequent drawing (until <see cref="Restore"/>).</summary>
    public PdfPage Clip()
    {
        Paint("W n");
        return this;
    }

    private void Paint(string op)
    {
        if (!_hasPath)
        {
            return; // a painting operator without a path is a no-op at best and an error in strict viewers
        }

        _content.Append(op).Append('\n');
        _hasPath = false;
    }

    private static string N(double value) => PdfFormat.Num(value);
}
