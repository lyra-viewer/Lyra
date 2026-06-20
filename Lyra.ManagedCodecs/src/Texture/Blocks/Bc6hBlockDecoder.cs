namespace Lyra.ManagedCodecs.Texture.Blocks;

/// <summary>
/// Decodes a 16-byte BC6H (BPTC float) block to 16 RGBA pixels of linear HDR float (alpha = 1).
/// BC6H has 14 modes with heavily scrambled per-mode endpoint bit layouts, delta-coded endpoints,
/// and signed/unsigned variants. The mode-unpacking switch and partition table are ported from the
/// public-domain bcdec.h reference (https://github.com/iOrange/bcdec); endpoints are unquantized to
/// half-float bit patterns and expanded to float32.
/// </summary>
internal static class Bc6hBlockDecoder
{
    // Per-mode endpoint precision: [0]=W (base) bits, [1..3]=dR/dG/dB (delta or explicit) bits.
    private static readonly int[][] ActualBits =
    [
        [10, 7, 11, 11, 11, 9, 8, 8, 8, 6, 10, 11, 12, 16], // W
        [5, 6, 5, 4, 4, 5, 6, 5, 5, 6, 10, 9, 8, 4],        // dR
        [5, 6, 4, 5, 4, 5, 5, 6, 5, 6, 10, 9, 8, 4],        // dG
        [5, 6, 4, 4, 5, 5, 5, 5, 6, 6, 10, 9, 8, 4],        // dB
    ];

    private static readonly int[] Weight3 = [0, 9, 18, 27, 37, 46, 55, 64];
    private static readonly int[] Weight4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    public static void DecodeBc6h(ReadOnlySpan<byte> block, Span<float> dst, bool isSigned)
    {
        var bits = new BitReader(block);
        Span<int> r = stackalloc int[4];
        Span<int> g = stackalloc int[4];
        Span<int> b = stackalloc int[4];

        var mode = bits.ReadBits(2);
        if (mode > 1)
        {
            mode |= bits.ReadBits(3) << 2;
        }

        var partition = 0;

        switch (mode) {
            /* mode 1 */
            case 0b00: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 75 bits (10.555, 10.555, 10.555) */
                g[2] |= bits.ReadBit() << 4;       /* gy[4]   */
                b[2] |= bits.ReadBit() << 4;       /* by[4]   */
                b[3] |= bits.ReadBit() << 4;       /* bz[4]   */
                r[0] |= bits.ReadBits(10);       /* rw[9:0] */
                g[0] |= bits.ReadBits(10);       /* gw[9:0] */
                b[0] |= bits.ReadBits(10);       /* bw[9:0] */
                r[1] |= bits.ReadBits(5);        /* rx[4:0] */
                g[3] |= bits.ReadBit() << 4;       /* gz[4]   */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(5);        /* gx[4:0] */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                g[3] |= bits.ReadBits(4);        /* gz[3:0] */
                b[1] |= bits.ReadBits(5);        /* bx[4:0] */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(5);        /* ry[4:0] */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                r[3] |= bits.ReadBits(5);        /* rz[4:0] */
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 0;
            } break;

            /* mode 2 */
            case 0b01: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 75 bits (7666, 7666, 7666) */
                g[2] |= bits.ReadBit() << 5;       /* gy[5]   */
                g[3] |= bits.ReadBit() << 4;       /* gz[4]   */
                g[3] |= bits.ReadBit() << 5;       /* gz[5]   */
                r[0] |= bits.ReadBits(7);        /* rw[6:0] */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[2] |= bits.ReadBit() << 4;       /* by[4]   */
                g[0] |= bits.ReadBits(7);        /* gw[6:0] */
                b[2] |= bits.ReadBit() << 5;       /* by[5]   */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                g[2] |= bits.ReadBit() << 4;       /* gy[4]   */
                b[0] |= bits.ReadBits(7);        /* bw[6:0] */
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                b[3] |= bits.ReadBit() << 5;       /* bz[5]   */
                b[3] |= bits.ReadBit() << 4;       /* bz[4]   */
                r[1] |= bits.ReadBits(6);        /* rx[5:0] */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(6);        /* gx[5:0] */
                g[3] |= bits.ReadBits(4);        /* gz[3:0] */
                b[1] |= bits.ReadBits(6);        /* bx[5:0] */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(6);        /* ry[5:0] */
                r[3] |= bits.ReadBits(6);        /* rz[5:0] */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 1;
            } break;

