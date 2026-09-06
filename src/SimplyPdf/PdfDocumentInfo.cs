namespace SimplyPdf;

/// <summary>Document metadata written to the PDF /Info dictionary.</summary>
public sealed class PdfDocumentInfo
{
    /// <summary>Document title.</summary>
    public string? Title { get; set; }

    /// <summary>Document author.</summary>
    public string? Author { get; set; }

    /// <summary>Document subject.</summary>
    public string? Subject { get; set; }

    /// <summary>Keywords, typically comma separated.</summary>
    public string? Keywords { get; set; }

    /// <summary>Application that created the original content. Defaults to "SimplyPdf".</summary>
    public string? Creator { get; set; } = "SimplyPdf";

    /// <summary>Application that produced the PDF. Defaults to "SimplyPdf".</summary>
    public string? Producer { get; set; } = "SimplyPdf";

    /// <summary>
    /// Creation timestamp. Defaults to the moment the document was instantiated; set it to
    /// <see langword="null"/> to omit it when byte-for-byte reproducible output matters.
    /// </summary>
    public DateTimeOffset? CreationDate { get; set; } = DateTimeOffset.Now;
}
