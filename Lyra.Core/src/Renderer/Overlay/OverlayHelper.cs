using Lyra.Common.Events;
using static Lyra.Common.Events.EventManager;

namespace Lyra.Renderer.Overlay;

public static class OverlayHelper
{
    public static T WithDrawableSizeSubscription<T>(this T overlay) where T : IDrawableSizeAware
    {
        Subscribe<DrawableSizeChangedEvent>(overlay.OnDrawableSizeChanged);
        return overlay;
    }
}