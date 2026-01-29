using static Lyra.Common.Events.EventManager;

namespace Lyra.Imaging.ConstraintsProvider;

public static class DecodeConstraintsProvider
{
    public sealed record DisplaySnapshot(uint? DisplayId, int Width, int Height, float Scale);

    public static DisplaySnapshot Current { get; private set; } = new(null, 0, 0, 1f);

    static DecodeConstraintsProvider()
    {
        Subscribe<DisplayBoundsChangedEvent>(OnDisplayBoundsChanged);
    }

    private static void OnDisplayBoundsChanged(DisplayBoundsChangedEvent e)
    {
        Current = new DisplaySnapshot(e.DisplayId, e.Width, e.Height, e.Scale);
    }
}