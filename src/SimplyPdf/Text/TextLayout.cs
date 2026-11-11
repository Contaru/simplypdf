using System.Runtime.InteropServices;
using SimplyPdf.Fonts;

namespace SimplyPdf.Text;

/// <summary>Greedy word wrapping over WinAnsi-encoded text.</summary>
internal static class TextLayout
{
    /// <summary>One laid-out line. <paramref name="EndsParagraph"/> is false when the line was produced by wrapping.</summary>
    public readonly record struct Line(byte[] Bytes, bool EndsParagraph);

    /// <summary>A <see cref="TextRun"/> whose font and size are settled; a null colour means the current fill colour.</summary>
    public readonly record struct StyledRun(string Text, PdfFont Font, double Size, PdfColor? Color);

    /// <summary>The part of a line drawn in one style.</summary>
    public readonly record struct Segment(byte[] Bytes, PdfFont Font, double Size, PdfColor? Color)
    {
        /// <summary>Advance width in points.</summary>
        public double Width => Font.WidthOf(Bytes, Size);

        /// <summary>Number of spaces, which is what justification stretches.</summary>
        public int Spaces => Bytes.Count(b => b == (byte)' ');
    }

    /// <summary>
    /// One laid-out line of styled text. <paramref name="Ascent"/>, <paramref name="Descent"/> and
    /// <paramref name="Height"/> are those of its tallest segment, so that segments of different
    /// sizes share a baseline; an empty line takes them from the run it belongs to.
    /// </summary>
    public sealed record StyledLine(IReadOnlyList<Segment> Segments, bool EndsParagraph, double Ascent, double Descent, double Height)
    {
        /// <summary>Advance width of the whole line in points.</summary>
        public double Width => Segments.Sum(s => s.Width);

        /// <summary>Number of spaces in the line.</summary>
        public int Spaces => Segments.Sum(s => s.Spaces);
    }

    /// <summary>
    /// Splits <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/> points.
    /// Explicit newlines always break; wrapping happens at spaces, or inside a word when a single
    /// word is wider than the box. Pass <see cref="double.PositiveInfinity"/> to disable wrapping.
    /// </summary>
    public static List<Line> Wrap(string text, double maxWidth, PdfFont font, double size)
    {
        var lines = new List<Line>();

        foreach (var paragraph in Normalise(text).Split('\n'))
        {
            var bytes = WinAnsiEncoding.Encode(paragraph);
            if (!Wraps(maxWidth) || font.WidthOf(bytes, size) <= maxWidth)
            {
                lines.Add(new Line(bytes, EndsParagraph: true));
                continue;
            }

            var advances = new double[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                advances[i] = font.WidthOfCode(bytes[i]) * size / 1000.0;
            }

            foreach (var (start, end, endsParagraph) in BreakParagraph(bytes, advances, maxWidth))
            {
                lines.Add(new Line(bytes[start..end], endsParagraph));
            }
        }

        return lines;
    }

    /// <summary>
    /// The same wrapping over runs of different styles: the runs are laid end to end, so a line
    /// may break inside any of them and a newline in any of them ends the paragraph. A single run
    /// yields exactly the lines <see cref="Wrap(string, double, PdfFont, double)"/> does.
    /// </summary>
    public static List<StyledLine> Wrap(IReadOnlyList<StyledRun> runs, double maxWidth)
    {
        var lines = new List<StyledLine>();
        if (runs.Count == 0)
        {
            return lines;
        }

        // The paragraph being gathered: its codes, and for each code the run it came from.
        var codes = new List<byte>();
        var owners = new List<int>();

        for (var run = 0; run < runs.Count; run++)
        {
            var paragraphs = Normalise(runs[run].Text).Split('\n');
            for (var p = 0; p < paragraphs.Length; p++)
            {
                if (p > 0)
                {
                    WrapParagraph(runs, codes, owners, run, maxWidth, lines); // a newline closes what was gathered
                    codes.Clear();
                    owners.Clear();
                }

                foreach (var code in WinAnsiEncoding.Encode(paragraphs[p]))
                {
                    codes.Add(code);
                    owners.Add(run);
                }
            }
        }

        WrapParagraph(runs, codes, owners, runs.Count - 1, maxWidth, lines);
        return lines;
    }

    private static void WrapParagraph(
        IReadOnlyList<StyledRun> runs, List<byte> codes, List<int> owners, int emptyLineRun, double maxWidth, List<StyledLine> lines)
    {
        var bytes = CollectionsMarshal.AsSpan(codes);
        var whole = Slice(runs, codes, owners, 0, bytes.Length, emptyLineRun, endsParagraph: true);
        if (!Wraps(maxWidth) || whole.Width <= maxWidth)
        {
            lines.Add(whole);
            return;
        }

        var advances = new double[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var run = runs[owners[i]];
            advances[i] = run.Font.WidthOfCode(bytes[i]) * run.Size / 1000.0;
        }

        foreach (var (start, end, endsParagraph) in BreakParagraph(bytes, advances, maxWidth))
        {
            lines.Add(Slice(runs, codes, owners, start, end, emptyLineRun, endsParagraph));
        }
    }

    /// <summary>Cuts the codes in [start, end) into one segment per stretch that belongs to the same run.</summary>
    private static StyledLine Slice(
        IReadOnlyList<StyledRun> runs, List<byte> codes, List<int> owners, int start, int end, int emptyLineRun, bool endsParagraph)
    {
        var segments = new List<Segment>();
        var ascent = 0.0;
        var descent = 0.0;
        var height = 0.0;

        var from = start;
        while (from < end)
        {
            var owner = owners[from];
            var to = from + 1;
            while (to < end && owners[to] == owner)
            {
                to++;
            }

            var run = runs[owner];
            segments.Add(new Segment(codes.GetRange(from, to - from).ToArray(), run.Font, run.Size, run.Color));
            ascent = Math.Max(ascent, run.Font.Ascent(run.Size));
            descent = Math.Max(descent, run.Font.Descent(run.Size));
            height = Math.Max(height, run.Font.LineHeight(run.Size, includeGap: true));
            from = to;
        }

        if (segments.Count == 0)
        {
            // An empty line still takes the room of the run it sits in.
            var run = runs[start < owners.Count ? owners[start] : emptyLineRun];
            ascent = run.Font.Ascent(run.Size);
            descent = run.Font.Descent(run.Size);
            height = run.Font.LineHeight(run.Size, includeGap: true);
        }

        return new StyledLine(segments, endsParagraph, ascent, descent, height);
    }

    /// <summary>
    /// Breaks one paragraph that does not fit into [start, end) ranges of at most
    /// <paramref name="maxWidth"/>, given the advance of every code. The spaces at a break belong to
    /// neither line.
    /// </summary>
    private static List<(int Start, int End, bool EndsParagraph)> BreakParagraph(ReadOnlySpan<byte> bytes, ReadOnlySpan<double> advances, double maxWidth)
    {
        var ranges = new List<(int Start, int End, bool EndsParagraph)>();
        var start = 0;
        while (start < bytes.Length)
        {
            var lineWidth = 0.0;
            var lastSpace = -1;
            var i = start;
            while (i < bytes.Length)
            {
                var advance = advances[i];
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

            ranges.Add((start, trimmedEnd, next >= bytes.Length));
            start = next;
        }

        return ranges;
    }

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace('\t', ' ');

    /// <summary>False when <paramref name="maxWidth"/> means "never wrap": infinite, zero or negative.</summary>
    private static bool Wraps(double maxWidth) => maxWidth > 0 && !double.IsPositiveInfinity(maxWidth);
}
