namespace Lyra.ManagedCodecs.Texture.Blocks.Astc;

/// <summary>
/// The structural decode of an ASTC block mode (spec C.2.10/Table C.2.8): the weight-grid dimensions,
/// weight range, dual-plane flag, and void-extent/error classification. Endpoint/partition parsing is
/// layered on top separately.
/// </summary>
internal readonly struct AstcBlockMode
{
    public bool VoidExtent { get; init; }
    public bool Error { get; init; }
    public int GridX { get; init; }
    public int GridY { get; init; }
    public int GridZ { get; init; } // weight-grid depth; 1 for 2D footprints
    public int WeightRange { get; init; } // max representable weight value
    public bool DualPlane { get; init; }

    /// <summary>Bits the (reversed) weight stream occupies at the top of the block.</summary>
    public int WeightBits { get; init; }
}

/// <summary>
/// ASTC block-mode parsing and weight reconstruction (spec C.2.10–C.2.18). Weights are stored
/// bit-reversed at the top of the block, ISE-encoded; once reversed they decode forward, un-quantize
/// to [0,64], and bilinearly infill from the (possibly smaller) weight grid to one value per texel.
/// Dual-plane blocks interleave two weights per grid point. Mirrors Google's Apache-2.0 astc-codec.
/// </summary>
internal static class AstcWeights
{
    // Weight range (max value) per Table C.2.7, indexed by (H&lt;&lt;3)|R; -1 marks a reserved encoding.
    private static readonly int[] WeightRanges = [-1, -1, 1, 2, 3, 4, 5, 7, -1, -1, 9, 11, 15, 19, 23, 31];

    public static AstcBlockMode DecodeMode(ReadOnlySpan<byte> block, bool is3d = false)
    {
        var low = AstcIntegerSequence.ReadBits(block, 0, 32);

        // Void-extent blocks have the low 9 bits == 0b111111100 (same marker in 2D and 3D; the color
        // still sits at bits 64-127, so the caller's void-extent path is dimension-agnostic).
        if ((low & 0x1FF) == 0x1FC)
        {
            return new AstcBlockMode { VoidExtent = true };
        }

        if (is3d)
        {
            return DecodeMode3D(low);
        }

        int width, height, rExtra;
        var dualPlane = ((low >> 10) & 1) != 0;
        var a6b6 = false;
        var a = (int)((low >> 5) & 3);

        if ((low & 3) != 0)
        {
            var modeBits = (low >> 2) & 3;
            switch (modeBits)
            {
                case 0:
                    width = (int)((low >> 7) & 3) + 4;
                    height = a + 2;
                    break; // B4_A2
                case 1:
                    width = (int)((low >> 7) & 3) + 8;
                    height = a + 2;
                    break; // B8_A2
                case 2:
                    width = a + 2;
                    height = (int)((low >> 7) & 3) + 8;
                    break; // A2_B8
                default:
                    if (((low >> 8) & 1) != 0)
                    {
                        width = (int)((low >> 7) & 1) + 2;
                        height = a + 2; // B2_A2
                    }
                    else
                    {
                        width = a + 2;
                        height = (int)((low >> 7) & 1) + 6; // A2_B6
                    }

                    break;
            }

            rExtra = (int)((low & 3) << 1);
        }
        else
        {
            var modeBits = (low >> 5) & 0xF;
            if ((modeBits & 0xC) == 0)
            {
                if ((low & 0xF) == 0)
                {
                    return new AstcBlockMode { Error = true }; // reserved
                }

                width = 12;
                height = a + 2; // 12_A2
            }
            else if ((modeBits & 0xC) == 4)
            {
                width = a + 2;
                height = 12; // A2_12
            }
            else if (modeBits == 0xC)
            {
                width = 6;
                height = 10;
            }
            else if (modeBits == 0xD)
            {
                width = 10;
                height = 6;
            }
            else if ((modeBits & 0xC) == 8)
            {
                a6b6 = true;
                dualPlane = false;
                width = a + 6;
                height = (int)((low >> 9) & 3) + 6; // A6_B6
            }
            else
            {
                return new AstcBlockMode { Error = true };
            }

            rExtra = (int)(((low >> 2) & 3) << 1);
        }

        var r = (int)((low >> 4) & 1) | rExtra;
        var h = a6b6 ? 0 : (int)((low >> 9) & 1);
        var idx = (h << 3) | r;
        if (idx >= WeightRanges.Length || WeightRanges[idx] < 0)
        {
            return new AstcBlockMode { Error = true };
        }

        var range = WeightRanges[idx];
        var numWeights = width * height * (dualPlane ? 2 : 1);
        if (numWeights > 64)
        {
            return new AstcBlockMode { Error = true };
        }

        AstcIntegerSequence.CountsForRange(range, out var t, out var q, out var b);
        var weightBits = AstcIntegerSequence.BitCount(numWeights, t, q, b);
        if (weightBits is < 24 or > 96)
        {
            return new AstcBlockMode { Error = true };
        }

        return new AstcBlockMode
        {
            GridX = width,
            GridY = height,
            GridZ = 1,
            WeightRange = range,
            DualPlane = dualPlane,
            WeightBits = weightBits,
        };
    }

