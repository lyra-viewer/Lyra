using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Renderer;

/// <summary>
/// Everything one frame draws beneath the interface: the image and how it sits in the window.
/// </summary>
/// <param name="HasView">False until the initial zoom has been resolved; the image is not drawn before that.</param>
/// <param name="Offset">Pan offset, in drawable pixels.</param>
public readonly record struct RenderScene(
    Composite? Composite,
    bool HasView,
    float ZoomPercentage,
    SKPoint Offset,
    BackgroundMode Background,
    SamplingMode Sampling
);