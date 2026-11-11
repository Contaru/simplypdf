using System.Globalization;
using System.Text.RegularExpressions;

namespace SimplyPdf.Tests;

public partial class LinkTests
{
    private const string Target = "https://contaru.com/facturas/FE-1";

    [Fact]
    public void Link_is_a_borderless_uri_annotation_with_a_bottom_left_rect()
    {
        var doc = new PdfDocument();
        doc.AddPage().Link(40, 100, 200, 12, Target); // Letter: 612 x 792

        var probe = new PdfProbe(doc.ToArray());
        var annotation = Assert.Single(Annotations(probe, page: 0));

        Assert.Contains("/Type /Annot", annotation, StringComparison.Ordinal);
        Assert.Contains("/Subtype /Link", annotation, StringComparison.Ordinal);
        // Top edge 100 from the top is 692 from the bottom; the bottom edge is 12 lower.
        Assert.Contains("/Rect [40 680 240 692]", annotation, StringComparison.Ordinal);
        Assert.Contains("/Border [0 0 0]", annotation, StringComparison.Ordinal);
        Assert.Contains("/A << /S /URI /URI (https://contaru.com/facturas/FE-1) >>", annotation, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_in_an_archival_file_carries_what_pdf_a_asks_of_an_annotation()
    {
        // ISO 19005-3, 6.3.2: /F with Print set and Hidden, Invisible, NoView and ToggleNoView clear,
        // which is exactly 4. A link needs nothing else: it is exempt from the appearance stream and
        // a URI action is allowed. Whether the file conforms is still veraPDF's call; this only keeps
        // the flag from going missing.
        var doc = new PdfDocument { Conformance = PdfConformance.PdfA3B };
        doc.AddPage().Link(40, 100, 200, 12, Target);

        var annotation = Assert.Single(Annotations(new PdfProbe(doc.ToArray()), page: 0));

        Assert.Contains("/F 4 ", annotation, StringComparison.Ordinal);
        Assert.Contains("/A << /S /URI /URI (https://contaru.com/facturas/FE-1) >>", annotation, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_returns_the_page_and_draws_nothing()
    {
        var plain = new PdfDocument { CompressStreams = false };
        plain.AddPage().Text("Ver factura", 40, 100);

        var linked = new PdfDocument { CompressStreams = false };
        var page = linked.AddPage();
        var returned = page.Text("Ver factura", 40, 100).Link(40, 100, 200, 12, Target);

        Assert.Same(page, returned);
        Assert.Equal(new PdfProbe(plain.ToArray()).FirstPageContent(), new PdfProbe(linked.ToArray()).FirstPageContent());
    }

    [Fact]
    public void Fractional_coordinates_keep_three_decimals_whatever_the_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-CO"); // decimal comma
            var doc = new PdfDocument();
            doc.AddPage().Link(10.5, 20.25, 100.125, 50, Target);

            var annotation = Assert.Single(Annotations(new PdfProbe(doc.ToArray()), page: 0));

            Assert.Contains("/Rect [10.5 721.75 110.625 771.75]", annotation, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Several_links_on_one_page_are_listed_in_call_order()
    {
        var doc = new PdfDocument();
        doc.AddPage()
            .Link(40, 100, 200, 12, "https://contaru.com")
            .Link(40, 120, 200, 12, "mailto:ventas@contaru.com")
            .Link(40, 140, 200, 12, "tel:+576015550100");

        var probe = new PdfProbe(doc.ToArray());
        var annotations = Annotations(probe, page: 0);

        Assert.Equal(3, annotations.Count);
        Assert.Contains("/URI (https://contaru.com)", annotations[0], StringComparison.Ordinal);
        Assert.Contains("/Rect [40 680 240 692]", annotations[0], StringComparison.Ordinal);
        Assert.Contains("/URI (mailto:ventas@contaru.com)", annotations[1], StringComparison.Ordinal);
        Assert.Contains("/Rect [40 660 240 672]", annotations[1], StringComparison.Ordinal);
        Assert.Contains("/URI (tel:+576015550100)", annotations[2], StringComparison.Ordinal);
        Assert.Contains("/Rect [40 640 240 652]", annotations[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Each_page_lists_its_own_links_and_flips_with_its_own_height()
    {
        var doc = new PdfDocument();
        doc.AddPage(PdfPageSize.Letter).Link(10, 10, 100, 20, "https://contaru.com/uno");
        doc.AddPage(new PdfPageSize(200, 400)).Link(10, 10, 100, 20, "https://contaru.com/dos");

        var probe = new PdfProbe(doc.ToArray());
        var first = Assert.Single(Annotations(probe, page: 0));
        var second = Assert.Single(Annotations(probe, page: 1));

        Assert.Contains("/URI (https://contaru.com/uno)", first, StringComparison.Ordinal);
        Assert.Contains("/Rect [10 762 110 782]", first, StringComparison.Ordinal);
        Assert.Contains("/URI (https://contaru.com/dos)", second, StringComparison.Ordinal);
        Assert.Contains("/Rect [10 370 110 390]", second, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_without_links_has_no_annots_entry()
    {
        var doc = new PdfDocument();
        doc.AddPage().Text("Sin enlaces");
        doc.AddPage().Link(10, 10, 100, 20, Target);

        var probe = new PdfProbe(doc.ToArray());
        var pages = PageObjects(probe);

        Assert.Equal(2, pages.Count);
        Assert.DoesNotContain("/Annots", pages[0], StringComparison.Ordinal);
        Assert.Contains("/Annots", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Document_with_links_keeps_a_consistent_xref()
    {
        var doc = new PdfDocument();
        doc.AddPage().Link(10, 10, 100, 20, Target).Link(10, 40, 100, 20, Target);
        doc.AddPage().Link(10, 10, 100, 20, Target);

        var probe = new PdfProbe(doc.ToArray());
        var offsets = probe.XrefOffsets();

        Assert.Equal(offsets.Count, offsets.Keys.Max()); // 1..n, no gaps
        var bodies = offsets.Keys.Select(probe.Object).ToList(); // asserts every "n 0 obj" sits at its declared offset
        Assert.Equal(3, bodies.Count(body => body.Contains("/Subtype /Link", StringComparison.Ordinal)));
        Assert.Contains($"/Size {offsets.Count + 1}", probe.Trailer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mailto:ventas@contaru.com?subject=Factura FE-1", "mailto:ventas@contaru.com?subject=Factura%20FE-1")]
    [InlineData("https://contaru.com/búsqueda?q=café&año=2026", "https://contaru.com/b%C3%BAsqueda?q=caf%C3%A9&a%C3%B1o=2026")]
    [InlineData("https://contaru.com/a%20b%2Fc?x=%C3%A9#frag", "https://contaru.com/a%20b%2Fc?x=%C3%A9#frag")] // already encoded: unchanged
    [InlineData("https://contaru.com/descuento-100%", "https://contaru.com/descuento-100%25")] // a '%' that is not an escape
    [InlineData("https://contaru.com/50%zz", "https://contaru.com/50%25zz")]
    [InlineData("https://contaru.com/\"<a>\"|{b}^`", "https://contaru.com/%22%3Ca%3E%22%7C%7Bb%7D%5E%60")]
    [InlineData("https://contaru.com/a\\b", "https://contaru.com/a%5Cb")]
    [InlineData("https://contaru.com/\U0001F600", "https://contaru.com/%F0%9F%98%80")] // surrogate pair -> 4 UTF-8 bytes
    [InlineData("https://[2001:db8::1]:8443/x;y=1,2!$'*+~", "https://[2001:db8::1]:8443/x;y=1,2!$'*+~")]
    [InlineData("  https://contaru.com/pago \r\n", "https://contaru.com/pago")] // white space around it is ignored
    public void Uri_is_percent_encoded_to_ascii(string uri, string expected)
    {
        var doc = new PdfDocument();
        doc.AddPage().Link(10, 10, 100, 20, uri);

        var annotation = Assert.Single(Annotations(new PdfProbe(doc.ToArray()), page: 0));

        Assert.Contains($"/URI ({expected}) >>", annotation, StringComparison.Ordinal);
        Assert.All(annotation, c => Assert.InRange(c, ' ', '~'));
    }

    [Fact]
    public void Parentheses_in_the_uri_are_escaped_in_the_pdf_string()
    {
        // '(' and ')' are legal in a URI but delimit a PDF string: unescaped, an unbalanced one would
        // swallow the rest of the dictionary.
        var doc = new PdfDocument();
        doc.AddPage().Link(10, 10, 100, 20, "https://es.wikipedia.org/wiki/Factura_(comercio");

        var annotation = Assert.Single(Annotations(new PdfProbe(doc.ToArray()), page: 0));

        Assert.Contains(@"/URI (https://es.wikipedia.org/wiki/Factura_\(comercio) >>", annotation, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_follows_a_scale_and_goes_back_to_page_space_after_restore()
    {
        // The way samples/A4Invoice draws: the whole page in CSS pixels under one Scale.
        var doc = new PdfDocument();
        doc.AddPage()
            .Save().Scale(0.5, 0.5)
            .Link(40, 100, 200, 16, "https://contaru.com/scaled")
            .Restore()
            .Link(40, 100, 200, 16, "https://contaru.com/plain");

        var annotations = Annotations(new PdfProbe(doc.ToArray()), page: 0);

        Assert.Contains("/Rect [20 734 120 742]", annotations[0], StringComparison.Ordinal);
        Assert.Contains("/Rect [40 676 240 692]", annotations[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Transforms_compose_in_the_order_they_were_applied()
    {
        var doc = new PdfDocument();
        doc.AddPage()
            .Save().Translate(10, 20).Scale(2, 2).Link(5, 5, 10, 10, Target).Restore()  // scaled, then moved: (20,30)-(40,50)
            .Save().Scale(2, 2).Translate(10, 20).Link(5, 5, 10, 10, Target).Restore(); // moved, then scaled: (30,50)-(50,70)

        var annotations = Annotations(new PdfProbe(doc.ToArray()), page: 0);

        Assert.Contains("/Rect [20 742 40 762]", annotations[0], StringComparison.Ordinal);
        Assert.Contains("/Rect [30 722 50 742]", annotations[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_save_and_restore_bring_back_the_matching_transform()
    {
        var doc = new PdfDocument();
        doc.AddPage()
            .Save().Translate(100, 0)
            .Save().Translate(0, 100)
            .Restore()
            .Link(0, 0, 10, 10, Target) // only the outer translation is left
            .Restore();

        var annotation = Assert.Single(Annotations(new PdfProbe(doc.ToArray()), page: 0));

        Assert.Contains("/Rect [100 782 110 792]", annotation, StringComparison.Ordinal);
    }

    [Fact]
    public void Quarter_turn_swaps_the_sides_of_the_rect()
    {
        // Rotate(90, 50, 50) maps (x, y) to (100 - y, x): a 100 x 10 strip becomes 10 x 100.
        var doc = new PdfDocument();
        doc.AddPage().Save().Rotate(90, 50, 50).Link(50, 50, 100, 10, Target).Restore();

        var annotation = Assert.Single(Annotations(new PdfProbe(doc.ToArray()), page: 0));

        Assert.Contains("/Rect [40 642 50 742]", annotation, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotated_link_covers_the_bounding_box_of_its_rectangle()
    {
        // A 100 pt square turned 45° around the page corner is a diamond 141.421 pt across;
        // /Rect cannot be a diamond, so it is the upright box around it.
        var doc = new PdfDocument();
        doc.AddPage().Rotate(45).Link(0, 0, 100, 100, Target);

        var annotation = Assert.Single(Annotations(new PdfProbe(doc.ToArray()), page: 0));

        Assert.Contains("/Rect [-70.711 650.579 70.711 792]", annotation, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_and_images_leave_the_tracked_transform_alone()
    {
        // They wrap their own operators in q ... Q, which must not leak into where links land.
        var doc = new PdfDocument();
        doc.AddPage()
            .Save().Scale(0.5, 0.5)
            .Text("Ver factura", 40, 100)
            .Image(PdfImage.FromGray(1, 1, [0]), 40, 120)
            .Link(40, 100, 200, 16, Target)
            .Restore();

        var annotation = Assert.Single(Annotations(new PdfProbe(doc.ToArray()), page: 0));

        Assert.Contains("/Rect [20 734 120 742]", annotation, StringComparison.Ordinal);
    }

    [Fact]
    public void Unbalanced_restore_keeps_the_current_transform()
    {
        var doc = new PdfDocument();
        doc.AddPage().Translate(10, 0).Restore().Link(0, 0, 10, 10, Target);

        var annotation = Assert.Single(Annotations(new PdfProbe(doc.ToArray()), page: 0));

        Assert.Contains("/Rect [10 782 20 792]", annotation, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_uri_is_rejected()
    {
        var page = new PdfDocument().AddPage();

        var error = Assert.Throws<ArgumentNullException>(() => page.Link(10, 10, 100, 20, null!));

        Assert.Equal("uri", error.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("www.contaru.com")] // no scheme: viewers disagree on what a relative URI means
    [InlineData("contaru.com/facturas")]
    [InlineData("ventas@contaru.com")]
    [InlineData("/facturas/FE-1")]
    [InlineData(@"C:\facturas\FE-1.pdf")] // a drive letter is not a scheme
    [InlineData("https:")] // a scheme and nothing else
    [InlineData("://contaru.com")]
    [InlineData("1https://contaru.com")] // a scheme starts with a letter
    [InlineData("ht tps://contaru.com")]
    public void Unusable_uri_is_rejected(string uri)
    {
        var doc = new PdfDocument();
        var page = doc.AddPage();

        var error = Assert.Throws<ArgumentException>(() => page.Link(10, 10, 100, 20, uri));

        Assert.Equal("uri", error.ParamName);
        Assert.DoesNotContain("/Annots", PageObjects(new PdfProbe(doc.ToArray()))[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Control_characters_and_unpaired_surrogates_are_rejected()
    {
        // Built here rather than in InlineData: attribute strings are stored as UTF-8, which cannot
        // carry an unpaired surrogate, and a NUL in a test name upsets some report writers.
        string[] unusable =
        [
            "https://contaru.com/a\nb", // a line break inside the URI (around it, it is trimmed)
            "https://contaru.com/a\tb",
            "https://contaru.com/\0",
            "https://contaru.com/\u007F",
            "https://contaru.com/a\u0085b", // C1 control
            "https://contaru.com/" + (char)0xD83D, // high surrogate at the end
            "https://contaru.com/" + (char)0xDE00 + "x", // low surrogate without its high half
        ];
        var page = new PdfDocument().AddPage();

        Assert.All(unusable, uri =>
        {
            var error = Assert.Throws<ArgumentException>(() => page.Link(10, 10, 100, 20, uri));
            Assert.Equal("uri", error.ParamName);
        });
    }

    [Theory]
    [InlineData(double.NaN, 10, 100, 20, "x")]
    [InlineData(10, double.PositiveInfinity, 100, 20, "y")]
    [InlineData(10, 10, double.NaN, 20, "width")]
    [InlineData(10, 10, 100, double.NegativeInfinity, "height")]
    [InlineData(10, 10, 0, 20, "width")] // nothing to click
    [InlineData(10, 10, -100, 20, "width")]
    [InlineData(10, 10, 100, 0, "height")]
    [InlineData(10, 10, 100, -20, "height")]
    public void Unusable_rectangle_is_rejected(double x, double y, double width, double height, string parameter)
    {
        var page = new PdfDocument().AddPage();

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => page.Link(x, y, width, height, Target));

        Assert.Equal(parameter, error.ParamName);
    }

    /// <summary>Page objects in document order.</summary>
    private static List<string> PageObjects(PdfProbe probe) =>
        probe.XrefOffsets().Keys.OrderBy(n => n).Select(probe.Object)
            .Where(body => body.Contains("/Type /Page ", StringComparison.Ordinal)).ToList();

    /// <summary>Bodies of the annotation objects referenced by the /Annots array of a page (0-based), in array order.</summary>
    private static List<string> Annotations(PdfProbe probe, int page)
    {
        var annots = AnnotsRegex().Match(PageObjects(probe)[page]);
        Assert.True(annots.Success, "/Annots missing");
        return ReferenceRegex().Matches(annots.Groups[1].Value)
            .Select(m => probe.Object(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))).ToList();
    }

    [GeneratedRegex(@"/Annots \[([^\]]*)\]")]
    private static partial Regex AnnotsRegex();

    [GeneratedRegex(@"(\d+) 0 R")]
    private static partial Regex ReferenceRegex();
}
