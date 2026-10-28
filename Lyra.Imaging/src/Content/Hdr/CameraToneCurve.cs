using SkiaSharp;

namespace Lyra.Imaging.Content.Hdr;

/// <summary>
/// The tone curve a camera's profile prescribes (DNG ProfileToneCurve), applied in place of the
/// default curve: to linear ProPhoto, per channel but keeping each color's hue, as the DNG
/// reference renderer does. Owned by the content that carries it.
/// </summary>
public sealed class CameraToneCurve(float[] table) : IDisposable
{
    /// <summary>Linear ProPhoto (ROMM), the space the curve is defined in.</summary>
    public static readonly SKColorSpace WorkingSpace = SKColorSpace.CreateRgb(
        SKColorSpaceTransferFn.Linear,
        new SKColorSpaceXyz(
            0.7976749f, 0.1351917f, 0.0313534f,
            0.2880402f, 0.7118741f, 0.0000857f,
            0.0000000f, 0.0000000f, 0.8252100f
        ));

    private readonly Lock _gate = new();
    private SKImage? _image;
    private bool _disposed;

    /// <summary>Samples at evenly spaced inputs from 0 to 1.</summary>
    public float[] Table { get; } = table;

    public float Evaluate(float value)
    {
        if (float.IsNaN(value) || value <= 0f)
            return Table[0];

        if (value >= 1f)
            return Table[^1];

        var position = value * (Table.Length - 1);
        var i = (int)position;
        return Table[i] + (Table[Math.Min(i + 1, Table.Length - 1)] - Table[i]) * (position - i);
    }

    public SKImage? Image
    {
        get
        {
            lock (_gate)
            {
                if (_disposed)
                    return null;

                return _image ??= Build();
            }
        }
    }

    private SKImage Build()
    {
        var info = new SKImageInfo(Table.Length, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, null);
        using var bitmap = new SKBitmap(info);

        unsafe
        {
            var row = (Half*)bitmap.GetPixels();
            for (var i = 0; i < Table.Length; i++)
            {
                row[i * 4] = row[i * 4 + 1] = row[i * 4 + 2] = (Half)Table[i];
                row[i * 4 + 3] = (Half)1f;
            }
        }

        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _image?.Dispose();
            _image = null;
        }
    }
}