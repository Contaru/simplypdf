using System.Buffers.Binary;
using System.Text;

namespace SimplyPdf.Fonts.TrueType;

/// <summary>
/// Reads the parts of a TrueType (glyf-based) font that PDF embedding needs: metrics, the
/// Unicode character map, glyph outlines and their component references. CFF-based OpenType
/// (.otf with a <c>CFF </c> table) and font collections are rejected.
/// </summary>
internal sealed class TrueTypeFont
{
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ushort> _characterMap = [];
    private uint[] _glyphOffsets = [];
    private ushort[] _advanceWidths = [];

    private TrueTypeFont(byte[] data)
    {
        Data = data;
    }

    /// <summary>The complete font file.</summary>
    public byte[] Data { get; }

    public IReadOnlyDictionary<string, (int Offset, int Length)> Tables => _tables;

    public int UnitsPerEm { get; private set; }

    public short XMin { get; private set; }

    public short YMin { get; private set; }

    public short XMax { get; private set; }

    public short YMax { get; private set; }

    /// <summary>hhea ascender, or the OS/2 typographic one when the font asks for it.</summary>
    public short Ascender { get; private set; }

    public short Descender { get; private set; }

    public short LineGap { get; private set; }

    public short CapHeight { get; private set; }

    public short XHeight { get; private set; }

    public ushort NumGlyphs { get; private set; }

    public ushort WeightClass { get; private set; } = 400;

    /// <summary>OS/2 fsType embedding permissions (0 = installable).</summary>
    public ushort EmbeddingFlags { get; private set; }

    public double ItalicAngle { get; private set; }

    public bool IsFixedPitch { get; private set; }

    public string PostScriptName { get; private set; } = "Font";

