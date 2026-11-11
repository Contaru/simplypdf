namespace SimplyPdf;

/// <summary>The archival standard a document is written to and declares itself to follow.</summary>
public enum PdfConformance
{
    /// <summary>A plain PDF (default).</summary>
    None,

    /// <summary>
    /// PDF/A-3b (ISO 19005-3, level B): a file meant to look the same decades from now, which may
    /// carry attachments of any kind, such as the XML of an electronic invoice. The document gets
    /// XMP metadata, an sRGB output intent and a file identifier, and saving fails if it uses
    /// anything the standard forbids: a font that is not embedded, a character its font has no
    /// glyph for, or a CMYK image.
    /// </summary>
    PdfA3B,
}
