using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;

namespace Lyra.Imaging.Decoding.Support;

internal readonly record struct ToneCurve(ToneMapMode Mode, float ExposureScale);

public static class HdrDecodeSettings
{
    private sealed record Setting(ToneMapMode Mode, int Stops);

    private static volatile Setting _setting = new(ToneMapMode.Aces, 0);

    /// <summary>Curve applied to scene-referred float pixels.</summary>
    public static ToneMapMode ToneMapMode
    {
        get => _setting.Mode;
        set => _setting = _setting with { Mode = value };
    }

    /// <summary>
    /// Exposure in whole stops, applied as a 2^n multiply before the curve. 0 leaves the image
    /// exactly as the file describes it.
    /// </summary>
    public static int ExposureStops
    {
        get => _setting.Stops;
        set => _setting = _setting with { Stops = value };
    }

    /// <summary>Linear multiplier for the current exposure.</summary>
    public static float ExposureScale => MathF.Pow(2f, _setting.Stops);

    /// <summary>Both settings, read together.</summary>
    internal static ToneCurve Curve
    {
        get
        {
            var setting = _setting;
            return new ToneCurve(setting.Mode, MathF.Pow(2f, setting.Stops));
        }
    }

    public static void InitializeFromSettings()
    {
        ToneMapMode = SettingsManager.UiSettings.ToneMapMode;
        ExposureStops = SettingsManager.UiSettings.ExposureStops;
    }
}