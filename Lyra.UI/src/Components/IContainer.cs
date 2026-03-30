namespace Lyra.UI.Components;

/// <summary>
/// A component that contains and manages child components.
///
/// Containers are responsible for:
///   - Measuring and arranging children during layout
///   - Skipping children with Present = false
///   - Disposing children they own
///
/// The Children list is used by UIContext for hit-testing
/// traversal (deepest non-transient child wins).
/// </summary>
public interface IContainer : IComponent
{
    IReadOnlyList<IComponent> Children { get; }
    void AddComponent(IComponent child);
    void AddComponents(params IComponent[] children);
}