    /// <summary>
    /// The 3D block-mode parse (spec Table C.2.9). Same weight-range/bit-budget rules as 2D, but the
    /// size bits encode a 3D weight grid. Mirrors Arm astcenc's <c>decode_block_mode_3d</c>.
    /// </summary>
    private static AstcBlockMode DecodeMode3D(uint low)
    {
        var dualPlane = ((low >> 10) & 1) != 0; // D
        var h = (int)((low >> 9) & 1); // H (high-precision range)
        var a = (int)((low >> 5) & 3);
        int x, y, z, rExtra;

        if ((low & 3) != 0)
        {
            rExtra = (int)((low & 3) << 1);
            var b = (int)((low >> 7) & 3);
            var c = (int)((low >> 2) & 3);
            x = a + 2;
            y = b + 2;
            z = c + 2;
        }
        else
        {
            var m = (int)((low >> 2) & 3);
            if (m == 0)
            {
                return new AstcBlockMode { Error = true }; // reserved
            }

            rExtra = m << 1;
            var b = (int)((low >> 9) & 3);
            var sel = (int)((low >> 7) & 3);
            if (sel != 3)
            {
                dualPlane = false; // D and H are not available in these encodings
                h = 0;
            }

            switch (sel)
            {
                case 0:
                    x = 6;
                    y = b + 2;
                    z = a + 2;
                    break;
                case 1:
                    x = a + 2;
                    y = 6;
                    z = b + 2;
                    break;
                case 2:
                    x = a + 2;
                    y = b + 2;
                    z = 6;
                    break;
                default: // 3: two axes are 2, one is 6
                    x = 2;
                    y = 2;
                    z = 2;
                    switch ((low >> 5) & 3)
                    {
                        case 0: x = 6; break;
                        case 1: y = 6; break;
                        case 2: z = 6; break;
                        default: return new AstcBlockMode { Error = true };
                    }

                    break;
            }
        }

        var r = (int)((low >> 4) & 1) | rExtra;
        var idx = (h << 3) | r;
        if (idx >= WeightRanges.Length || WeightRanges[idx] < 0)
        {
            return new AstcBlockMode { Error = true };
        }

        var range = WeightRanges[idx];
        var numWeights = x * y * z * (dualPlane ? 2 : 1);
        if (numWeights > 64)
        {
            return new AstcBlockMode { Error = true };
        }

        AstcIntegerSequence.CountsForRange(range, out var t, out var q, out var b2);
        var weightBits = AstcIntegerSequence.BitCount(numWeights, t, q, b2);
        if (weightBits is < 24 or > 96)
            return new AstcBlockMode { Error = true };

        return new AstcBlockMode
        {
            GridX = x,
            GridY = y,
            GridZ = z,
            WeightRange = range,
            DualPlane = dualPlane,
            WeightBits = weightBits,
        };
    }

