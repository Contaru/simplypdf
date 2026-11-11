using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace SimplyPdf.Tests;

/// <summary>
/// What a PDF/A-3b file adds and refuses. Whether the result really conforms is a validator's call
/// (veraPDF); these tests pin down the parts it checks so that they do not silently go missing.
/// </summary>
public partial class PdfATests
{
    private static PdfFont Embedded() => PdfFont.FromFile(Path.Combine(AppContext.BaseDirectory, "fonts", "CourierPrime-Regular.ttf"));

    private static PdfDocument Archival()
    {
        var doc = new PdfDocument { Conformance = PdfConformance.PdfA3B };
        doc.Info.Title = "Factura <FSII 1305> & anexos";
        doc.Info.Author = "Almacenes Universal S.A.S.";
        doc.Info.CreationDate = new DateTimeOffset(2026, 11, 3, 17, 26, 5, TimeSpan.FromHours(-5));
        return doc;
    }

    private static List<(int Number, string Body)> Objects(PdfProbe probe) =>
        probe.XrefOffsets().Keys.Select(n => (n, probe.Object(n))).ToList();

    [Fact]
    public void An_archival_file_declares_itself_in_uncompressed_xmp_that_agrees_with_the_info()
    {
        var doc = Archival();
        doc.AddPage().Font(Embedded(), 10).Text("Factura");

        var probe = new PdfProbe(doc.ToArray());
        var metadata = Assert.Single(Objects(probe), o => o.Body.Contains("/Type /Metadata", StringComparison.Ordinal));

        Assert.Contains("/Subtype /XML", metadata.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("/Filter", metadata.Body[..metadata.Body.IndexOf("stream\n", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains($"/Metadata {metadata.Number} 0 R", probe.Object(1), StringComparison.Ordinal);

        var xmp = Encoding.UTF8.GetString(probe.StreamData(metadata.Number));
        Assert.Contains("<pdfaid:part>3</pdfaid:part>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", xmp, StringComparison.Ordinal);
        Assert.Contains("<rdf:li xml:lang=\"x-default\">Factura &lt;FSII 1305&gt; &amp; anexos</rdf:li>", xmp, StringComparison.Ordinal);
        Assert.Contains("<rdf:li>Almacenes Universal S.A.S.</rdf:li>", xmp, StringComparison.Ordinal);
        Assert.Contains("<xmp:CreateDate>2026-11-03T17:26:05-05:00</xmp:CreateDate>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdf:Producer>SimplyPdf</pdf:Producer>", xmp, StringComparison.Ordinal);
        Assert.StartsWith("<?xpacket begin=\"﻿\"", xmp, StringComparison.Ordinal);
        Assert.EndsWith("<?xpacket end=\"w\"?>", xmp, StringComparison.Ordinal);
    }

    [Fact]
    public void An_archival_file_carries_an_srgb_output_intent()
    {
        var doc = Archival();
        doc.AddPage().Rect(10, 10, 50, 50).Fill("#cc0000"); // no text, so no font at all

        var probe = new PdfProbe(doc.ToArray());
        var catalog = probe.Object(1);

        Assert.Contains("/OutputIntents [<< /Type /OutputIntent /S /GTS_PDFA1 /OutputConditionIdentifier (sRGB IEC61966-2.1)", catalog, StringComparison.Ordinal);
        var profileNumber = int.Parse(ProfileRegex().Match(catalog).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("/N 3", probe.Object(profileNumber), StringComparison.Ordinal);

        // An ICC profile says what it is in its header: its own size, a display profile, RGB over XYZ.
        var profile = probe.StreamData(profileNumber);
        Assert.Equal((uint)profile.Length, BinaryPrimitives.ReadUInt32BigEndian(profile));
        Assert.Equal("mntr", Encoding.ASCII.GetString(profile, 12, 4));
        Assert.Equal("RGB ", Encoding.ASCII.GetString(profile, 16, 4));
        Assert.Equal("XYZ ", Encoding.ASCII.GetString(profile, 20, 4));
        Assert.Equal("acsp", Encoding.ASCII.GetString(profile, 36, 4));
        Assert.Equal(9u, BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(128))); // description, copyright, white, 3 primaries, 3 curves
    }

    [Fact]
    public void An_archival_file_is_version_1_7_with_an_identifier_that_follows_its_content()
    {
        var first = Archival();
        first.AddPage().Font(Embedded(), 10).Text("uno");
        var again = Archival();
        again.AddPage().Font(Embedded(), 10).Text("uno");
        var other = Archival();
        other.AddPage().Font(Embedded(), 10).Text("dos");

        var probe = new PdfProbe(first.ToArray());
        var id = IdRegex().Match(probe.Trailer);

        Assert.Equal("%PDF-1.7", probe.Header);
        Assert.True(id.Success, "the trailer needs an /ID");
        Assert.Equal(id.Groups[1].Value, id.Groups[2].Value);
        Assert.Equal(first.ToArray(), again.ToArray()); // same document, same bytes, same identifier
        Assert.NotEqual(id.Groups[1].Value, IdRegex().Match(new PdfProbe(other.ToArray()).Trailer).Groups[1].Value);
    }

    [Fact]
    public void A_plain_file_gets_none_of_it()
    {
        var doc = new PdfDocument();
        doc.AddPage().Text("Helvetica is fine here");

        var probe = new PdfProbe(doc.ToArray());

        Assert.Equal("%PDF-1.4", probe.Header);
        Assert.DoesNotContain("/ID", probe.Trailer, StringComparison.Ordinal);
        Assert.DoesNotContain("/Metadata", probe.Object(1), StringComparison.Ordinal);
        Assert.DoesNotContain("/OutputIntents", probe.Object(1), StringComparison.Ordinal);
    }

    [Fact]
    public void What_the_standard_forbids_is_refused_with_the_reason()
    {
        var standardFont = Archival();
        standardFont.AddPage().Text("drawn in the default Helvetica");
        Assert.Contains("Helvetica is never embedded", Assert.Throws<InvalidOperationException>(() => standardFont.ToArray()).Message, StringComparison.Ordinal);

        var missingGlyph = Archival();
        missingGlyph.AddPage().Font(Embedded(), 10).Text("中文");
        Assert.Contains("no glyph for", Assert.Throws<InvalidOperationException>(() => missingGlyph.ToArray()).Message, StringComparison.Ordinal);

        // A JPEG whose frame header declares four components: CMYK.
        byte[] cmyk =
        [
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x14, 0x08, 0x00, 0x10, 0x00, 0x10, 0x04,
            0x01, 0x11, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00, 0x04, 0x11, 0x00,
            0xFF, 0xDA,
        ];
        var cmykImage = Archival();
        cmykImage.AddPage().Image(PdfImage.FromBytes(cmyk), 0, 0);
        Assert.Contains("CMYK", Assert.Throws<InvalidOperationException>(() => cmykImage.ToArray()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Attachments_travel_in_an_archival_file()
    {
        var doc = Archival();
        doc.AddPage().Font(Embedded(), 10).Text("Factura con su XML");
        doc.Attach("factura.xml", "<Invoice/>"u8.ToArray(), "application/xml", relationship: PdfAttachmentRelationship.Alternative);

        var catalog = new PdfProbe(doc.ToArray()).Object(1);

        Assert.Contains("/EmbeddedFiles", catalog, StringComparison.Ordinal);
        Assert.Contains("/AF [", catalog, StringComparison.Ordinal);
        Assert.Contains("/Metadata", catalog, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"/DestOutputProfile (\d+) 0 R")]
    private static partial Regex ProfileRegex();

    [GeneratedRegex(@"/ID \[<([0-9A-F]{32})> <([0-9A-F]{32})>\]")]
    private static partial Regex IdRegex();
}
