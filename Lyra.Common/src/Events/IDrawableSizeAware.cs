using static Lyra.Common.Events.EventManager;

namespace Lyra.Common.Events;

public interface IDrawableSizeAware
{
    void OnDrawableSizeChanged(DrawableSizeChangedEvent e);
}