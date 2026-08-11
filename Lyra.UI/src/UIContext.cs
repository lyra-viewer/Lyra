using Lyra.UI.Components;
using SkiaSharp;

namespace Lyra.UI;

/// <summary>
/// Holds the layered component trees and routes pointer input.
///
/// Coordinates must be in logical space (matching the layout).
/// SDL3 on macOS reports points directly; no conversion needed.
///
/// Layers are stored bottom-to-top.  Rendering iterates forward
/// (lowest layer first).  Input dispatch iterates backward (highest
/// layer first) and stops at the first layer that either handles
/// the event or has <see cref="Layer.BlocksInput"/> set.
///
/// Hit-testing within a single layer traverses back-to-front
/// (last child = top of visual stack) and returns the deepest
/// non-transient component under the pointer.
/// Components that are not Present, not Visible, or not Enabled
/// are excluded from hit-testing.
/// </summary>
public partial class UIContext : IDisposable
{
    // --------------------------------------------------------
    //  Layer management
    // --------------------------------------------------------

    private readonly List<Layer> _layers = [];

    /// <summary>
    /// Ordered bottom-to-top. LyraUI.Process iterates this for
    /// layout and rendering.
    /// </summary>
    public IReadOnlyList<Layer> Layers => _layers;

    /// <summary>
    /// Appends a new layer at the top of the z-order.
    /// </summary>
    public Layer AddLayer(string name)
    {
        var layer = new Layer(name, this);
        _layers.Add(layer);
        Invalidate();
        return layer;
    }

    /// <summary>
    /// Inserts a new layer before (below) an existing layer.
    /// Returns the new layer, or null if <paramref name="beforeName"/>
    /// was not found.
    /// </summary>
    public Layer? InsertLayerBefore(string name, string beforeName)
    {
        var index = _layers.FindIndex(l => l.Name == beforeName);
        if (index < 0)
            return null;

        var layer = new Layer(name, this);
        _layers.Insert(index, layer);
        Invalidate();
        return layer;
    }

    public Layer? GetLayer(string name) => _layers.FirstOrDefault(l => l.Name == name);

    public bool RemoveLayer(string name)
    {
        var layer = GetLayer(name);
        if (layer is null)
            return false;

        _layers.Remove(layer);
        layer.Dispose();
        Invalidate();
        return true;
    }

    private Layer GetOrCreateMainLayer()
    {
        var main = GetLayer("Main");
        if (main is not null)
            return main;

        main = new Layer("Main", this);
        _layers.Insert(0, main);
        return main;
    }

    public IComponent? Root
    {
        get => GetLayer("Main")?.Root;
        set => GetOrCreateMainLayer().Root = value;
    }

    // --------------------------------------------------------
    //  State
    // --------------------------------------------------------

    public bool IsDirty { get; private set; } = true;

    private IComponent? _hoveredComponent;
    public IComponent? HoveredComponent => _hoveredComponent;

    // Pointer capture: the component that received PointerDown owns the
    // subsequent PointerUp regardless of where the pointer ends up. This
    // is what lets a Button decide whether to fire Click (released-over)
    // or cancel (released-elsewhere) instead of getting a stuck _isPressed.
    private IComponent? _capturedComponent;

    public void Invalidate() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;

    // --------------------------------------------------------
    //  Pointer input - layer-aware dispatch
    // --------------------------------------------------------
    //  Each method iterates layers top-down (highest z first).
    //  Within a layer, normal hit-testing applies.
    //  If a layer has BlocksInput, dispatch stops there even
    //  if nothing was hit - this is the modal behavior.
    // --------------------------------------------------------

    public bool HandlePointerDown(SKPoint logicalPoint)
    {
        if (TryStartResize(logicalPoint))
            return true;

        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            if (layer.Root is null)
                continue;

            var hit = HitTest(layer.Root, logicalPoint);
            if (hit != null)
            {
                _capturedComponent = hit;
                hit.OnPointerDown(logicalPoint);
                return true;
            }

            if (layer.BlocksInput)
                return true;
        }

        return false;
    }

    public bool HandlePointerUp(SKPoint logicalPoint)
    {
        if (TryEndResize())
            return true;

        // If a component captured the press, route the release back to it
        // even when the pointer is no longer over it. This is what lets a
        // Button cleanly distinguish "released over me" from "press cancelled".
        if (_capturedComponent is not null)
        {
            var captured = _capturedComponent;
            _capturedComponent = null;
            captured.OnPointerUp(logicalPoint);
            return true;
        }

        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            if (layer.Root is null)
                continue;

            var hit = HitTest(layer.Root, logicalPoint);
            if (hit != null)
            {
                hit.OnPointerUp(logicalPoint);
                return true;
            }

            if (layer.BlocksInput)
                return true;
        }

        return false;
    }

    public bool HandlePointerMove(SKPoint logicalPoint)
    {
        if (TryHandleResizeDrag(logicalPoint))
            return true;

        var captured = _capturedComponent;
        if (captured is null)
            UpdateResizeCursor(logicalPoint);

        // Normal hover tracking across layers
        IComponent? hit = null;

        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            if (layer.Root is null)
                continue;

            hit = HitTest(layer.Root, logicalPoint);
            if (hit != null)
                break;

            if (layer.BlocksInput)
                break;
        }

        if (hit != _hoveredComponent)
        {
            _hoveredComponent?.OnPointerLeave();
            _hoveredComponent = hit;
            _hoveredComponent?.OnPointerEnter();
        }
        
        if (captured is not null)
        {
            captured.OnPointerMove(logicalPoint);
            Invalidate();
            return true;
        }

        hit?.OnPointerMove(logicalPoint);
        return hit != null;
    }

    /// <summary>
    /// Routes a wheel delta to the nearest IScrollable under the point.
    /// </summary>
    public bool HandleScroll(SKPoint logicalPoint, float delta)
    {
        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            if (layer.Root is null)
                continue;

            var hit = HitTest(layer.Root, logicalPoint);

            if (hit is null)
            {
                // Nothing hit in this layer - check if it blocks lower layers.
                if (layer.BlocksInput)
                    return true;

                continue;
            }

            // Pointer is over a UI component - walk up looking for a scrollable.
            var current = hit;
            while (current != null)
            {
                if (current is IScrollable scrollable && scrollable.OnScroll(delta))
                    return true;

                current = current.Parent;
            }

            // No scrollable consumed the event, but the pointer was over UI.
            // Block passthrough so scrolling the sidebar doesn't zoom the image.
            return true;
        }

        return false;
    }

    // --------------------------------------------------------
    //  Hit-testing
    // --------------------------------------------------------
    //  Traverses back-to-front (last child = top of visual stack).
    //  Skips: Transient, not Present, not Visible, not Enabled.
    //  Returns the deepest matching component, or null.
    // --------------------------------------------------------

    internal static IComponent? HitTest(IComponent component, SKPoint point)
    {
        if (!component.Present)
            return null;

        if (!component.IsEffectivelyVisible)
            return null;

        if (!component.IsEffectivelyEnabled)
            return null;

        if (!component.ArrangedBounds.Contains(point))
            return null;
        
        if (component is IScrollable scrollable && scrollable.ScrollbarContains(point))
            return component;

        if (component is IContainer container)
        {
            // Hoisted: Children is a property, and this runs on every pointer event.
            var children = container.Children;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                var hit = HitTest(children[i], point);
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
        foreach (var layer in _layers)
            layer.Dispose();

        _layers.Clear();
    }
}