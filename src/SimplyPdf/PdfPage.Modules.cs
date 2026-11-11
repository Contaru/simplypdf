namespace SimplyPdf;

public sealed partial class PdfPage
{
    /// <summary>
    /// Draws a square matrix of dark and light modules — a QR code, or an Aztec or Data Matrix
    /// symbol — with its top-left corner, quiet zone included, at (<paramref name="x"/>, <paramref name="y"/>).
    /// Nothing is encoded here: the matrix comes from whatever generator the caller uses, e.g.
    /// <c>page.Modules(qr.Size, qr.GetModule, 375, 25, quietZone: 4)</c>.
    /// </summary>
    /// <param name="size">Modules per side (a version 1 QR code has 21).</param>
    /// <param name="isDark">
    /// Tells whether the module at (column, row) is dark. Both are zero-based and (0, 0) is the
    /// top-left module. It is called once per module, before anything is drawn.
    /// </param>
    /// <param name="x">Left edge of the symbol, quiet zone included.</param>
    /// <param name="y">Top edge of the symbol, quiet zone included.</param>
    /// <param name="width">
    /// Drawn width in points, quiet zone included; the module size follows from it. Use it to fit
    /// the symbol into a box whatever its module count. Exclusive with <paramref name="moduleSize"/>.
    /// </param>
    /// <param name="moduleSize">
    /// Side of one module in points. With neither this nor <paramref name="width"/>, one module is one point.
    /// </param>
    /// <param name="quietZone">Blank margin on every side, in modules (a QR code asks for 4). Nothing is painted there.</param>
    /// <param name="color">Colour of the dark modules; the current fill colour when omitted, as for text.</param>
    /// <remarks>
    /// <para>
    /// Each horizontal run of dark modules becomes one rectangle and the whole symbol is filled
    /// once, so neighbouring modules show no hairline seams in viewers that anti-alias. Only dark
    /// modules are painted: light ones and the quiet zone show whatever is underneath, so draw a
    /// light background first where the page is not white.
    /// </para>
    /// <para>
    /// The page's fill colour — which is also the text colour — is left as it was: a
    /// <paramref name="color"/> given here is set between <see cref="Save"/> and <see cref="Restore"/>
    /// and applies to this symbol only. Like <see cref="Fill"/>, this is a painting call that
    /// consumes the current path, so call it with no path pending.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A negative <paramref name="size"/> or <paramref name="quietZone"/>, a <paramref name="width"/> or
    /// <paramref name="moduleSize"/> that is not a positive finite number, or a position that is not finite.
    /// </exception>
    /// <exception cref="ArgumentException">Both <paramref name="width"/> and <paramref name="moduleSize"/> were given.</exception>
    public PdfPage Modules(int size, Func<int, int, bool> isDark, double x, double y,
        double? width = null, double? moduleSize = null, int quietZone = 0, PdfColor? color = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ArgumentNullException.ThrowIfNull(isDark);
        return FillModules(size, size, isDark, x, y, width, moduleSize, quietZone, color);
    }

    /// <summary>
    /// Draws a matrix of dark and light modules held in an array, which may be rectangular. See
    /// <see cref="Modules(int, Func{int, int, bool}, double, double, double?, double?, int, PdfColor?)"/>
    /// for how it is drawn and what it does to the graphics state.
    /// </summary>
    /// <param name="modules">
    /// The matrix, indexed <c>[row, column]</c> so that an array initialiser reads like the symbol; true is dark.
    /// </param>
    /// <param name="x">Left edge of the symbol, quiet zone included.</param>
    /// <param name="y">Top edge of the symbol, quiet zone included.</param>
    /// <param name="width">
    /// Drawn width in points, quiet zone included; modules stay square, so the height follows.
    /// Exclusive with <paramref name="moduleSize"/>.
    /// </param>
    /// <param name="moduleSize">
    /// Side of one module in points. With neither this nor <paramref name="width"/>, one module is one point.
    /// </param>
    /// <param name="quietZone">Blank margin on every side, in modules. Nothing is painted there.</param>
    /// <param name="color">Colour of the dark modules; the current fill colour when omitted, as for text.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A negative <paramref name="quietZone"/>, a <paramref name="width"/> or <paramref name="moduleSize"/>
    /// that is not a positive finite number, or a position that is not finite.
    /// </exception>
    /// <exception cref="ArgumentException">Both <paramref name="width"/> and <paramref name="moduleSize"/> were given.</exception>
    public PdfPage Modules(bool[,] modules, double x, double y,
        double? width = null, double? moduleSize = null, int quietZone = 0, PdfColor? color = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        return FillModules(modules.GetLength(1), modules.GetLength(0), (column, row) => modules[row, column],
            x, y, width, moduleSize, quietZone, color);
    }

