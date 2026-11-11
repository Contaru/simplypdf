using System.Globalization;
using System.Text;

namespace SimplyPdf.Internal;

/// <summary>
/// The XMP packet PDF/A requires in the catalog: the claim of conformance itself, and the same
/// title, author, dates and tools the /Info dictionary carries, which the standard wants to agree.
/// </summary>
internal static class XmpMetadata
{
    public static byte[] Build(PdfDocumentInfo info, int part, string conformance)
    {
        var xml = new StringBuilder();

        // The packet header carries a byte-order mark and a fixed id, as the XMP specification says.
        xml.Append("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n")
            .Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n")
            .Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");

        xml.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n")
            .Append("   <pdfaid:part>").Append(part.ToString(CultureInfo.InvariantCulture)).Append("</pdfaid:part>\n")
            .Append("   <pdfaid:conformance>").Append(conformance).Append("</pdfaid:conformance>\n")
            .Append("  </rdf:Description>\n");

        if (info.Title is not null || info.Author is not null || info.Subject is not null)
        {
            xml.Append("  <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n");
            if (info.Title is { } title)
            {
                xml.Append("   <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">").Append(Escape(title)).Append("</rdf:li></rdf:Alt></dc:title>\n");
            }

            if (info.Author is { } author)
            {
                xml.Append("   <dc:creator><rdf:Seq><rdf:li>").Append(Escape(author)).Append("</rdf:li></rdf:Seq></dc:creator>\n");
            }

            if (info.Subject is { } subject)
            {
                xml.Append("   <dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">").Append(Escape(subject)).Append("</rdf:li></rdf:Alt></dc:description>\n");
            }

            xml.Append("  </rdf:Description>\n");
        }

        if (info.Creator is not null || info.CreationDate is not null)
        {
            xml.Append("  <rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">\n");
            if (info.Creator is { } creator)
            {
                xml.Append("   <xmp:CreatorTool>").Append(Escape(creator)).Append("</xmp:CreatorTool>\n");
            }

            if (info.CreationDate is { } created)
            {
                xml.Append("   <xmp:CreateDate>").Append(created.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)).Append("</xmp:CreateDate>\n");
            }

            xml.Append("  </rdf:Description>\n");
        }

        if (info.Producer is not null || info.Keywords is not null)
        {
            xml.Append("  <rdf:Description rdf:about=\"\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">\n");
            if (info.Producer is { } producer)
            {
                xml.Append("   <pdf:Producer>").Append(Escape(producer)).Append("</pdf:Producer>\n");
            }

            if (info.Keywords is { } keywords)
            {
                xml.Append("   <pdf:Keywords>").Append(Escape(keywords)).Append("</pdf:Keywords>\n");
            }

            xml.Append("  </rdf:Description>\n");
        }

        xml.Append(" </rdf:RDF>\n</x:xmpmeta>\n<?xpacket end=\"w\"?>");
        return Encoding.UTF8.GetBytes(xml.ToString());
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
