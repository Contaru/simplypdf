using System.Globalization;
using SimplyPdf.Layout;

namespace SimplyPdf.Tests.Layout;

/// <summary>The table: heights measured from the wrapped cells, slices that stop before the bottom and resume on the
/// next page with the header again, guaranteed progress, and a page left as it was found.</summary>
public sealed class PdfTableTests
{
    private static readonly PdfTableColumn[] Columns =
    [
        new("Item", 30, TextAlign.Center),
        new("Description", 200),
        new("Qty", 40, TextAlign.Right),
        new("Total", 80, TextAlign.Right),
    ];

    private static PdfTable Sample(int rows, PdfTableStyle? style = null)
    {
        var table = new PdfTable(Columns, style);
        for (var i = 1; i <= rows; i++)
        {
            table.AddRow(i.ToString(CultureInfo.InvariantCulture), "Product " + i, "1", "1,000.00");
        }

        return table;
    }

    private static (PdfDocument Document, PdfPage Page) Letter()
    {
        var document = new PdfDocument { CompressStreams = false };
        return (document, document.AddPage(PdfPageSize.Letter, PdfMargins.All(20)));
    }

    [Fact]
    public void Row_height_follows_the_tallest_wrapped_cell()
    {
        var table = Sample(1).AddRow("2", string.Join(' ', Enumerable.Repeat("wrap", 40)), "1", "1.00");
        var lineHeight = PdfFont.Helvetica.LineHeight(8, includeGap: true);

        Assert.Equal(lineHeight + 4, table.RowHeight(0), 6); // one line plus 2 pt of padding above and below
        Assert.True(table.RowHeight(1) > table.RowHeight(0) + lineHeight); // the long description wraps into several lines
        var lines = (table.RowHeight(1) - 4) / lineHeight;
        Assert.Equal(Math.Round(lines), lines, 6); // always a whole number of lines
        Assert.Equal(350, table.Width);
        Assert.Equal(table.HeaderHeight + table.RowHeight(0) + table.RowHeight(1), table.Height, 6);
    }

    [Fact]
    public void Empty_cells_still_take_one_line()
    {
        var table = new PdfTable(Columns).AddRow(null, "", null, "");

        Assert.Equal(PdfFont.Helvetica.LineHeight(8, includeGap: true) + 4, table.RowHeight(0), 6);
        Assert.Equal(["", "", "", ""], table.Rows[0]);
    }

    [Fact]
    public void Draw_completes_on_one_page_and_reports_the_bottom()
    {
        var table = Sample(3);
        var (document, page) = Letter();

        var slice = table.Draw(page, 30, 100, 700);

        Assert.Equal((3, true), (slice.NextRow, slice.IsComplete));
        Assert.Equal(100 + table.Height, slice.Bottom, 6);
        var content = new PdfProbe(document.ToArray()).FirstPageContent();
        Assert.Contains("(Description) Tj", content, StringComparison.Ordinal);
        Assert.Contains("(Product 3) Tj", content, StringComparison.Ordinal);
        Assert.Contains("30 100 350 ", content, StringComparison.Ordinal); // header band and frame start at the table's corner
        Assert.Contains("0.5 w", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_stops_before_the_bottom_and_resumes_with_the_header_on_the_next_page()
    {
        var table = Sample(40);
        var (document, first) = Letter();

        var slice = table.Draw(first, 30, 100, 200);
        Assert.False(slice.IsComplete);
        Assert.InRange(slice.NextRow, 1, 39);
        Assert.True(slice.Bottom <= 200);

        var second = document.AddPage(PdfPageSize.Letter, PdfMargins.All(20));
        var rest = table.Draw(second, 30, 50, 700, slice.NextRow);
        Assert.Equal((40, true), (rest.NextRow, rest.IsComplete));

        var probe = new PdfProbe(document.ToArray());
        var firstProduct = "(Product " + (slice.NextRow + 1).ToString(CultureInfo.InvariantCulture) + ") Tj";
        Assert.DoesNotContain(firstProduct, probe.PageContent(0), StringComparison.Ordinal);
        Assert.Contains(firstProduct, probe.PageContent(1), StringComparison.Ordinal);
        Assert.Contains("(Description) Tj", probe.PageContent(1), StringComparison.Ordinal); // header repeated
    }

    [Fact]
    public void Draw_can_leave_the_header_out_of_later_slices()
    {
        var table = Sample(40, new PdfTableStyle { RepeatHeader = false });
        var (document, first) = Letter();
        var slice = table.Draw(first, 30, 100, 200);
        var second = document.AddPage(PdfPageSize.Letter, PdfMargins.All(20));

        table.Draw(second, 30, 50, 700, slice.NextRow);

        Assert.DoesNotContain("(Description) Tj", new PdfProbe(document.ToArray()).PageContent(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_always_advances_even_when_nothing_fits()
    {
        var table = Sample(3);
        var (_, page) = Letter();

        var slice = table.Draw(page, 30, 100, 101);

        Assert.Equal((1, false), (slice.NextRow, slice.IsComplete));
        Assert.True(slice.Bottom > 101); // the header and the first row overflow rather than stall the caller
    }

    [Fact]
    public void Draw_leaves_the_page_font_as_it_found_it()
    {
        var table = Sample(2);
        var (_, page) = Letter();
        page.Font(StandardFont.TimesBold, 14);

        table.Draw(page, 30, 100, 700);

        Assert.Equal((PdfFont.TimesBold, 14.0), (page.CurrentFont, page.CurrentFontSize));
    }

    [Fact]
    public void Stripes_paint_every_other_row()
    {
        var table = Sample(4, new PdfTableStyle { StripeBackground = PdfColor.Gray(0.95), HeaderBackground = null, BorderColor = null });
        var (document, page) = Letter();

        table.Draw(page, 30, 100, 700);

        var content = new PdfProbe(document.ToArray()).FirstPageContent();
        Assert.Equal(2, content.Split(" re\n").Length - 1); // rows 2 and 4 only: no header band, no frame
    }

    [Fact]
    public void AddRow_rejects_a_wrong_number_of_cells()
    {
        var table = new PdfTable(Columns);

        Assert.Throws<ArgumentException>(() => table.AddRow("only", "three", "cells"));
        Assert.Throws<ArgumentException>(() => new PdfTable([]));
    }
}
