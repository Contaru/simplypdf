using System.Runtime.CompilerServices;
using SimplyPdf.Text;

namespace SimplyPdf.Layout;

/// <summary>Where a <see cref="PdfTable.Draw"/> call stopped.</summary>
/// <param name="NextRow">Index of the first row not drawn; equals the row count when the table is complete.</param>
/// <param name="Bottom">The y coordinate just below the last row drawn (or below the header, if no row fit).</param>
/// <param name="IsComplete">True when every row has been drawn.</param>
public readonly record struct PdfTableSlice(int NextRow, double Bottom, bool IsComplete);

/// <summary>
/// A grid of text cells with fixed column widths that draws itself one slice at a time, so a long
/// table can continue on the next page: <see cref="Draw"/> paints as many rows as fit above a given
/// bottom, repeats the header on every slice, and reports where it stopped. Row heights follow the
/// wrapped cell text, measured with the same layout the page uses to draw it. Cells are plain
/// strings in the table's style, or <see cref="PdfTableCell"/>s where one needs its own font,
/// colours, alignment or column span.
/// </summary>
/// <remarks>
/// The typical loop is: draw; while the slice is not complete, add a page and draw again from
/// <see cref="PdfTableSlice.NextRow"/>. Every call draws at least the header and one row, even when
/// that row overflows the bottom, so the loop always advances. The page's font, colours and line
/// width are left as they were.
/// </remarks>
public sealed class PdfTable
{
    private static readonly PdfTableCell EmptyCell = new(string.Empty);

    private readonly PdfTableColumn[] _columns;
    private readonly PdfTableCell[] _header;
    private readonly List<PdfTableCell[]> _rows = [];
    private readonly List<string[]> _rowTexts = [];
    private readonly PdfTableStyle _style;
    private double? _headerHeight;

    /// <summary>Creates a table with the given columns and style (<see cref="PdfTableStyle.Default"/> when omitted).</summary>
    public PdfTable(IEnumerable<PdfTableColumn> columns, PdfTableStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(columns);
        _columns = columns.ToArray();
        if (_columns.Length == 0)
        {
            throw new ArgumentException("A table needs at least one column.", nameof(columns));
        }

        _header = _columns.Select(c => new PdfTableCell(c.Header) { Align = c.HeaderAlign }).ToArray();
        _style = style ?? PdfTableStyle.Default;
    }

    /// <summary>The columns, left to right.</summary>
    public IReadOnlyList<PdfTableColumn> Columns => _columns;

    /// <summary>The style the table is drawn with.</summary>
    public PdfTableStyle Style => _style;

    /// <summary>
    /// Rows added so far, each as one string per column: the text of the cell that starts at that
    /// column, and an empty string for the columns a spanning cell covers.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows => _rowTexts;

    /// <summary>Number of body rows.</summary>
    public int RowCount => _rows.Count;

    /// <summary>Total width in points: the sum of the column widths.</summary>
    public double Width => _columns.Sum(c => c.Width);

    /// <summary>Height of the header row, from its wrapped captions plus the vertical padding.</summary>
    public double HeaderHeight => _headerHeight ??= RowHeightOf(_header, _style.HeaderFont, _style.HeaderFontSize);

    /// <summary>Adds a body row; one cell per column, null cells count as empty.</summary>
    [OverloadResolutionPriority(1)] // plain strings (and bare nulls) stay on this overload
    public PdfTable AddRow(params string?[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Length != _columns.Length)
        {
            throw new ArgumentException($"Expected {_columns.Length} cells, got {cells.Length}.", nameof(cells));
        }

        return Add(cells.Select(c => new PdfTableCell(c ?? string.Empty)).ToArray());
    }

