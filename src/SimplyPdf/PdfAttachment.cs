namespace SimplyPdf;

/// <summary>
/// How an attached file relates to the document that carries it. Archiving and e-invoicing
/// readers (PDF/A-3, Factur-X, ZUGFeRD) look at it to tell the machine-readable original from a
/// mere enclosure.
/// </summary>
public enum PdfAttachmentRelationship
{
    /// <summary>Nothing is said about the relationship (default).</summary>
    Unspecified,

    /// <summary>The file is what the document was made from.</summary>
    Source,

    /// <summary>The file holds the data the document shows, such as the figures behind a table.</summary>
    Data,

    /// <summary>The file is another form of the same content: the XML of an invoice next to its printed page.</summary>
    Alternative,

    /// <summary>The file adds to the document without standing for it.</summary>
    Supplement,
}

/// <summary>
/// A file carried inside a PDF, added with <see cref="PdfDocument.Attach"/>. Viewers list it in
/// their attachments panel and let the reader save it.
/// </summary>
public sealed class PdfAttachment
{
    internal PdfAttachment(
        string fileName, byte[] data, string mediaType, string? description, PdfAttachmentRelationship relationship, DateTimeOffset? modified)
    {
        FileName = fileName;
        Data = data;
        MediaType = mediaType;
        Description = description;
        Relationship = relationship;
        Modified = modified;
    }

    /// <summary>The name the file is listed and saved under.</summary>
    public string FileName { get; }

    /// <summary>The MIME type of the file, such as <c>application/xml</c>.</summary>
    public string MediaType { get; }

    /// <summary>A line of text viewers show next to the file, or <see langword="null"/>.</summary>
    public string? Description { get; }

    /// <summary>How the file relates to the document.</summary>
    public PdfAttachmentRelationship Relationship { get; }

    /// <summary>When the file was last changed, or <see langword="null"/> to use the document's creation date.</summary>
    public DateTimeOffset? Modified { get; }

    /// <summary>Size of the file in bytes.</summary>
    public int Length => Data.Length;

    internal byte[] Data { get; }
}
