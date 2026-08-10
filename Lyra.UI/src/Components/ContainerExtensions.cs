namespace Lyra.UI.Components;

public static class ContainerExtensions
{
    /// <summary>Adds children and returns the container, preserving its concrete type.</summary>
    public static T Children<T>(this T container, params IComponent[] children)
        where T : IContainer
    {
        container.AddComponents(children);
        return container;
    }

    /// <summary>Adds a single child and returns the container, preserving its concrete type.</summary>
    public static T Child<T>(this T container, IComponent child)
        where T : IContainer
    {
        container.AddComponent(child);
        return container;
    }
}
