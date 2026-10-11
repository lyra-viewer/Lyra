using SkiaSharp;

namespace Lyra.SdlCore;

/// <summary>The zoom and pan the next image inherits while the view is locked.</summary>
public sealed class ViewLock
{
    public readonly record struct View(float Zoom, SKPoint Offset);

    private View? _pending;
    
    public void Capture(bool locked, View? outgoing)
    {
        if (locked && outgoing is { } view)
            _pending = view;
    }

    public View? Inherit(bool locked) => locked ? _pending : null;

    public void Release() => _pending = null;
}