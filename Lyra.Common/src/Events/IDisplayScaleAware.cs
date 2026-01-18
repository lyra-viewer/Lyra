using static Lyra.Common.Events.EventManager;

namespace Lyra.Common.Events;

public interface IDisplayScaleAware
{
    void OnDisplayScaleChanged(DisplayScaleChangedEvent e);
}