    /// <summary>
    /// Decodes the weight grid and infills it to one weight per texel (range [0,64]). Fills
    /// <paramref name="primary"/> (length <c>blockW*blockH</c>); for dual-plane blocks also fills
    /// <paramref name="secondary"/> with the second plane.
    /// </summary>
    public static void DecodeWeights(ReadOnlySpan<byte> block, in AstcBlockMode mode, int blockW, int blockH, int blockD, Span<int> primary, Span<int> secondary)
    {
        AstcIntegerSequence.CountsForRange(mode.WeightRange, out var t, out var q, out var b);
        var gridSize = mode.GridX * mode.GridY * mode.GridZ;
        var freq = mode.DualPlane ? 2 : 1;
        var numWeights = gridSize * freq;

        Span<byte> reversed = stackalloc byte[16];
        Reverse128(block, reversed);

        Span<int> raw = stackalloc int[64];
        AstcIntegerSequence.Decode(reversed, 0, numWeights, t, q, b, raw);

        Span<int> grid = stackalloc int[64];
        for (var i = 0; i < gridSize; i++) 
            grid[i] = AstcQuantization.UnquantizeWeight(raw[i * freq], mode.WeightRange);

        InfillAny(grid[..gridSize], mode, blockW, blockH, blockD, primary);

        if (mode.DualPlane)
        {
            for (var i = 0; i < gridSize; i++) 
                grid[i] = AstcQuantization.UnquantizeWeight(raw[(i * freq) + 1], mode.WeightRange);

            InfillAny(grid[..gridSize], mode, blockW, blockH, blockD, secondary);
        }
    }

    /// <summary>Dispatches to the 2D (bilinear) or 3D (tetrahedral) weight infill by footprint depth.</summary>
    private static void InfillAny(ReadOnlySpan<int> grid, in AstcBlockMode mode, int blockW, int blockH, int blockD, Span<int> dst)
    {
        if (blockD > 1)
            Infill3D(grid, mode.GridX, mode.GridY, mode.GridZ, blockW, blockH, blockD, dst);
        else
            Infill(grid, mode.GridX, mode.GridY, blockW, blockH, dst);
    }

    /// <summary>Bilinearly interpolates a <paramref name="gridX"/>×<paramref name="gridY"/> weight grid to one weight per texel (spec C.2.18).</summary>
    private static void Infill(ReadOnlySpan<int> grid, int gridX, int gridY, int blockW, int blockH, Span<int> dst)
    {
        var ds = ScaleFactor(blockW);
        var dt = ScaleFactor(blockH);
        var gridSize = gridX * gridY;

        for (var ty = 0; ty < blockH; ty++)
        for (var sx = 0; sx < blockW; sx++)
        {
            var cs = ds * sx;
            var ct = dt * ty;
            var gs = ((cs * (gridX - 1)) + 32) >> 6;
            var gt = ((ct * (gridY - 1)) + 32) >> 6;

            var js = gs >> 4;
            var jt = gt >> 4;
            var fs = gs & 0xF;
            var ft = gt & 0xF;

            var w3 = ((fs * ft) + 8) >> 4;
            var w2 = ft - w3;
            var w1 = fs - w3;
            var w0 = 16 - fs - ft + w3;

            var p0 = js + (gridX * jt);
            var p1 = p0 + 1;
            var p2 = js + (gridX * (jt + 1));
            var p3 = p2 + 1;

            var weight = 0;
            if (p0 < gridSize) 
                weight += grid[p0] * w0;
            
            if (p1 < gridSize) 
                weight += grid[p1] * w1;
            
            if (p2 < gridSize) 
                weight += grid[p2] * w2;
            
            if (p3 < gridSize) 
                weight += grid[p3] * w3;

            dst[(ty * blockW) + sx] = (weight + 8) >> 4;
        }
    }

