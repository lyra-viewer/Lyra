namespace Lyra.Renderer.Backends;

/// <summary>
/// Decides whether to draw into a window that may not be on screen. A hidden window is skipped,
/// but tried again every <see cref="RetryIntervalMs"/>, so one that never reports itself visible
/// still draws.
/// </summary>
internal sealed class HiddenWindowGate
{
    public const long RetryIntervalMs = 2000;

    private long _hiddenSinceMs = -1;

    public bool ShouldDraw(bool visible, long nowMs)
    {
        if (visible)
        {
            _hiddenSinceMs = -1;
            return true;
        }

        if (_hiddenSinceMs < 0)
            _hiddenSinceMs = nowMs;

        if (nowMs - _hiddenSinceMs < RetryIntervalMs)
            return false;

        _hiddenSinceMs = nowMs;
        return true;
    }
}
