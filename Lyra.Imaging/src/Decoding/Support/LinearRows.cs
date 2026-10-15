using System.Runtime.InteropServices;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Linear scene-referred pixels, read as RGBA float a row at a time, so the HDR paths take
/// sources that are not float RGBA without first expanding the whole image to it.
/// </summary>
internal abstract unsafe class LinearRows(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    public long PixelCount => (long)Width * Height;

    /// <summary>Whether <see cref="Row"/> writes into the scratch row it is given.</summary>
    protected abstract bool NeedsScratch { get; }

    /// <summary>Row <paramref name="y"/> as <c>Width * 4</c> floats: the source's own memory, or <paramref name="scratch"/> filled in.</summary>
    public abstract float* Row(int y, float* scratch);

    /// <summary>One pixel's color, for sampling.</summary>
    public abstract void Rgb(long pixel, out float r, out float g, out float b);

    public delegate bool RowBody(int y, float* row);

    /// <summary>Runs <paramref name="body"/> over every row in parallel; true when every call returned true.</summary>
    public bool AllRows(CancellationToken ct, RowBody body) => AllOf(Height, ct, (y, scratch) => body(y, Row(y, scratch)));

    public delegate bool ScratchBody(int index, float* scratch);

    /// <summary>
    /// Runs <paramref name="body"/> for every index in parallel, each worker with its own scratch
    /// row when this source needs one; true when every call returned true.
    /// </summary>
    public bool AllOf(int count, CancellationToken ct, ScratchBody body)
    {
        var all = true;
        var options = new ParallelOptions { CancellationToken = ct };

        Parallel.For(0, count, options,
            () => NeedsScratch ? (nint)NativeMemory.Alloc((nuint)Width * 4, sizeof(float)) : 0,
            (index, _, scratch) =>
            {
                if (!body(index, (float*)scratch))
                    all = false;

                return scratch;
            },
            scratch =>
            {
                if (scratch != 0)
                    NativeMemory.Free((void*)scratch);
            });

        return all;
    }
}

/// <summary>Interleaved RGBA float, read in place.</summary>
internal sealed unsafe class FloatRgbaRows(float* pixels, int width, int height) : LinearRows(width, height)
{
    protected override bool NeedsScratch => false;

    public override float* Row(int y, float* scratch) => pixels + (nint)y * Width * 4;

    public override void Rgb(long pixel, out float r, out float g, out float b)
    {
        var p = pixels + (nint)pixel * 4;
        r = p[0];
        g = p[1];
        b = p[2];
    }
}

/// <summary>Interleaved 16-bit RGB where 65535 is <paramref name="gain"/>, opaque.</summary>
internal sealed unsafe class Rgb16Rows(ushort* pixels, int width, int height, float gain = 1f) : LinearRows(width, height)
{
    private readonly float _scale = gain / 65535f;

    protected override bool NeedsScratch => true;

    public override float* Row(int y, float* scratch)
    {
        var src = pixels + (nint)y * Width * 3;

        for (var x = 0; x < Width; x++)
        {
            var s = src + x * 3;
            var d = scratch + x * 4;
            d[0] = s[0] * _scale;
            d[1] = s[1] * _scale;
            d[2] = s[2] * _scale;
            d[3] = 1f;
        }

        return scratch;
    }

    public override void Rgb(long pixel, out float r, out float g, out float b)
    {
        var p = pixels + (nint)pixel * 3;
        r = p[0] * _scale;
        g = p[1] * _scale;
        b = p[2] * _scale;
    }
}