    private PdfPage FillModules(int columns, int rows, Func<int, int, bool> isDark, double x, double y,
        double? width, double? moduleSize, int quietZone, PdfColor? color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quietZone);
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        if (width is not null && moduleSize is not null)
        {
            throw new ArgumentException("Give the drawn width or the module size, not both.", nameof(moduleSize));
        }

        RequirePositive(width, nameof(width));
        RequirePositive(moduleSize, nameof(moduleSize));

        if (columns == 0 || rows == 0)
        {
            return this;
        }

        // Everything that can throw (the caller's delegate included) runs before the first operator
        // is written, so a failure never leaves a half-drawn symbol or an unbalanced q on the page.
        var runs = DarkRuns(columns, rows, isDark);
        if (runs.Count == 0)
        {
            return this;
        }

        var module = moduleSize ?? (width is { } total ? total / (columns + 2.0 * quietZone) : 1);
        var xs = Edges(x + quietZone * module, module, columns);
        var ys = Edges(y + quietZone * module, module, rows);

        if (color is { } c)
        {
            Save().FillColor(c);
        }

        foreach (var (row, start, end) in runs)
        {
            Rect(xs[start], ys[row], xs[end] - xs[start], ys[row + 1] - ys[row]);
        }

        Fill();
        return color is null ? this : Restore();
    }

    /// <summary>Every maximal horizontal run of dark modules, as [Start, End) columns of a row.</summary>
    private static List<(int Row, int Start, int End)> DarkRuns(int columns, int rows, Func<int, int, bool> isDark)
    {
        var runs = new List<(int Row, int Start, int End)>();
        for (var row = 0; row < rows; row++)
        {
            var start = -1;
            for (var column = 0; column < columns; column++)
            {
                if (isDark(column, row))
                {
                    if (start < 0)
                    {
                        start = column;
                    }
                }
                else if (start >= 0)
                {
                    runs.Add((row, start, column));
                    start = -1;
                }
            }

            if (start >= 0)
            {
                runs.Add((row, start, columns));
            }
        }

        return runs;
    }

    /// <summary>
    /// The <paramref name="count"/> + 1 module boundaries along one axis, rounded to the three
    /// decimals a content stream keeps. Rectangles are then written as differences between
    /// boundaries, so a row ends exactly where the next one starts even when the module size is not
    /// a round number (80 pt across 37 modules); rounding each position and the size on their own
    /// would leave slivers of up to 0.001 pt between rows.
    /// </summary>
    private static double[] Edges(double origin, double module, int count)
    {
        var edges = new double[count + 1];
        for (var i = 0; i <= count; i++)
        {
            edges[i] = Math.Round(origin + i * module, 3, MidpointRounding.AwayFromZero);
        }

        return edges;
    }

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The position must be a finite number of points.");
        }
    }

    private static void RequirePositive(double? value, string name)
    {
        if (value is { } v && !(v > 0 && double.IsFinite(v)))
        {
            throw new ArgumentOutOfRangeException(name, v, "The size must be a positive, finite number of points.");
        }
    }
}
