using System.Buffers.Binary;
using System.Text;

namespace SimplyPdf.Fonts.TrueType;

/// <summary>
/// Produces a smaller TrueType file that keeps only the glyphs actually used. Glyph ids are
/// preserved (unused glyphs simply become empty), so the original <c>cmap</c> and <c>hmtx</c>
/// stay valid and no glyph renumbering is needed — the approach every PDF viewer accepts.
/// </summary>
internal static class TrueTypeSubsetter
{
    // Tables copied through when present. Hinting tables are kept so glyphs rasterise identically.
    private static readonly string[] CopiedTables = ["cmap", "cvt ", "fpgm", "hhea", "hmtx", "maxp", "name", "OS/2", "prep"];

    public static byte[] Subset(TrueTypeFont font, IEnumerable<ushort> glyphIds)
    {
        var keep = CollectGlyphs(font, glyphIds);

        // glyf + loca (always long offsets, so head.indexToLocFormat becomes 1).
        using var glyf = new MemoryStream();
        var loca = new byte[(font.NumGlyphs + 1) * 4];
        for (var id = 0; id < font.NumGlyphs; id++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(id * 4), (uint)glyf.Length);
            if (keep.Contains((ushort)id))
            {
                var data = font.GlyphData(id);
                glyf.Write(data);
                var padding = (4 - data.Length % 4) % 4;
                for (var p = 0; p < padding; p++)
                {
                    glyf.WriteByte(0);
                }
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(font.NumGlyphs * 4), (uint)glyf.Length);

        var head = font.Table("head").ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0); // checkSumAdjustment, fixed up below
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1); // indexToLocFormat = long

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["glyf"] = glyf.ToArray(),
            ["head"] = head,
            ["loca"] = loca,
            ["post"] = MinimalPost(font.Table("post")),
        };

        foreach (var tag in CopiedTables)
        {
            var table = font.Table(tag);
            if (table.Length > 0)
            {
                tables[tag] = table.ToArray();
            }
        }

        return Assemble(tables);
    }

    /// <summary>The requested glyphs, .notdef, and every component a composite glyph references.</summary>
    private static HashSet<ushort> CollectGlyphs(TrueTypeFont font, IEnumerable<ushort> glyphIds)
    {
        var keep = new HashSet<ushort> { 0 };
        var pending = new Stack<ushort>(glyphIds);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (id >= font.NumGlyphs || !keep.Add(id))
            {
                continue;
            }

            foreach (var component in font.ComponentGlyphs(id))
            {
                pending.Push(component);
            }
        }

        return keep;
    }

    /// <summary>A format 3 post table: the 32-byte header of the original, without glyph names.</summary>
    private static byte[] MinimalPost(ReadOnlySpan<byte> original)
    {
        var post = new byte[32];
        original[..Math.Min(32, original.Length)].CopyTo(post);
        BinaryPrimitives.WriteUInt32BigEndian(post, 0x00030000);
        return post;
    }

    private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
    {
        var numTables = (ushort)tables.Count;
        var entrySelector = (ushort)Math.Floor(Math.Log2(numTables));
        var searchRange = (ushort)((1 << entrySelector) * 16);
        var rangeShift = (ushort)(numTables * 16 - searchRange);

        var directoryLength = 12 + numTables * 16;
        var totalLength = directoryLength;
        foreach (var table in tables.Values)
        {
            totalLength += Align4(table.Length);
        }

        var file = new byte[totalLength];
        BinaryPrimitives.WriteUInt32BigEndian(file, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), numTables);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(6), searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(8), entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(10), rangeShift);

        var record = 12;
        var offset = directoryLength;
        var headOffset = -1;
        foreach (var (tag, table) in tables)
        {
            Encoding.ASCII.GetBytes(tag).CopyTo(file, record);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(record + 4), Checksum(table));
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(record + 12), (uint)table.Length);
            table.CopyTo(file, offset);
            if (tag == "head")
            {
                headOffset = offset;
            }

            record += 16;
            offset += Align4(table.Length);
        }

        // head.checkSumAdjustment makes the whole file sum to the magic constant.
        var adjustment = unchecked(0xB1B0AFBAu - Checksum(file));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(headOffset + 8), adjustment);
        return file;
    }

    private static int Align4(int length) => (length + 3) & ~3;

    /// <summary>Sum of big-endian 32-bit words, zero-padded to a multiple of four bytes.</summary>
    internal static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var whole = data.Length & ~3;
        for (var i = 0; i < whole; i += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data[i..]));
        }

        if (whole < data.Length)
        {
            Span<byte> tail = stackalloc byte[4];
            data[whole..].CopyTo(tail);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(tail));
        }

        return sum;
    }
}
