using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;

namespace Lyra.Imaging.Decoding.Support;

public static class HdrDecodeSettings
{
    private static volatile int _toneMapMode = (int)ToneMapMode.Aces;
    private static volatile int _exposureStops;

    /// <summary>Curve applied to scene-referred float pixels.</summary>
    public static ToneMapMode ToneMapMode
    {
        get => (ToneMapMode)_toneMapMode;
        set => _toneMapMode = (int)value;
    }

    /// <summary>
    /// Exposure in whole stops, applied as a 2^n multiply before the curve. 0 leaves the image
    /// exactly as the file describes it.
    /// </summary>
    public static int ExposureStops
    {
        get => _exposureStops;
        set => _exposureStops = value;
    }

    /// <summary>Linear multiplier for the current exposure.</summary>
    public static float ExposureScale => MathF.Pow(2f, _exposureStops);

    public static void InitializeFromSettings()
    {
        ToneMapMode = SettingsManager.UiSettings.ToneMapMode;
        ExposureStops = SettingsManager.UiSettings.ExposureStops;
    }
}