    /// <summary>
    /// Trilinear weight infill for a 3D footprint via tetrahedral (simplex) interpolation: each texel
    /// blends the four corners of the simplex its fractional grid position falls in (spec C.2.18, 3D
    /// path). Mirrors Arm astcenc's <c>init_decimation_info_3d</c>. The four blend weights sum to 16.
    /// </summary>
    private static void Infill3D(ReadOnlySpan<int> grid, int gridX, int gridY, int gridZ, int blockW, int blockH, int blockD, Span<int> dst)
    {
        var dsX = ScaleFactor(blockW);
        var dsY = ScaleFactor(blockH);
        var dsZ = ScaleFactor(blockD);
        var gridSize = gridX * gridY * gridZ;
        var n = gridX; // +1 along Y
        var nm = gridX * gridY; // +1 along Z

        for (var z = 0; z < blockD; z++)
        for (var y = 0; y < blockH; y++)
        for (var x = 0; x < blockW; x++)
        {
            var xw = ((dsX * x * (gridX - 1)) + 32) >> 6;
            var yw = ((dsY * y * (gridY - 1)) + 32) >> 6;
            var zw = ((dsZ * z * (gridZ - 1)) + 32) >> 6;

            var xi = xw >> 4;
            var yi = yw >> 4;
            var zi = zw >> 4;
            var fs = xw & 0xF;
            var ft = yw & 0xF;
            var fp = zw & 0xF;

            // Pick the simplex (one of six tetrahedra) by ordering the fractional coordinates.
            var cas = (fs > ft ? 4 : 0) + (ft > fp ? 2 : 0) + (fs > fp ? 1 : 0);
            int s1, s2, w0, w1, w2, w3;
            switch (cas)
            {
                case 7:
                    s1 = 1;
                    s2 = n;
                    w0 = 16 - fs;
                    w1 = fs - ft;
                    w2 = ft - fp;
                    w3 = fp;
                    break;
                case 3:
                    s1 = n;
                    s2 = 1;
                    w0 = 16 - ft;
                    w1 = ft - fs;
                    w2 = fs - fp;
                    w3 = fp;
                    break;
                case 5:
                    s1 = 1;
                    s2 = nm;
                    w0 = 16 - fs;
                    w1 = fs - fp;
                    w2 = fp - ft;
                    w3 = ft;
                    break;
                case 4:
                    s1 = nm;
                    s2 = 1;
                    w0 = 16 - fp;
                    w1 = fp - fs;
                    w2 = fs - ft;
                    w3 = ft;
                    break;
                case 2:
                    s1 = n;
                    s2 = nm;
                    w0 = 16 - ft;
                    w1 = ft - fp;
                    w2 = fp - fs;
                    w3 = fs;
                    break;
                default:
                    s1 = nm;
                    s2 = n;
                    w0 = 16 - fp;
                    w1 = fp - ft;
                    w2 = ft - fs;
                    w3 = fs;
                    break; // 0, 1, 6
            }

            var q0 = ((zi * gridY) + yi) * gridX + xi;
            var q1 = q0 + s1;
            var q2 = q1 + s2;
            var q3 = (((zi + 1) * gridY) + (yi + 1)) * gridX + (xi + 1);

            // Out-of-range corners only ever carry a zero blend weight, so guarding the index is safe.
            var weight = 0;
            if (w0 != 0 && (uint)q0 < (uint)gridSize) 
                weight += grid[q0] * w0;
            
            if (w1 != 0 && (uint)q1 < (uint)gridSize) 
                weight += grid[q1] * w1;
            
            if (w2 != 0 && (uint)q2 < (uint)gridSize) 
                weight += grid[q2] * w2;
            
            if (w3 != 0 && (uint)q3 < (uint)gridSize) 
                weight += grid[q3] * w3;

            dst[((z * blockH) + y) * blockW + x] = (weight + 8) >> 4;
        }
    }

    /// <summary>The per-axis grid scale factor D (spec C.2.18).</summary>
    internal static int ScaleFactor(int blockDim) => (int)((1024f + (blockDim >> 1)) / (blockDim - 1));

    /// <summary>Reverses all 128 bits of the block (byte order reversed, each byte bit-reversed).</summary>
    internal static void Reverse128(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        for (var i = 0; i < 16; i++) 
            dst[i] = ReverseByte(src[15 - i]);
    }

    private static byte ReverseByte(byte v)
    {
        var r = 0;
        for (var i = 0; i < 8; i++) 
            r = (r << 1) | ((v >> i) & 1);

        return (byte)r;
    }
}