namespace Lyra.Common.Events;

public static class EventManager
{
    private static readonly Dictionary<Type, List<Delegate>> Listeners = new();

    public static void Subscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (!Listeners.ContainsKey(type))
            Listeners[type] = [];

        Listeners[type].Add(handler);
    }

    public static void Unsubscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (Listeners.TryGetValue(type, out var handlers))
        {
            handlers.Remove(handler);
            if (handlers.Count == 0)
                Listeners.Remove(type);
        }
    }

    public static void Publish<T>(T evt)
    {
        var type = typeof(T);
        if (Listeners.TryGetValue(type, out var handlers))
        {
            foreach (var handler in handlers)
            {
                if (handler is Action<T> typedHandler)
                    typedHandler.Invoke(evt);
            }
        }
    }
    
    public readonly record struct DrawableSizeChangedEvent(int PixelWidth, int PixelHeight, float Scale);
    
    public readonly record struct DisplayBoundsChangedEvent(int PixelWidth, int PixelHeight, uint? DisplayId = null);
}