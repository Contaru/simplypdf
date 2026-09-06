using SimplyPdf.Fonts;

namespace SimplyPdf.Text;

/// <summary>Greedy word wrapping over WinAnsi-encoded text.</summary>
internal static class TextLayout
{
    /// <summary>One laid-out line. <paramref name="EndsParagraph"/> is false when the line was produced by wrapping.</summary>
    public readonly record struct Line(byte[] Bytes, bool EndsParagraph);

    /// <summary>
    /// Splits <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/> points.
    /// Explicit newlines always break; wrapping happens at spaces, or inside a word when a single
    /// word is wider than the box. Pass <see cref="double.PositiveInfinity"/> to disable wrapping.
    /// </summary>
    public static List<Line> Wrap(string text, double maxWidth, FontMetricsData font, double size)
    {
        var lines = new List<Line>();
        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace('\t', ' ');

        foreach (var paragraph in normalised.Split('\n'))
        {
            var bytes = WinAnsiEncoding.Encode(paragraph);
            if (!(maxWidth > 0) || double.IsPositiveInfinity(maxWidth) || font.WidthOf(bytes, size) <= maxWidth)
            {
                lines.Add(new Line(bytes, EndsParagraph: true));
                continue;
            }

            WrapParagraph(bytes, maxWidth, font, size, lines);
        }

        return lines;
    }

    private static void WrapParagraph(byte[] bytes, double maxWidth, FontMetricsData font, double size, List<Line> lines)
    {
        var start = 0;
        while (start < bytes.Length)
        {
            var lineWidth = 0.0;
            var lastSpace = -1;
            var i = start;
            while (i < bytes.Length)
            {
                var advance = font.Widths[bytes[i]] * size / 1000.0;
                if (lineWidth + advance > maxWidth && i > start)
                {
                    break;
                }

                lineWidth += advance;
                if (bytes[i] == (byte)' ')
                {
                    lastSpace = i;
                }

                i++;
            }

            int end;
            if (i >= bytes.Length)
            {
                end = bytes.Length; // the remainder fits
            }
            else if (lastSpace >= start)
            {
                end = lastSpace; // break at the last space that fit
                i = lastSpace + 1;
            }
            else
            {
                end = i; // a single word wider than the box: hard break
            }

            var trimmedEnd = end;
            while (trimmedEnd > start && bytes[trimmedEnd - 1] == (byte)' ')
            {
                trimmedEnd--;
            }

            // Leading spaces do not carry over to the next line.
            var next = i;
            while (next < bytes.Length && bytes[next] == (byte)' ')
            {
                next++;
            }

            lines.Add(new Line(bytes[start..trimmedEnd], EndsParagraph: next >= bytes.Length));
            start = next;
        }
    }
}
