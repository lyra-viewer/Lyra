using static Lyra.Common.Events.EventManager;

namespace Lyra.Imaging.ConstraintsProvider;

public static class DecodeConstraintsProvider
{
    public sealed record DisplaySnapshot(int LogicalWidth, int LogicalHeight, uint? DisplayId);

    public static DisplaySnapshot Current { get; private set; } = new(0, 0, null);

    static DecodeConstraintsProvider()
    {
        Subscribe<DisplayBoundsChangedEvent>(OnDisplayBoundsChanged);
    }

    private static void OnDisplayBoundsChanged(DisplayBoundsChangedEvent e)
    {
        Current = new DisplaySnapshot(e.LogicalWidth, e.LogicalHeight, e.DisplayId);
    }
}