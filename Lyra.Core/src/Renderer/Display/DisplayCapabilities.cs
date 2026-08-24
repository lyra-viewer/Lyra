namespace Lyra.Renderer.Display;

/// <summary>
/// What the display the window currently sits on can do with values above SDR white.
/// </summary>
public readonly record struct DisplayCapabilities(uint DisplayId, string DisplayName, float PotentialHeadroom, float CurrentHeadroom, float ReferenceHeadroom)
{
    public static readonly DisplayCapabilities Sdr = new(0, "unknown", 1f, 1f, 1f);
    
    private const float Epsilon = 0.001f;
    
    public bool SupportsExtendedRange => PotentialHeadroom > 1f + Epsilon;
    
    public bool HasHeadroomNow => CurrentHeadroom > 1f + Epsilon;
    
    public static DisplayCapabilities Create(uint displayId, string? displayName, double potential, double current, double reference)
        => new(displayId,
            string.IsNullOrWhiteSpace(displayName) ? "unknown" : displayName,
            Sanitize(potential),
            Sanitize(current),
            Sanitize(reference)
        );

    private static float Sanitize(double value)
        => double.IsFinite(value) && value > 1.0 ? (float)value : 1f;

    public override string ToString()
        => $"\"{DisplayName}\" (display {DisplayId}): now {CurrentHeadroom:0.###}x, up to {PotentialHeadroom:0.###}x, reference {ReferenceHeadroom:0.###}x";
}