using System.ComponentModel;

namespace Lyra.Common.Settings.Enums;

public enum SamplingMode
{
    [Description("Cubic (Smooth)")]
    Cubic,

    [Description("Linear (Fast)")]
    Linear,

    [Description("Nearest (Pixelated)")]
    Nearest,

    [Description("Anti-aliasing OFF")]
    None
}