    public static TrueTypeFont Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 12)
        {
            throw new InvalidDataException("Not a TrueType font: file too short.");
        }

        var version = BinaryPrimitives.ReadUInt32BigEndian(data);
        switch (version)
        {
            case 0x00010000 or 0x74727565: // 1.0 or 'true'
                break;
            case 0x4F54544F: // 'OTTO'
                throw new NotSupportedException("CFF-based OpenType fonts (.otf) are not supported; use a TrueType (.ttf) font with glyf outlines.");
            case 0x74746366: // 'ttcf'
                throw new NotSupportedException("TrueType collections (.ttc) are not supported; extract a single font first.");
            default:
                throw new InvalidDataException("Not a TrueType font: unknown sfnt version.");
        }

        var font = new TrueTypeFont(data);
        font.ReadTableDirectory();
        font.ReadHead();
        font.ReadMaxp();
        font.ReadHhea();
        font.ReadHmtx();
        font.ReadLoca();
        font.ReadCmap();
        font.ReadOs2();
        font.ReadPost();
        font.ReadName();
        return font;
    }

    /// <summary>Glyph index for a Unicode code point, or 0 (.notdef) when the font lacks it.</summary>
    public ushort GlyphFor(int codePoint) => _characterMap.TryGetValue(codePoint, out var glyph) ? glyph : (ushort)0;

    public bool HasGlyph(int codePoint) => _characterMap.ContainsKey(codePoint);

    /// <summary>Advance width of a glyph in font units.</summary>
    public ushort AdvanceWidth(int glyphId)
    {
        if (_advanceWidths.Length == 0)
        {
            return 0;
        }

        return _advanceWidths[Math.Min(glyphId, _advanceWidths.Length - 1)];
    }

    /// <summary>Raw glyf data of a glyph; empty for glyphs without outlines (space) and out-of-range ids.</summary>
    public ReadOnlySpan<byte> GlyphData(int glyphId)
    {
        if (glyphId < 0 || glyphId + 1 >= _glyphOffsets.Length || !_tables.TryGetValue("glyf", out var glyf))
        {
            return [];
        }

        var start = _glyphOffsets[glyphId];
        var end = _glyphOffsets[glyphId + 1];
        if (end <= start || end > (uint)glyf.Length)
        {
            return [];
        }

        return Data.AsSpan(glyf.Offset + (int)start, (int)(end - start));
    }

    /// <summary>Glyphs referenced by a composite glyph (direct components only); empty for simple glyphs.</summary>
    public List<ushort> ComponentGlyphs(int glyphId)
    {
        var components = new List<ushort>();
        var glyph = GlyphData(glyphId);
        if (glyph.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(glyph) >= 0)
        {
            return components; // simple glyph or empty
        }

        var pos = 10;
        while (pos + 4 <= glyph.Length)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(glyph[pos..]);
            components.Add(BinaryPrimitives.ReadUInt16BigEndian(glyph[(pos + 2)..]));

            pos += 4;
            pos += (flags & 0x0001) != 0 ? 4 : 2; // ARG_1_AND_2_ARE_WORDS
            if ((flags & 0x0008) != 0)
            {
                pos += 2; // WE_HAVE_A_SCALE
            }
            else if ((flags & 0x0040) != 0)
            {
                pos += 4; // WE_HAVE_AN_X_AND_Y_SCALE
            }
            else if ((flags & 0x0080) != 0)
            {
                pos += 8; // WE_HAVE_A_TWO_BY_TWO
            }

            if ((flags & 0x0020) == 0)
            {
                break; // no MORE_COMPONENTS
            }
        }

        return components;
    }

    public ReadOnlySpan<byte> Table(string tag) =>
        _tables.TryGetValue(tag, out var table) ? Data.AsSpan(table.Offset, table.Length) : [];

    private void ReadTableDirectory()
    {
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(4));
        var pos = 12;
        for (var i = 0; i < numTables; i++, pos += 16)
        {
            if (pos + 16 > Data.Length)
            {
                throw new InvalidDataException("Malformed TrueType font: truncated table directory.");
            }

            var tag = Encoding.ASCII.GetString(Data, pos, 4);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(pos + 8));
            var length = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(pos + 12));
            if (offset > int.MaxValue || length > int.MaxValue || offset + length > (uint)Data.Length)
            {
                throw new InvalidDataException($"Malformed TrueType font: table '{tag}' points outside the file.");
            }

            _tables[tag] = ((int)offset, (int)length);
        }

        if (_tables.ContainsKey("CFF "))
        {
            throw new NotSupportedException("CFF-based OpenType fonts are not supported; use a TrueType font with glyf outlines.");
        }

        foreach (var required in new[] { "head", "hhea", "hmtx", "maxp", "loca", "glyf", "cmap" })
        {
            if (!_tables.ContainsKey(required))
            {
                throw new InvalidDataException($"Malformed TrueType font: missing '{required}' table.");
            }
        }
    }

    private void ReadHead()
    {
        var head = Require("head", 54);
        UnitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(head[18..]);
        if (UnitsPerEm == 0)
        {
            throw new InvalidDataException("Malformed TrueType font: unitsPerEm is zero.");
        }

        XMin = BinaryPrimitives.ReadInt16BigEndian(head[36..]);
        YMin = BinaryPrimitives.ReadInt16BigEndian(head[38..]);
        XMax = BinaryPrimitives.ReadInt16BigEndian(head[40..]);
        YMax = BinaryPrimitives.ReadInt16BigEndian(head[42..]);
        IndexToLocFormat = BinaryPrimitives.ReadInt16BigEndian(head[50..]);
    }

    private short IndexToLocFormat { get; set; }

    private void ReadMaxp()
    {
        var maxp = Require("maxp", 6);
        NumGlyphs = BinaryPrimitives.ReadUInt16BigEndian(maxp[4..]);
    }

    private void ReadHhea()
    {
        var hhea = Require("hhea", 36);
        Ascender = BinaryPrimitives.ReadInt16BigEndian(hhea[4..]);
        Descender = BinaryPrimitives.ReadInt16BigEndian(hhea[6..]);
        LineGap = BinaryPrimitives.ReadInt16BigEndian(hhea[8..]);
        NumberOfHMetrics = BinaryPrimitives.ReadUInt16BigEndian(hhea[34..]);
    }

    private ushort NumberOfHMetrics { get; set; }

    private void ReadHmtx()
    {
        var hmtx = Table("hmtx");
        var count = Math.Min((int)NumberOfHMetrics, hmtx.Length / 4);
        _advanceWidths = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            _advanceWidths[i] = BinaryPrimitives.ReadUInt16BigEndian(hmtx[(i * 4)..]);
        }
    }

    private void ReadLoca()
    {
        var loca = Table("loca");
        var entries = NumGlyphs + 1;
        _glyphOffsets = new uint[entries];
        if (IndexToLocFormat == 0)
        {
            if (loca.Length < entries * 2)
            {
                throw new InvalidDataException("Malformed TrueType font: loca table too short.");
            }

            for (var i = 0; i < entries; i++)
            {
                _glyphOffsets[i] = (uint)BinaryPrimitives.ReadUInt16BigEndian(loca[(i * 2)..]) * 2;
            }
        }
        else
        {
            if (loca.Length < entries * 4)
            {
                throw new InvalidDataException("Malformed TrueType font: loca table too short.");
            }

            for (var i = 0; i < entries; i++)
            {
                _glyphOffsets[i] = BinaryPrimitives.ReadUInt32BigEndian(loca[(i * 4)..]);
            }
        }
    }

    private void ReadCmap()
    {
        var cmap = Table("cmap");
        if (cmap.Length < 4)
        {
            throw new InvalidDataException("Malformed TrueType font: cmap table too short.");
        }

        var numSubtables = BinaryPrimitives.ReadUInt16BigEndian(cmap[2..]);
        var best = -1;
        var bestScore = -1;
        for (var i = 0; i < numSubtables; i++)
        {
            var record = 4 + i * 8;
            if (record + 8 > cmap.Length)
            {
                break;
            }

            var platform = BinaryPrimitives.ReadUInt16BigEndian(cmap[record..]);
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(cmap[(record + 2)..]);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(cmap[(record + 4)..]);
            if (offset + 4 > (uint)cmap.Length)
            {
                continue;
            }

            var format = BinaryPrimitives.ReadUInt16BigEndian(cmap[(int)offset..]);
            var score = (platform, encoding, format) switch
            {
                (3, 10, 12) => 4, // Windows, full Unicode
                (3, 1, 4) => 3,   // Windows, Unicode BMP — what PDF viewers look for
                (0, _, 12) => 2,  // Unicode platform
                (0, _, 4) => 1,
                _ => -1,
            };

            if (score > bestScore)
            {
                bestScore = score;
                best = (int)offset;
            }
        }

        if (best < 0)
        {
            throw new NotSupportedException("The font has no Unicode cmap subtable (format 4 or 12); symbol fonts are not supported.");
        }

        var subtable = cmap[best..];
        if (BinaryPrimitives.ReadUInt16BigEndian(subtable) == 12)
        {
            ReadCmapFormat12(subtable);
        }
        else
        {
            ReadCmapFormat4(subtable);
        }

        if (_characterMap.Count == 0)
        {
            throw new InvalidDataException("Malformed TrueType font: empty cmap.");
        }
    }

    private void ReadCmapFormat4(ReadOnlySpan<byte> table)
    {
        var segCountX2 = BinaryPrimitives.ReadUInt16BigEndian(table[6..]);
        var segCount = segCountX2 / 2;
        var endCodes = 14;
        var startCodes = endCodes + segCountX2 + 2;
        var idDeltas = startCodes + segCountX2;
        var idRangeOffsets = idDeltas + segCountX2;
        if (idRangeOffsets + segCountX2 > table.Length)
        {
            throw new InvalidDataException("Malformed TrueType font: cmap format 4 subtable truncated.");
        }

        for (var seg = 0; seg < segCount; seg++)
        {
            var endCode = BinaryPrimitives.ReadUInt16BigEndian(table[(endCodes + seg * 2)..]);
            var startCode = BinaryPrimitives.ReadUInt16BigEndian(table[(startCodes + seg * 2)..]);
            var idDelta = BinaryPrimitives.ReadUInt16BigEndian(table[(idDeltas + seg * 2)..]);
            var rangeOffsetPos = idRangeOffsets + seg * 2;
            var idRangeOffset = BinaryPrimitives.ReadUInt16BigEndian(table[rangeOffsetPos..]);
            if (startCode == 0xFFFF)
            {
                continue;
            }

            for (var code = (int)startCode; code <= endCode; code++)
            {
                ushort glyph;
                if (idRangeOffset == 0)
                {
                    glyph = (ushort)(code + idDelta);
                }
                else
                {
                    var glyphPos = rangeOffsetPos + idRangeOffset + (code - startCode) * 2;
                    if (glyphPos + 2 > table.Length)
                    {
                        continue;
                    }

                    glyph = BinaryPrimitives.ReadUInt16BigEndian(table[glyphPos..]);
                    if (glyph != 0)
                    {
                        glyph = (ushort)(glyph + idDelta);
                    }
                }

                if (glyph != 0)
                {
                    _characterMap[code] = glyph;
                }
            }
        }
    }

    private void ReadCmapFormat12(ReadOnlySpan<byte> table)
    {
        var numGroups = BinaryPrimitives.ReadUInt32BigEndian(table[12..]);
        var pos = 16;
        for (uint g = 0; g < numGroups && pos + 12 <= table.Length; g++, pos += 12)
        {
            var start = BinaryPrimitives.ReadUInt32BigEndian(table[pos..]);
            var end = BinaryPrimitives.ReadUInt32BigEndian(table[(pos + 4)..]);
            var startGlyph = BinaryPrimitives.ReadUInt32BigEndian(table[(pos + 8)..]);
            if (end > 0x10FFFF || end < start || end - start > 0xFFFF)
            {
                continue;
            }

            for (var code = start; code <= end; code++)
            {
                var glyph = startGlyph + (code - start);
                if (glyph is > 0 and <= 0xFFFF)
                {
                    _characterMap[(int)code] = (ushort)glyph;
                }
            }
        }
    }

    private void ReadOs2()
    {
        var os2 = Table("OS/2");
        if (os2.Length >= 78)
        {
            var version = BinaryPrimitives.ReadUInt16BigEndian(os2);
            WeightClass = BinaryPrimitives.ReadUInt16BigEndian(os2[4..]);
            EmbeddingFlags = BinaryPrimitives.ReadUInt16BigEndian(os2[8..]);

            // USE_TYPO_METRICS (fsSelection bit 7): the designer wants the typographic metrics used for line layout.
            var fsSelection = BinaryPrimitives.ReadUInt16BigEndian(os2[62..]);
            if ((fsSelection & 0x0080) != 0)
            {
                Ascender = BinaryPrimitives.ReadInt16BigEndian(os2[68..]);
                Descender = BinaryPrimitives.ReadInt16BigEndian(os2[70..]);
                LineGap = BinaryPrimitives.ReadInt16BigEndian(os2[72..]);
            }

            if (version >= 2 && os2.Length >= 90)
            {
                XHeight = BinaryPrimitives.ReadInt16BigEndian(os2[86..]);
                CapHeight = BinaryPrimitives.ReadInt16BigEndian(os2[88..]);
            }
        }

        // Older or minimal fonts: measure the glyphs themselves, then fall back to proportions.
        if (CapHeight <= 0)
        {
            CapHeight = GlyphTop('H') ?? (short)(Ascender * 0.7);
        }

        if (XHeight <= 0)
        {
            XHeight = GlyphTop('x') ?? (short)(Ascender * 0.5);
        }
    }

    /// <summary>yMax of a glyph's bounding box, used to derive cap/x height when OS/2 lacks them.</summary>
    private short? GlyphTop(char c)
    {
        var glyph = GlyphData(GlyphFor(c));
        return glyph.Length >= 10 ? BinaryPrimitives.ReadInt16BigEndian(glyph[8..]) : null;
    }

    private void ReadPost()
    {
        var post = Table("post");
        if (post.Length < 16)
        {
            return;
        }

        ItalicAngle = BinaryPrimitives.ReadInt32BigEndian(post[4..]) / 65536.0;
        IsFixedPitch = BinaryPrimitives.ReadUInt32BigEndian(post[12..]) != 0;
    }

    private void ReadName()
    {
        var name = Table("name");
        if (name.Length < 6)
        {
            return;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(name[2..]);
        var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(name[4..]);
        string? postScript = null;
        string? family = null;
        string? subfamily = null;

        for (var i = 0; i < count; i++)
        {
            var record = 6 + i * 12;
            if (record + 12 > name.Length)
            {
                break;
            }

            var platform = BinaryPrimitives.ReadUInt16BigEndian(name[record..]);
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 2)..]);
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 6)..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 8)..]);
            var offset = stringOffset + BinaryPrimitives.ReadUInt16BigEndian(name[(record + 10)..]);
            if (offset + length > name.Length || nameId is not (1 or 2 or 6))
            {
                continue;
            }

            var bytes = name.Slice(offset, length);
            string? value = (platform, encoding) switch
            {
                (3, 1) or (3, 10) or (0, _) => Encoding.BigEndianUnicode.GetString(bytes),
                (1, 0) => Encoding.Latin1.GetString(bytes),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            switch (nameId)
            {
                case 6:
                    postScript ??= value;
                    break;
                case 1:
                    family ??= value;
                    break;
                case 2:
                    subfamily ??= value;
                    break;
            }
        }

        var candidate = postScript ?? (family is null ? null : subfamily is null ? family : family + "-" + subfamily);
        if (candidate is not null)
        {
            // PostScript names are ASCII without spaces or delimiters.
            var cleaned = new string(candidate.Where(c => c > ' ' && c < 127 && c is not ('/' or '[' or ']' or '(' or ')' or '<' or '>' or '{' or '}' or '%' or '#')).ToArray());
            if (cleaned.Length > 0)
            {
                PostScriptName = cleaned;
            }
        }
    }

    private ReadOnlySpan<byte> Require(string tag, int minimumLength)
    {
        var table = Table(tag);
        if (table.Length < minimumLength)
        {
            throw new InvalidDataException($"Malformed TrueType font: '{tag}' table too short.");
        }

        return table;
    }
}
