using Lyra.UI.Components;
using SkiaSharp;

namespace Lyra.UI;

/// <summary>
/// Holds the component tree state and routes pointer input.
///
/// Coordinates must be in logical space (matching the layout).
/// SDL3 on macOS reports points directly; no conversion needed.
///
/// Hit-testing traverses the tree back-to-front (last child first)
/// and returns the deepest non-transient component under the pointer.
/// Components that are not Present, not Visible, or not Enabled
/// are excluded from hit-testing.
/// </summary>
public partial class UIContext : IDisposable
{
    // --------------------------------------------------------
    //  State
    // --------------------------------------------------------

    public IComponent? Root { get; set; }
    public bool IsDirty { get; private set; } = true;

    private IComponent? _hoveredComponent;
    public IComponent? HoveredComponent => _hoveredComponent;

    public void Invalidate() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;

    // --------------------------------------------------------
    //  Pointer input
    // --------------------------------------------------------

    public bool HandlePointerDown(SKPoint logicalPoint)
    {
        if (Root is null)
            return false;

        if (TryStartResize(logicalPoint))
            return true;

        var hit = HitTest(Root, logicalPoint);
        hit?.OnPointerDown(logicalPoint);
        return hit != null;
    }

    public bool HandlePointerUp(SKPoint logicalPoint)
    {
        if (TryEndResize())
            return true;

        if (Root is null)
            return false;

        var hit = HitTest(Root, logicalPoint);
        hit?.OnPointerUp(logicalPoint);
        return hit != null;
    }

    public bool HandlePointerMove(SKPoint logicalPoint)
    {
        if (Root is null)
            return false;

        if (TryHandleResizeDrag(logicalPoint))
            return true;

        // Not dragging - update cursor for edge proximity
        UpdateResizeCursor(logicalPoint);

        // Normal hover tracking
        var hit = HitTest(Root, logicalPoint);

        if (hit != _hoveredComponent)
        {
            _hoveredComponent?.OnPointerLeave();
            _hoveredComponent = hit;
            _hoveredComponent?.OnPointerEnter();
        }

        hit?.OnPointerMove(logicalPoint);
        return hit != null;
    }

    public bool HandleScroll(SKPoint logicalPoint, float deltaX, float deltaY)
    {
        if (Root is null)
            return false;

        var hit = HitTest(Root, logicalPoint);
        var foundScrollable = false;

        while (hit != null)
        {
            if (hit is IScrollable scrollable)
            {
                foundScrollable = true;

                if (scrollable.OnScroll(deltaX, deltaY))
                    return true; // consumed
            }

            hit = hit.Parent;
        }

        return foundScrollable;
    }

    // --------------------------------------------------------
    //  Hit-testing
    // --------------------------------------------------------
    //  Traverses back-to-front (last child = top of visual stack).
    //  Skips: Transient, not Present, not Visible, not Enabled.
    //  Returns the deepest matching component, or null.
    // --------------------------------------------------------

    private static IComponent? HitTest(IComponent component, SKPoint point)
    {
        if (!component.Present)
            return null;

        if (!component.IsEffectivelyVisible)
            return null;

        if (!component.IsEffectivelyEnabled)
            return null;

        if (!component.ArrangedBounds.Contains(point))
            return null;

        if (component is IContainer container)
        {
            for (var i = container.Children.Count - 1; i >= 0; i--)
            {
                var hit = HitTest(container.Children[i], point);
                if (hit != null)
                    return hit;
            }
        }

        return component.Transient ? null : component;
    }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    public void Dispose()
    {
        Root?.Dispose();
    }
}