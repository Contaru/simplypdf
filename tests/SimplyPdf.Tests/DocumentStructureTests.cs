using System.Globalization;

namespace SimplyPdf.Tests;

public class DocumentStructureTests
{
    [Fact]
    public void Minimal_document_has_valid_header_xref_and_trailer()
    {
        var doc = new PdfDocument();
        doc.Info.CreationDate = null;
        doc.AddPage();

        var probe = new PdfProbe(doc.ToArray());

        Assert.Equal("%PDF-1.4", probe.Header);
        Assert.True(probe.EndsWithEof);

        var offsets = probe.XrefOffsets();
        Assert.Equal(offsets.Count, offsets.Keys.Max()); // 1..n, no gaps
        foreach (var number in offsets.Keys)
        {
            probe.Object(number); // asserts "n 0 obj" sits exactly at the declared offset
        }

        Assert.Contains("/Root 1 0 R", probe.Trailer, StringComparison.Ordinal);
        Assert.Contains($"/Size {offsets.Count + 1}", probe.Trailer, StringComparison.Ordinal);
        Assert.Contains("/Type /Catalog", probe.Object(1), StringComparison.Ordinal);
        Assert.Contains("/Type /Pages", probe.Object(2), StringComparison.Ordinal);
        Assert.Contains("/Count 1", probe.Object(2), StringComparison.Ordinal);
    }

    [Fact]
    public void Page_declares_letter_media_box_and_flips_coordinates()
    {
        var doc = new PdfDocument { CompressStreams = false };
        var page = doc.AddPage();

        var probe = new PdfProbe(doc.ToArray());
        var pageObject = probe.Object(probe.XrefOffsets().Keys.First(n => probe.Object(n).Contains("/Type /Page ", StringComparison.Ordinal)));

        Assert.Contains("/MediaBox [0 0 612 792]", pageObject, StringComparison.Ordinal);
        Assert.Equal(612, page.Width);
        Assert.Equal(792, page.Height);
        Assert.StartsWith("1 0 0 -1 0 792 cm\n", probe.FirstPageContent(), StringComparison.Ordinal);
    }

    [Fact]
    public void Output_is_culture_invariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-CO"); // decimal comma
            var doc = new PdfDocument { CompressStreams = false };
            doc.AddPage(new PdfPageSize(595.28, 841.89))
                .Rect(10.5, 20.25, 100.125, 50)
                .Fill("#e3e3e3")
                .Font(StandardFont.Helvetica, 8.5)
                .Text("Hola", 1.5, 2.5);

            var probe = new PdfProbe(doc.ToArray());
            var content = probe.FirstPageContent();

            Assert.DoesNotContain(",", content, StringComparison.Ordinal);
            Assert.Contains("10.5 20.25 100.125 50 re", content, StringComparison.Ordinal);
            Assert.Contains("/F1 8.5 Tf", content, StringComparison.Ordinal);
            Assert.Contains("/MediaBox [0 0 595.28 841.89]", probe.Object(probe.XrefOffsets().Keys.First(n => probe.Object(n).Contains("/MediaBox", StringComparison.Ordinal))), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Info_dictionary_uses_utf16_for_non_ascii_text()
    {
        var doc = new PdfDocument();
        doc.Info.Title = "Factura electrónica";
        doc.Info.Author = "Almacenes Universal S.A.S.";
        doc.Info.CreationDate = new DateTimeOffset(2026, 8, 28, 16, 14, 7, TimeSpan.FromHours(-5));
        doc.AddPage();

        var probe = new PdfProbe(doc.ToArray());
        var info = probe.Object(probe.XrefOffsets().Keys.First(n => probe.Object(n).Contains("/Title", StringComparison.Ordinal)));

        Assert.Contains("/Title <FEFF", info, StringComparison.Ordinal);
        Assert.Contains("/Author (Almacenes Universal S.A.S.)", info, StringComparison.Ordinal);
        Assert.Contains("/CreationDate (D:20260828161407-05'00')", info, StringComparison.Ordinal);
        Assert.Contains("/Producer (SimplyPdf)", info, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_streams_are_flate_compressed_by_default()
    {
        var doc = new PdfDocument();
        doc.AddPage().Text("Hello");

        var probe = new PdfProbe(doc.ToArray());
        var contentObject = probe.XrefOffsets().Keys.First(n => probe.Object(n).Contains("/FlateDecode", StringComparison.Ordinal));

        Assert.Contains("(Hello) Tj", System.Text.Encoding.Latin1.GetString(probe.StreamData(contentObject)), StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_without_pages_throws()
    {
        var doc = new PdfDocument();
        Assert.Throws<InvalidOperationException>(() => doc.ToArray());
    }

    [Fact]
    public void Fonts_are_registered_once_per_document_and_listed_per_page()
    {
        var doc = new PdfDocument();
        doc.AddPage().Font(StandardFont.HelveticaBold).Text("A").Font(StandardFont.Helvetica).Text("B");
        doc.AddPage().Font(StandardFont.HelveticaBold).Text("C");

        var probe = new PdfProbe(doc.ToArray());
        var objects = probe.XrefOffsets().Keys.Select(probe.Object).ToList();

        Assert.Equal(2, objects.Count(o => o.Contains("/Type /Font", StringComparison.Ordinal)));
        Assert.Single(objects, o => o.Contains("/BaseFont /Helvetica-Bold", StringComparison.Ordinal));
        Assert.All(objects.Where(o => o.Contains("/Type /Font", StringComparison.Ordinal)),
            o => Assert.Contains("/Encoding /WinAnsiEncoding", o, StringComparison.Ordinal));

        var pages = objects.Where(o => o.Contains("/Type /Page ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, pages.Count);
        Assert.Contains("/F1", pages[0], StringComparison.Ordinal);
        Assert.Contains("/F2", pages[0], StringComparison.Ordinal);
    }
}
