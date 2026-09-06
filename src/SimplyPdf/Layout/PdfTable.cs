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
/// wrapped cell text, measured with the same layout the page uses to draw it.
/// </summary>
/// <remarks>
/// The typical loop is: draw; while the slice is not complete, add a page and draw again from
/// <see cref="PdfTableSlice.NextRow"/>. Every call draws at least the header and one row, even when
/// that row overflows the bottom, so the loop always advances. The page's font, colours and line
/// width are left as they were.
/// </remarks>
public sealed class PdfTable
{
    private readonly PdfTableColumn[] _columns;
    private readonly List<string[]> _rows = [];
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

        _style = style ?? PdfTableStyle.Default;
    }

    /// <summary>The columns, left to right.</summary>
    public IReadOnlyList<PdfTableColumn> Columns => _columns;

    /// <summary>The style the table is drawn with.</summary>
    public PdfTableStyle Style => _style;

    /// <summary>Rows added so far, each as one string per column.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows => _rows;

    /// <summary>Number of body rows.</summary>
    public int RowCount => _rows.Count;

    /// <summary>Total width in points: the sum of the column widths.</summary>
    public double Width => _columns.Sum(c => c.Width);

    /// <summary>Height of the header row, from its wrapped captions plus the vertical padding.</summary>
    public double HeaderHeight => _headerHeight ??= RowHeightOf(_columns.Select(c => c.Header).ToArray(), _style.HeaderFont, _style.HeaderFontSize);

    /// <summary>Adds a body row; one cell per column, null cells count as empty.</summary>
    public PdfTable AddRow(params string?[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Length != _columns.Length)
        {
            throw new ArgumentException($"Expected {_columns.Length} cells, got {cells.Length}.", nameof(cells));
        }

        _rows.Add(cells.Select(c => c ?? string.Empty).ToArray());
        return this;
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
        if (firstRow == 0 || _style.RepeatHeader)
        {
            cursor = DrawHeader(page, x, cursor);
            separators.Add(cursor);
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
            cursor += height;
            separators.Add(cursor);
            next++;
        }

        DrawGrid(page, x, y, cursor, separators);
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

        page.Font(_style.HeaderFont, _style.HeaderFontSize).FillColor(_style.HeaderTextColor);
        DrawCells(page, _columns.Select(c => c.Header).ToArray(), x, y);
        return y + height;
    }

    private void DrawRow(PdfPage page, int index, double x, double y, double height)
    {
        if (_style.StripeBackground is { } band && index % 2 == 1)
        {
            page.Rect(x, y, Width, height).Fill(band);
        }

        page.Font(_style.Font, _style.FontSize).FillColor(_style.TextColor);
        DrawCells(page, _rows[index], x, y);
    }

    private void DrawCells(PdfPage page, string[] cells, double x, double y)
    {
        var cellX = x;
        for (var i = 0; i < _columns.Length; i++)
        {
            var column = _columns[i];
            if (cells[i].Length > 0)
            {
                page.Text(cells[i], cellX + _style.PaddingX, y + _style.PaddingY, new TextOptions
                {
                    Width = InnerWidth(column),
                    Align = column.Align,
                    LineGap = _style.LineGap,
                });
            }

            cellX += column.Width;
        }
    }

    /// <summary>Outer frame, one line under the header and under every row, one line between columns.</summary>
    private void DrawGrid(PdfPage page, double x, double top, double bottom, List<double> separators)
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

        var edge = x;
        for (var i = 0; i < _columns.Length - 1; i++)
        {
            edge += _columns[i].Width;
            page.MoveTo(edge, top).LineTo(edge, bottom);
        }

        page.Stroke(color);
    }

    private double RowHeightOf(string[] cells, PdfFont font, double size)
    {
        var lineHeight = font.LineHeight(size, includeGap: true) + _style.LineGap;
        var lines = 1;
        for (var i = 0; i < _columns.Length; i++)
        {
            lines = Math.Max(lines, TextLayout.Wrap(cells[i], InnerWidth(_columns[i]), font, size).Count);
        }

        return lines * lineHeight + 2 * _style.PaddingY;
    }

    private double InnerWidth(PdfTableColumn column) => Math.Max(0, column.Width - 2 * _style.PaddingX);
}
