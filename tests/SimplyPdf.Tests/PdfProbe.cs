using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SimplyPdf.Tests;

/// <summary>
/// Just enough PDF parsing to assert on generated files: cross-reference offsets, object bodies
/// and (decompressed) stream contents. Deliberately independent from the library internals.
/// </summary>
internal sealed partial class PdfProbe
{
    private readonly byte[] _bytes;
    private readonly string _latin1;

    public PdfProbe(byte[] bytes)
    {
        _bytes = bytes;
        _latin1 = Encoding.Latin1.GetString(bytes);
    }

    public string Header => _latin1[.._latin1.IndexOf('\n', StringComparison.Ordinal)];

    public bool EndsWithEof => _latin1.TrimEnd('\n', '\r').EndsWith("%%EOF", StringComparison.Ordinal);

    public long StartXref
    {
        get
        {
            var match = StartXrefRegex().Match(_latin1);
            Assert.True(match.Success, "startxref not found");
            return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Object offsets as declared in the xref table, keyed by object number.</summary>
    public Dictionary<int, long> XrefOffsets()
    {
        var xref = _latin1[(int)StartXref..];
        Assert.StartsWith("xref\n", xref, StringComparison.Ordinal);
        var lines = xref.Split('\n');
        var header = lines[1].Split(' ');
        var first = int.Parse(header[0], CultureInfo.InvariantCulture);
        var count = int.Parse(header[1], CultureInfo.InvariantCulture);

        var result = new Dictionary<int, long>();
        for (var i = 0; i < count; i++)
        {
            var entry = lines[2 + i];
            Assert.Equal(19, entry.Length); // 20 bytes including the newline we split on
            if (entry[17] == 'n')
            {
                result[first + i] = long.Parse(entry[..10], CultureInfo.InvariantCulture);
            }
        }

        return result;
    }

    /// <summary>Body of an indirect object (between "n 0 obj" and "endobj"), as Latin-1 text.</summary>
    public string Object(int number)
    {
        var offset = XrefOffsets()[number];
        var marker = number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n";
        Assert.Equal(marker, _latin1.Substring((int)offset, marker.Length));
        var start = (int)offset + marker.Length;
        var end = _latin1.IndexOf("\nendobj", start, StringComparison.Ordinal);
        return _latin1[start..end];
    }

    public string Trailer
    {
        get
        {
            var start = _latin1.LastIndexOf("trailer", StringComparison.Ordinal);
            var end = _latin1.IndexOf("startxref", start, StringComparison.Ordinal);
            return _latin1[start..end];
        }
    }

    /// <summary>Decoded bytes of a stream object, inflating /FlateDecode when present.</summary>
    public byte[] StreamData(int number)
    {
        var offset = (int)XrefOffsets()[number];
        var body = Object(number);
        var streamKeyword = body.IndexOf("stream\n", StringComparison.Ordinal);
        Assert.True(streamKeyword >= 0, $"object {number} is not a stream");

        var lengthMatch = LengthRegex().Match(body[..streamKeyword]);
        Assert.True(lengthMatch.Success, "/Length missing");
        var length = int.Parse(lengthMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        var marker = number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n";
        var dataStart = offset + marker.Length + streamKeyword + "stream\n".Length;
        var data = _bytes.AsSpan(dataStart, length).ToArray();

        if (body[..streamKeyword].Contains("/FlateDecode", StringComparison.Ordinal))
        {
            using var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        return data;
    }

    /// <summary>Content stream of the first page as text.</summary>
    public string FirstPageContent()
    {
        var offsets = XrefOffsets();
        foreach (var number in offsets.Keys.OrderBy(n => n))
        {
            var body = Object(number);
            if (body.Contains("/Type /Page ", StringComparison.Ordinal) || body.Contains("/Type /Page\n", StringComparison.Ordinal))
            {
                var contents = ContentsRegex().Match(body);
                Assert.True(contents.Success, "/Contents missing");
                return Encoding.Latin1.GetString(StreamData(int.Parse(contents.Groups[1].Value, CultureInfo.InvariantCulture)));
            }
        }

        throw new InvalidOperationException("No page object found.");
    }

    [GeneratedRegex(@"startxref\n(\d+)\n%%EOF")]
    private static partial Regex StartXrefRegex();

    [GeneratedRegex(@"/Length (\d+)")]
    private static partial Regex LengthRegex();

    [GeneratedRegex(@"/Contents (\d+) 0 R")]
    private static partial Regex ContentsRegex();
}
