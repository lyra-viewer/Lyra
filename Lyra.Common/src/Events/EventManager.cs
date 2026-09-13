namespace Lyra.Common.Events;

public static class EventManager
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, List<Delegate>> Listeners = new();
    
    private static readonly Dictionary<Type, object> Retained = new();
    
    private static class Kind<T>
    {
        public static readonly bool Retained = typeof(IRetainedEvent).IsAssignableFrom(typeof(T));
    }
    
    public static void Subscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var replay = false;
        var state = default(T)!;

        lock (Gate)
        {
            if (!Listeners.TryGetValue(typeof(T), out var handlers))
                Listeners[typeof(T)] = handlers = [];

            handlers.Add(handler);

            if (Kind<T>.Retained && Retained.TryGetValue(typeof(T), out var value))
            {
                state = (T)value;
                replay = true;
            }
        }
        
        if (replay)
            handler(state);
    }

    public static void Unsubscribe<T>(Action<T> handler)
    {
        lock (Gate)
        {
            if (!Listeners.TryGetValue(typeof(T), out var handlers))
                return;

            handlers.Remove(handler);
            if (handlers.Count == 0)
                Listeners.Remove(typeof(T));
        }
    }

    public static void Publish<T>(T evt)
    {
        Delegate[] snapshot;

        lock (Gate)
        {
            if (Kind<T>.Retained)
                Retained[typeof(T)] = evt!;

            if (!Listeners.TryGetValue(typeof(T), out var handlers) || handlers.Count == 0)
                return;

            snapshot = handlers.ToArray();
        }

        foreach (var handler in snapshot)
            if (handler is Action<T> typed)
                typed.Invoke(evt);
    }
}