            /* mode 3 */
            case 0b00010: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 72 bits (11.555, 11.444, 11.444) */
                r[0] |= bits.ReadBits(10);       /* rw[9:0] */
                g[0] |= bits.ReadBits(10);       /* gw[9:0] */
                b[0] |= bits.ReadBits(10);       /* bw[9:0] */
                r[1] |= bits.ReadBits(5);        /* rx[4:0] */
                r[0] |= bits.ReadBit() << 10;      /* rw[10]  */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(4);        /* gx[3:0] */
                g[0] |= bits.ReadBit() << 10;      /* gw[10]  */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                g[3] |= bits.ReadBits(4);        /* gz[3:0] */
                b[1] |= bits.ReadBits(4);        /* bx[3:0] */
                b[0] |= bits.ReadBit() << 10;      /* bw[10]  */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(5);        /* ry[4:0] */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                r[3] |= bits.ReadBits(5);        /* rz[4:0] */
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 2;
            } break;

            /* mode 4 */
            case 0b00110: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 72 bits (11.444, 11.555, 11.444) */
                r[0] |= bits.ReadBits(10);       /* rw[9:0] */
                g[0] |= bits.ReadBits(10);       /* gw[9:0] */
                b[0] |= bits.ReadBits(10);       /* bw[9:0] */
                r[1] |= bits.ReadBits(4);        /* rx[3:0] */
                r[0] |= bits.ReadBit() << 10;      /* rw[10]  */
                g[3] |= bits.ReadBit() << 4;       /* gz[4]   */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(5);        /* gx[4:0] */
                g[0] |= bits.ReadBit() << 10;      /* gw[10]  */
                g[3] |= bits.ReadBits(4);        /* gz[3:0] */
                b[1] |= bits.ReadBits(4);        /* bx[3:0] */
                b[0] |= bits.ReadBit() << 10;      /* bw[10]  */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(4);        /* ry[3:0] */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                r[3] |= bits.ReadBits(4);        /* rz[3:0] */
                g[2] |= bits.ReadBit() << 4;       /* gy[4]   */
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 3;
            } break;

            /* mode 5 */
            case 0b01010: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 72 bits (11.444, 11.444, 11.555) */
                r[0] |= bits.ReadBits(10);       /* rw[9:0] */
                g[0] |= bits.ReadBits(10);       /* gw[9:0] */
                b[0] |= bits.ReadBits(10);       /* bw[9:0] */
                r[1] |= bits.ReadBits(4);        /* rx[3:0] */
                r[0] |= bits.ReadBit() << 10;      /* rw[10]  */
                b[2] |= bits.ReadBit() << 4;       /* by[4]   */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(4);        /* gx[3:0] */
                g[0] |= bits.ReadBit() << 10;      /* gw[10]  */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                g[3] |= bits.ReadBits(4);        /* gz[3:0] */
                b[1] |= bits.ReadBits(5);        /* bx[4:0] */
                b[0] |= bits.ReadBit() << 10;      /* bw[10]  */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(4);        /* ry[3:0] */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                r[3] |= bits.ReadBits(4);        /* rz[3:0] */
                b[3] |= bits.ReadBit() << 4;       /* bz[4]   */ 
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 4;
            } break;

            /* mode 6 */
            case 0b01110: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 72 bits (9555, 9555, 9555) */
                r[0] |= bits.ReadBits(9);        /* rw[8:0] */
                b[2] |= bits.ReadBit() << 4;       /* by[4]   */
                g[0] |= bits.ReadBits(9);        /* gw[8:0] */
                g[2] |= bits.ReadBit() << 4;       /* gy[4]   */
                b[0] |= bits.ReadBits(9);        /* bw[8:0] */
                b[3] |= bits.ReadBit() << 4;       /* bz[4]   */
                r[1] |= bits.ReadBits(5);        /* rx[4:0] */
                g[3] |= bits.ReadBit() << 4;       /* gz[4]   */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(5);        /* gx[4:0] */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                g[3] |= bits.ReadBits(4);        /* gx[3:0] */
                b[1] |= bits.ReadBits(5);        /* bx[4:0] */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(5);        /* ry[4:0] */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                r[3] |= bits.ReadBits(5);        /* rz[4:0] */
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 5;
            } break;

            /* mode 7 */
            case 0b10010: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 72 bits (8666, 8555, 8555) */
                r[0] |= bits.ReadBits(8);        /* rw[7:0] */
                g[3] |= bits.ReadBit() << 4;       /* gz[4]   */
                b[2] |= bits.ReadBit() << 4;       /* by[4]   */
                g[0] |= bits.ReadBits(8);        /* gw[7:0] */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                g[2] |= bits.ReadBit() << 4;       /* gy[4]   */
                b[0] |= bits.ReadBits(8);        /* bw[7:0] */
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                b[3] |= bits.ReadBit() << 4;       /* bz[4]   */
                r[1] |= bits.ReadBits(6);        /* rx[5:0] */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(5);        /* gx[4:0] */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                g[3] |= bits.ReadBits(4);        /* gz[3:0] */
                b[1] |= bits.ReadBits(5);        /* bx[4:0] */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(6);        /* ry[5:0] */
                r[3] |= bits.ReadBits(6);        /* rz[5:0] */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 6;
            } break;

            /* mode 8 */
            case 0b10110: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 72 bits (8555, 8666, 8555) */
                r[0] |= bits.ReadBits(8);        /* rw[7:0] */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                b[2] |= bits.ReadBit() << 4;       /* by[4]   */
                g[0] |= bits.ReadBits(8);        /* gw[7:0] */
                g[2] |= bits.ReadBit() << 5;       /* gy[5]   */
                g[2] |= bits.ReadBit() << 4;       /* gy[4]   */
                b[0] |= bits.ReadBits(8);        /* bw[7:0] */
                g[3] |= bits.ReadBit() << 5;       /* gz[5]   */
                b[3] |= bits.ReadBit() << 4;       /* bz[4]   */
                r[1] |= bits.ReadBits(5);        /* rx[4:0] */
                g[3] |= bits.ReadBit() << 4;       /* gz[4]   */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(6);        /* gx[5:0] */
                g[3] |= bits.ReadBits(4);        /* zx[3:0] */
                b[1] |= bits.ReadBits(5);        /* bx[4:0] */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(5);        /* ry[4:0] */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                r[3] |= bits.ReadBits(5);        /* rz[4:0] */
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 7;
            } break;

            /* mode 9 */
            case 0b11010: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 72 bits (8555, 8555, 8666) */
                r[0] |= bits.ReadBits(8);        /* rw[7:0] */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[2] |= bits.ReadBit() << 4;       /* by[4]   */
                g[0] |= bits.ReadBits(8);        /* gw[7:0] */
                b[2] |= bits.ReadBit() << 5;       /* by[5]   */
                g[2] |= bits.ReadBit() << 4;       /* gy[4]   */
                b[0] |= bits.ReadBits(8);        /* bw[7:0] */
                b[3] |= bits.ReadBit() << 5;       /* bz[5]   */
                b[3] |= bits.ReadBit() << 4;       /* bz[4]   */
                r[1] |= bits.ReadBits(5);        /* bw[4:0] */
                g[3] |= bits.ReadBit() << 4;       /* gz[4]   */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(5);        /* gx[4:0] */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                g[3] |= bits.ReadBits(4);        /* gz[3:0] */
                b[1] |= bits.ReadBits(6);        /* bx[5:0] */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(5);        /* ry[4:0] */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                r[3] |= bits.ReadBits(5);        /* rz[4:0] */
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 8;
            } break;

            /* mode 10 */
            case 0b11110: {
                /* Partitition indices: 46 bits
                   Partition: 5 bits
                   Color Endpoints: 72 bits (6666, 6666, 6666) */
                r[0] |= bits.ReadBits(6);        /* rw[5:0] */
                g[3] |= bits.ReadBit() << 4;       /* gz[4]   */
                b[3] |= bits.ReadBit();            /* bz[0]   */
                b[3] |= bits.ReadBit() << 1;       /* bz[1]   */
                b[2] |= bits.ReadBit() << 4;       /* by[4]   */
                g[0] |= bits.ReadBits(6);        /* gw[5:0] */
                g[2] |= bits.ReadBit() << 5;       /* gy[5]   */
                b[2] |= bits.ReadBit() << 5;       /* by[5]   */
                b[3] |= bits.ReadBit() << 2;       /* bz[2]   */
                g[2] |= bits.ReadBit() << 4;       /* gy[4]   */
                b[0] |= bits.ReadBits(6);        /* bw[5:0] */
                g[3] |= bits.ReadBit() << 5;       /* gz[5]   */
                b[3] |= bits.ReadBit() << 3;       /* bz[3]   */
                b[3] |= bits.ReadBit() << 5;       /* bz[5]   */
                b[3] |= bits.ReadBit() << 4;       /* bz[4]   */
                r[1] |= bits.ReadBits(6);        /* rx[5:0] */
                g[2] |= bits.ReadBits(4);        /* gy[3:0] */
                g[1] |= bits.ReadBits(6);        /* gx[5:0] */
                g[3] |= bits.ReadBits(4);        /* gz[3:0] */
                b[1] |= bits.ReadBits(6);        /* bx[5:0] */
                b[2] |= bits.ReadBits(4);        /* by[3:0] */
                r[2] |= bits.ReadBits(6);        /* ry[5:0] */
                r[3] |= bits.ReadBits(6);        /* rz[5:0] */
                partition = bits.ReadBits(5);    /* d[4:0]  */
                mode = 9;
            } break;

            /* mode 11 */
            case 0b00011: {
                /* Partitition indices: 63 bits
                   Partition: 0 bits
                   Color Endpoints: 60 bits (10.10, 10.10, 10.10) */
                r[0] |= bits.ReadBits(10);       /* rw[9:0] */
                g[0] |= bits.ReadBits(10);       /* gw[9:0] */
                b[0] |= bits.ReadBits(10);       /* bw[9:0] */
                r[1] |= bits.ReadBits(10);       /* rx[9:0] */
                g[1] |= bits.ReadBits(10);       /* gx[9:0] */
                b[1] |= bits.ReadBits(10);       /* bx[9:0] */
                mode = 10;
            } break;

            /* mode 12 */
            case 0b00111: {
                /* Partitition indices: 63 bits
                   Partition: 0 bits
                   Color Endpoints: 60 bits (11.9, 11.9, 11.9) */
                r[0] |= bits.ReadBits(10);       /* rw[9:0] */
                g[0] |= bits.ReadBits(10);       /* gw[9:0] */
                b[0] |= bits.ReadBits(10);       /* bw[9:0] */
                r[1] |= bits.ReadBits(9);        /* rx[8:0] */
                r[0] |= bits.ReadBit() << 10;      /* rw[10]  */
                g[1] |= bits.ReadBits(9);        /* gx[8:0] */
                g[0] |= bits.ReadBit() << 10;      /* gw[10]  */
                b[1] |= bits.ReadBits(9);        /* bx[8:0] */
                b[0] |= bits.ReadBit() << 10;      /* bw[10]  */
                mode = 11;
            } break;

            /* mode 13 */
            case 0b01011: {
                /* Partitition indices: 63 bits
                   Partition: 0 bits
                   Color Endpoints: 60 bits (12.8, 12.8, 12.8) */
                r[0] |= bits.ReadBits(10);       /* rw[9:0] */
                g[0] |= bits.ReadBits(10);       /* gw[9:0] */
                b[0] |= bits.ReadBits(10);       /* bw[9:0] */
                r[1] |= bits.ReadBits(8);        /* rx[7:0] */
                r[0] |= bits.ReadBitsReversed(2) << 10;/* rx[10:11] */
                g[1] |= bits.ReadBits(8);        /* gx[7:0] */
                g[0] |= bits.ReadBitsReversed(2) << 10;/* gx[10:11] */
                b[1] |= bits.ReadBits(8);        /* bx[7:0] */
                b[0] |= bits.ReadBitsReversed(2) << 10;/* bx[10:11] */
                mode = 12;
            } break;

            /* mode 14 */
            case 0b01111: {
                /* Partitition indices: 63 bits
                   Partition: 0 bits
                   Color Endpoints: 60 bits (16.4, 16.4, 16.4) */
                r[0] |= bits.ReadBits(10);       /* rw[9:0] */
                g[0] |= bits.ReadBits(10);       /* gw[9:0] */
                b[0] |= bits.ReadBits(10);       /* bw[9:0] */
                r[1] |= bits.ReadBits(4);        /* rx[3:0] */
                r[0] |= bits.ReadBitsReversed(6) << 10;/* rw[10:15] */
                g[1] |= bits.ReadBits(4);        /* gx[3:0] */
                g[0] |= bits.ReadBitsReversed(6) << 10;/* gw[10:15] */
                b[1] |= bits.ReadBits(4);        /* bx[3:0] */
                b[0] |= bits.ReadBitsReversed(6) << 10;/* bw[10:15] */
                mode = 13;
            } break;
            default:
                // Reserved mode: spec requires all-zero colour (BC6H has no alpha; we emit opaque).
                for (var t = 0; t < 16; t++)
                {
                    dst[(t * 4) + 0] = 0f;
                    dst[(t * 4) + 1] = 0f;
                    dst[(t * 4) + 2] = 0f;
                    dst[(t * 4) + 3] = 1f;
                }

                return;
        }

        var numPartitions = mode >= 10 ? 0 : 1;
        var actualBits0 = ActualBits[0][mode];

        if (isSigned)
        {
            r[0] = ExtendSign(r[0], actualBits0);
            g[0] = ExtendSign(g[0], actualBits0);
            b[0] = ExtendSign(b[0], actualBits0);
        }

        // Modes 9 and 10 store endpoints explicitly (no delta); everything else delta-codes them.
        if ((mode != 9 && mode != 10) || isSigned)
        {
            for (var i = 1; i < (numPartitions + 1) * 2; i++)
            {
                r[i] = ExtendSign(r[i], ActualBits[1][mode]);
                g[i] = ExtendSign(g[i], ActualBits[2][mode]);
                b[i] = ExtendSign(b[i], ActualBits[3][mode]);
            }
        }

        if (mode != 9 && mode != 10)
        {
            for (var i = 1; i < (numPartitions + 1) * 2; i++)
            {
                r[i] = TransformInverse(r[i], r[0], actualBits0, isSigned);
                g[i] = TransformInverse(g[i], g[0], actualBits0, isSigned);
                b[i] = TransformInverse(b[i], b[0], actualBits0, isSigned);
            }
        }

        for (var i = 0; i < (numPartitions + 1) * 2; i++)
        {
            r[i] = Unquantize(r[i], actualBits0, isSigned);
            g[i] = Unquantize(g[i], actualBits0, isSigned);
            b[i] = Unquantize(b[i], actualBits0, isSigned);
        }

        var weights = mode >= 10 ? Weight4 : Weight3;
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                var partitionSet = mode >= 10 ? ((i | j) != 0 ? 0 : 128) : Partition(partition, i, j);
                var indexBits = mode >= 10 ? 4 : 3;
                if ((partitionSet & 0x80) != 0)
                {
                    indexBits--;
                }

                partitionSet &= 0x01;
                var index = bits.ReadBits(indexBits);
                var ep = partitionSet * 2;
                var texel = (i * 4) + j;

                dst[(texel * 4) + 0] = HalfToFloat(FinishUnquantize(Interpolate(r[ep], r[ep + 1], weights, index), isSigned));
                dst[(texel * 4) + 1] = HalfToFloat(FinishUnquantize(Interpolate(g[ep], g[ep + 1], weights, index), isSigned));
                dst[(texel * 4) + 2] = HalfToFloat(FinishUnquantize(Interpolate(b[ep], b[ep + 1], weights, index), isSigned));
                dst[(texel * 4) + 3] = 1f;
            }
        }
    }

    // BC6H uses the first 32 entries of BC7's 2-subset partition table (anchor flagged by bit 7).
    private static int Partition(int partition, int row, int col) => Bc7Tables.Partition2[(partition * 16) + (row * 4) + col];

    private static int ExtendSign(int value, int bits) => (value << (32 - bits)) >> (32 - bits);

    private static int TransformInverse(int value, int baseVal, int bits, bool isSigned)
    {
        value = (value + baseVal) & ((1 << bits) - 1);
        return isSigned ? ExtendSign(value, bits) : value;
    }

    private static int Unquantize(int value, int bits, bool isSigned)
    {
        if (!isSigned)
        {
            if (bits >= 15) return value;
            if (value == 0) return 0;
            if (value == (1 << bits) - 1) return 0xFFFF;
            return ((value << 16) + 0x8000) >> bits;
        }

        if (bits >= 16) return value;

        var sign = false;
        if (value < 0)
        {
            sign = true;
            value = -value;
        }

        int unq;
        if (value == 0) unq = 0;
        else if (value >= (1 << (bits - 1)) - 1) unq = 0x7FFF;
        else unq = ((value << 15) + 0x4000) >> (bits - 1);

        return sign ? -unq : unq;
    }

    private static int Interpolate(int a, int b, int[] weights, int index)
        => ((a * (64 - weights[index])) + (b * weights[index]) + 32) >> 6;

    private static ushort FinishUnquantize(int value, bool isSigned)
    {
        if (!isSigned)
        {
            return (ushort)((value * 31) >> 6); // scale magnitude by 31/64
        }

        value = value < 0 ? -(((-value) * 31) >> 5) : (value * 31) >> 5; // scale magnitude by 31/32
        var sign = 0;
        if (value < 0)
        {
            sign = 0x8000;
            value = -value;
        }

        return (ushort)(sign | value);
    }

    private static float HalfToFloat(ushort half) => (float)BitConverter.UInt16BitsToHalf(half);

    /// <summary>Reads the 128-bit block LSB-first (matching bcdec's low/high bitstream).</summary>
    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _block;
        private int _pos;

        public BitReader(ReadOnlySpan<byte> block)
        {
            _block = block;
            _pos = 0;
        }

        public int ReadBit()
        {
            var bit = (_block[_pos >> 3] >> (_pos & 7)) & 1;
            _pos++;
            return bit;
        }

        public int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                value |= ReadBit() << i;
            }

            return value;
        }

        public int ReadBitsReversed(int count)
        {
            var bits = ReadBits(count);
            var result = 0;
            while (count-- > 0)
            {
                result = (result << 1) | (bits & 1);
                bits >>= 1;
            }

            return result;
        }
    }
}