namespace Lyra.Renderer.Display;

internal sealed class HeadroomTracker
{
    private const float RelativeEpsilon = 0.05f;

    private bool _hasReported;
    private DisplayCapabilities _lastReported;

    public DisplayCapabilities Current { get; private set; } = DisplayCapabilities.Sdr;
    
    public bool Observe(DisplayCapabilities sample)
    {
        Current = sample;

        if (_hasReported && !IsWorthReporting(_lastReported, sample))
            return false;

        _hasReported = true;
        _lastReported = sample;
        return true;
    }

    private static bool IsWorthReporting(DisplayCapabilities last, DisplayCapabilities now)
        => now.DisplayId != last.DisplayId
           || now.SupportsExtendedRange != last.SupportsExtendedRange
           || Moved(last.CurrentHeadroom, now.CurrentHeadroom)
           || Moved(last.PotentialHeadroom, now.PotentialHeadroom);

    private static bool Moved(float last, float now) => MathF.Abs(now - last) > last * RelativeEpsilon;
}