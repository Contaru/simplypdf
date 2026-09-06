using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SimplyPdf.Internal;

/// <summary>
/// Collects indirect objects and serialises them as a classic PDF 1.4 file:
/// header, body, cross-reference table, trailer.
/// </summary>
internal sealed class PdfWriter
{
    private const string Version = "1.4";

    // Body bytes of each object, indexed by object number - 1. Null = reserved but not yet set.
    private readonly List<byte[]?> _bodies = [];

    /// <summary>Reserves an object number so it can be referenced before its body exists.</summary>
    public PdfObjectRef Reserve()
    {
        _bodies.Add(null);
        return new PdfObjectRef(_bodies.Count);
    }

    public PdfObjectRef Add(PdfDictionary dictionary)
    {
        var reference = Reserve();
        Set(reference, dictionary);
        return reference;
    }

    public PdfObjectRef Add(PdfDictionary dictionary, byte[] streamData, bool compress)
    {
        var reference = Reserve();
        Set(reference, dictionary, streamData, compress);
        return reference;
    }

    public void Set(PdfObjectRef reference, PdfDictionary dictionary)
    {
        var sb = new StringBuilder();
        dictionary.AppendTo(sb);
        _bodies[reference.Number - 1] = Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>Sets a stream object. When <paramref name="compress"/> is true the data is Flate-encoded.</summary>
    public void Set(PdfObjectRef reference, PdfDictionary dictionary, byte[] streamData, bool compress)
    {
        if (compress)
        {
            streamData = Deflate(streamData);
            dictionary["Filter"] = new PdfName("FlateDecode");
        }

        dictionary["Length"] = streamData.Length;

        var sb = new StringBuilder();
        dictionary.AppendTo(sb);
        sb.Append("\nstream\n");
        var head = Encoding.ASCII.GetBytes(sb.ToString());
        var tail = "\nendstream"u8;

        var body = new byte[head.Length + streamData.Length + tail.Length];
        head.CopyTo(body, 0);
        streamData.CopyTo(body, head.Length);
        tail.CopyTo(body.AsSpan(head.Length + streamData.Length));
        _bodies[reference.Number - 1] = body;
    }

    public void WriteTo(Stream output, PdfObjectRef catalog, PdfObjectRef? info)
    {
        ArgumentNullException.ThrowIfNull(output);

        // Assemble in memory so offsets are exact even when the target stream is not seekable.
        using var buffer = new MemoryStream();

        WriteAscii(buffer, "%PDF-" + Version + "\n");
        buffer.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]); // binary comment, as recommended by the spec

        var offsets = new long[_bodies.Count];
        for (var i = 0; i < _bodies.Count; i++)
        {
            var body = _bodies[i] ?? throw new InvalidOperationException($"Object {i + 1} was reserved but never written.");
            offsets[i] = buffer.Position;
            WriteAscii(buffer, (i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            buffer.Write(body);
            WriteAscii(buffer, "\nendobj\n");
        }

        var xrefOffset = buffer.Position;
        WriteAscii(buffer, "xref\n0 " + (_bodies.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n");
        WriteAscii(buffer, "0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            // Each entry is exactly 20 bytes: 10-digit offset, space, 5-digit generation, space, type, space, LF.
            WriteAscii(buffer, offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        var trailer = new PdfDictionary
        {
            ["Size"] = _bodies.Count + 1,
            ["Root"] = catalog,
            ["Info"] = info,
        };
        var sb = new StringBuilder("trailer\n");
        trailer.AppendTo(sb);
        sb.Append("\nstartxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        WriteAscii(buffer, sb.ToString());

        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    private static void WriteAscii(Stream stream, string text)
    {
        stream.Write(Encoding.ASCII.GetBytes(text));
    }

    /// <summary>zlib-wrapped deflate (RFC 1950), which is what /FlateDecode expects.</summary>
    internal static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