    /// <summary>
    /// Adds a body row of cells that may carry their own style or span several columns; plain strings
    /// can be mixed in and null cells count as empty. The column spans must add up to the column count.
    /// </summary>
    public PdfTable AddRow(params PdfTableCell?[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var row = cells.Select(c => c ?? EmptyCell).ToArray();
        var spanned = 0;
        foreach (var cell in row)
        {
            if (cell.ColumnSpan < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(cells), cell.ColumnSpan, "A cell spans at least one column.");
            }

            if (cell.FontSize is <= 0 or double.NaN)
            {
                throw new ArgumentOutOfRangeException(nameof(cells), cell.FontSize, "The font size of a cell must be positive.");
            }

            spanned += cell.ColumnSpan;
        }

        if (spanned != _columns.Length)
        {
            throw new ArgumentException($"The cells span {spanned} columns, but the table has {_columns.Length}.", nameof(cells));
        }

        return Add(row);
    }

    /// <summary>Height of the body row at <paramref name="index"/>, from its tallest wrapped cell plus the vertical padding.</summary>
    public double RowHeight(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _rows.Count);
        return RowHeightOf(_rows[index], _style.Font, _style.FontSize);
    }

    /// <summary>Height the whole table takes drawn in one slice: the header plus every row.</summary>
    public double Height => HeaderHeight + Enumerable.Range(0, _rows.Count).Sum(RowHeight);

    /// <summary>
    /// Draws the header (always on the first slice; on later ones when <see cref="PdfTableStyle.RepeatHeader"/>)
    /// and the rows from <paramref name="firstRow"/> that fit above <paramref name="maxY"/>, with the
    /// table's top-left corner at (<paramref name="x"/>, <paramref name="y"/>). Returns where it stopped.
    /// </summary>
    public PdfTableSlice Draw(PdfPage page, double x, double y, double maxY, int firstRow = 0)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfNegative(firstRow);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstRow, _rows.Count);

        var previousFont = page.CurrentFont;
        var previousSize = page.CurrentFontSize;
        page.Save();

        var cursor = y;
        var separators = new List<double>();
        var bands = new List<(double Top, double Bottom, PdfTableCell[] Cells)>();
        if (firstRow == 0 || _style.RepeatHeader)
        {
            cursor = DrawHeader(page, x, cursor);
            separators.Add(cursor);
            bands.Add((y, cursor, _header));
        }

        var next = firstRow;
        while (next < _rows.Count)
        {
            var height = RowHeight(next);
            if (cursor + height > maxY && next > firstRow)
            {
                break; // a later row that does not fit waits for the next slice; the first one always draws
            }

            DrawRow(page, next, x, cursor, height);
            bands.Add((cursor, cursor + height, _rows[next]));
            cursor += height;
            separators.Add(cursor);
            next++;
        }

        DrawGrid(page, x, y, cursor, separators, bands);
        page.Restore().Font(previousFont, previousSize);
        return new PdfTableSlice(next, cursor, next == _rows.Count);
    }

    private double DrawHeader(PdfPage page, double x, double y)
    {
        var height = HeaderHeight;
        if (_style.HeaderBackground is { } band)
        {
            page.Rect(x, y, Width, height).Fill(band);
        }

        DrawCells(page, _header, x, y, height, _style.HeaderFont, _style.HeaderFontSize, _style.HeaderTextColor);
        return y + height;
    }

    private void DrawRow(PdfPage page, int index, double x, double y, double height)
    {
        if (_style.StripeBackground is { } band && index % 2 == 1)
        {
            page.Rect(x, y, Width, height).Fill(band);
        }

        DrawCells(page, _rows[index], x, y, height, _style.Font, _style.FontSize, _style.TextColor);
    }

    /// <summary>Paints the cells' own backgrounds, then their text in the row's style or in their own.</summary>
    private void DrawCells(PdfPage page, PdfTableCell[] cells, double x, double y, double height, PdfFont font, double size, PdfColor color)
    {
        foreach (var (cell, _, left, width) in Place(cells))
        {
            if (cell.Background is { } background)
            {
                page.Rect(x + left, y, width, height).Fill(background);
            }
        }

        page.Font(font, size).FillColor(color);
        foreach (var (cell, column, left, width) in Place(cells))
        {
            if (string.IsNullOrEmpty(cell.Text))
            {
                continue;
            }

            page.Font(cell.Font ?? font, cell.FontSize ?? size);
            if (cell.TextColor is { } own)
            {
                page.FillColor(own);
            }

            page.Text(cell.Text, x + left + _style.PaddingX, y + _style.PaddingY, new TextOptions
            {
                Width = InnerWidth(width),
                Align = cell.Align ?? _columns[column].Align,
                LineGap = _style.LineGap,
            });

            if (cell.TextColor is not null)
            {
                page.FillColor(color); // back to the row's colour for the cells that follow
            }
        }
    }

    /// <summary>
    /// Outer frame, one line under the header and under every row, and one line between columns
    /// wherever two cells meet: a spanning cell interrupts the lines it covers.
    /// </summary>
    private void DrawGrid(
        PdfPage page, double x, double top, double bottom, List<double> separators, List<(double Top, double Bottom, PdfTableCell[] Cells)> bands)
    {
        if (_style.BorderColor is not { } color || _style.BorderWidth <= 0)
        {
            return;
        }

        page.LineWidth(_style.BorderWidth).Rect(x, top, Width, bottom - top);
        for (var i = 0; i < separators.Count - 1; i++) // the last separator is the frame's bottom edge
        {
            page.MoveTo(x, separators[i]).LineTo(x + Width, separators[i]);
        }

        var starts = bands.Select(band => Place(band.Cells).Select(placed => placed.Column).ToHashSet()).ToList();
        var edge = x;
        for (var boundary = 1; boundary < _columns.Length; boundary++)
        {
            edge += _columns[boundary - 1].Width;

            // One stroke per stretch of consecutive rows in which a cell starts at this boundary.
            double? from = null;
            var to = 0.0;
            for (var i = 0; i < bands.Count; i++)
            {
                if (starts[i].Contains(boundary))
                {
                    from ??= bands[i].Top;
                    to = bands[i].Bottom;
                }
                else if (from is { } open)
                {
                    page.MoveTo(edge, open).LineTo(edge, to);
                    from = null;
                }
            }

            if (from is { } last)
            {
                page.MoveTo(edge, last).LineTo(edge, to);
            }
        }

        page.Stroke(color);
    }

    private PdfTable Add(PdfTableCell[] row)
    {
        var texts = Enumerable.Repeat(string.Empty, _columns.Length).ToArray();
        foreach (var (cell, column, _, _) in Place(row))
        {
            texts[column] = cell.Text ?? string.Empty;
        }

        _rows.Add(row);
        _rowTexts.Add(texts);
        return this;
    }

    /// <summary>Where each cell of a row sits: the column it starts at, its left edge from the table's and its full width.</summary>
    private IEnumerable<(PdfTableCell Cell, int Column, double Left, double Width)> Place(PdfTableCell[] cells)
    {
        var column = 0;
        var left = 0.0;
        foreach (var cell in cells)
        {
            var width = 0.0;
            for (var i = column; i < column + cell.ColumnSpan; i++)
            {
                width += _columns[i].Width;
            }

            yield return (cell, column, left, width);
            column += cell.ColumnSpan;
            left += width;
        }
    }

    /// <summary>Tallest cell of the row, each measured wrapped in its own font, plus the vertical padding.</summary>
    private double RowHeightOf(PdfTableCell[] cells, PdfFont font, double size)
    {
        var tallest = 0.0;
        foreach (var (cell, _, _, width) in Place(cells))
        {
            var cellFont = cell.Font ?? font;
            var cellSize = cell.FontSize ?? size;
            var lines = TextLayout.Wrap(cell.Text ?? string.Empty, InnerWidth(width), cellFont, cellSize).Count;
            tallest = Math.Max(tallest, lines * (cellFont.LineHeight(cellSize, includeGap: true) + _style.LineGap));
        }

        return tallest + 2 * _style.PaddingY;
    }

    private double InnerWidth(double width) => Math.Max(0, width - 2 * _style.PaddingX);
}
