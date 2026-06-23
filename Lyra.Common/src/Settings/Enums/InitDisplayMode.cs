using System.ComponentModel;

namespace Lyra.Common.Settings.Enums;

public enum InitDisplayMode
{
    [Description("Fit large images (default)")]
    FitLarge,

    [Description("Fit all images")]
    FitAll,

    [Description("Fit small images")]
    FitSmall,

    [Description("Actual size")]
    ActualSize
}