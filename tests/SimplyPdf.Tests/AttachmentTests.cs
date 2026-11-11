using System.Text;

namespace SimplyPdf.Tests;

public class AttachmentTests
{
    private static readonly byte[] Xml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><Invoice><ID>FSII1305</ID><Note>ñandú</Note></Invoice>");

    private static PdfDocument NewDocument()
    {
        var doc = new PdfDocument();
        doc.Info.CreationDate = new DateTimeOffset(2026, 11, 3, 17, 26, 5, TimeSpan.FromHours(-5));
        doc.AddPage();
        return doc;
    }

    private static List<(int Number, string Body)> Objects(PdfProbe probe) =>
        probe.XrefOffsets().Keys.Select(n => (n, probe.Object(n))).ToList();

    [Fact]
    public void An_attached_file_is_embedded_with_its_specification_and_listed_in_the_catalog()
    {
        var doc = NewDocument();
        var attachment = doc.Attach("factura.xml", Xml, "application/xml", "Factura electrónica UBL", PdfAttachmentRelationship.Alternative);

        var probe = new PdfProbe(doc.ToArray());
        var objects = Objects(probe);

        var file = Assert.Single(objects, o => o.Body.Contains("/Type /EmbeddedFile", StringComparison.Ordinal));
        Assert.Contains("/Subtype /application#2Fxml", file.Body, StringComparison.Ordinal);
        Assert.Contains($"/Params << /Size {Xml.Length} /ModDate (D:20261103172605-05'00') >>", file.Body, StringComparison.Ordinal);
        Assert.Equal(Xml, probe.StreamData(file.Number)); // the bytes come back exactly

        var specification = Assert.Single(objects, o => o.Body.Contains("/Type /Filespec", StringComparison.Ordinal));
        Assert.Contains("/F (factura.xml) /UF (factura.xml)", specification.Body, StringComparison.Ordinal);
        Assert.Contains($"/EF << /F {file.Number} 0 R /UF {file.Number} 0 R >>", specification.Body, StringComparison.Ordinal);
        Assert.Contains("/AFRelationship /Alternative", specification.Body, StringComparison.Ordinal);
        Assert.Contains("/Desc <FEFF", specification.Body, StringComparison.Ordinal); // "electrónica" needs UTF-16

        var catalog = probe.Object(1);
        Assert.Contains($"/Names << /EmbeddedFiles << /Names [(factura.xml) {specification.Number} 0 R] >> >>", catalog, StringComparison.Ordinal);
        Assert.Contains($"/AF [{specification.Number} 0 R]", catalog, StringComparison.Ordinal);

        Assert.Equal(("factura.xml", "application/xml", Xml.Length), (attachment.FileName, attachment.MediaType, attachment.Length));
        Assert.Same(attachment, Assert.Single(doc.Attachments));
    }

    [Fact]
    public void Attachments_are_listed_by_name_whatever_the_order_they_were_added_in()
    {
        var doc = NewDocument();
        doc.Attach("zeta.txt", [1], "text/plain");
        doc.Attach("alfa.txt", [2], "text/plain");

        var catalog = new PdfProbe(doc.ToArray()).Object(1);

        Assert.Matches(@"/Names \[\(alfa\.txt\) \d+ 0 R \(zeta\.txt\) \d+ 0 R\]", catalog); // a name tree is sorted
        Assert.Equal(["zeta.txt", "alfa.txt"], doc.Attachments.Select(a => a.FileName));
    }

    [Fact]
    public void A_document_without_attachments_says_nothing_about_them()
    {
        var catalog = new PdfProbe(NewDocument().ToArray()).Object(1);

        Assert.DoesNotContain("/Names", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("/AF", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void The_attached_bytes_are_a_copy_and_the_date_defaults_to_the_documents()
    {
        var doc = NewDocument();
        var data = new byte[] { 1, 2, 3 };
        doc.Attach("a.bin", data, "application/octet-stream");
        doc.Attach("b.bin", data, "application/octet-stream", modified: new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
        data[0] = 99; // changing the caller's array afterwards must not reach the file

        var probe = new PdfProbe(doc.ToArray());
        var files = Objects(probe).Where(o => o.Body.Contains("/Type /EmbeddedFile", StringComparison.Ordinal)).ToList();

        Assert.All(files, f => Assert.Equal([1, 2, 3], probe.StreamData(f.Number)));
        Assert.Contains(files, f => f.Body.Contains("/ModDate (D:20261103172605-05'00')", StringComparison.Ordinal));
        Assert.Contains(files, f => f.Body.Contains("/ModDate (D:20200102030405+00'00')", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "application/xml")]
    [InlineData("carpeta/factura.xml", "application/xml")]
    [InlineData("carpeta\\factura.xml", "application/xml")]
    [InlineData("factura.xml", "xml")]
    [InlineData("factura.xml", "application/")]
    [InlineData("factura.xml", "application/x ml")]
    public void Unusable_names_and_media_types_are_rejected(string fileName, string mediaType)
    {
        Assert.ThrowsAny<ArgumentException>(() => NewDocument().Attach(fileName, Xml, mediaType));
    }

    [Fact]
    public void Two_attachments_cannot_share_a_name()
    {
        var doc = NewDocument();
        doc.Attach("factura.xml", Xml, "application/xml");

        Assert.Throws<ArgumentException>(() => doc.Attach("factura.xml", Xml, "application/xml"));
        Assert.Throws<ArgumentNullException>(() => doc.Attach("otra.xml", null!, "application/xml"));
    }
}
