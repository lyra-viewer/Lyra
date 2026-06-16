namespace Lyra.Common.Events;

public static class EventManager
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, List<Delegate>> Listeners = new();

    public static void Subscribe<T>(Action<T> handler)
    {
        lock (Gate)
        {
            if (!Listeners.TryGetValue(typeof(T), out var handlers))
                Listeners[typeof(T)] = handlers = [];

            handlers.Add(handler);
        }
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
            if (!Listeners.TryGetValue(typeof(T), out var handlers) || handlers.Count == 0)
                return;

            snapshot = handlers.ToArray();
        }

        foreach (var handler in snapshot)
            if (handler is Action<T> typed)
                typed.Invoke(evt);
    }
}
