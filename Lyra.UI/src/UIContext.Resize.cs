using Lyra.UI.Components;
using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI;

// ============================================================================
//  UIContext - RESIZE SUPPORT (partial)
// ----------------------------------------------------------------------------
//  Adds drag-to-resize behavior for components with ResizeEdges != None.
//
//  Edge detection runs before normal hit-testing so resize zones
//  at the boundary of a container take priority over child clicks.
//  Resize targets are searched top-down across layers, respecting
//  BlocksInput - a modal layer prevents resizing in layers below.
//
//  During a drag, pointer events are captured - no hover tracking
//  or child forwarding occurs until the drag ends.
//
//  Cursor changes are communicated via OnCursorChanged callback,
//  which UIManager wires to SDL_SetCursor.
// ============================================================================
public partial class UIContext
{
    // --------------------------------------------------------
    //  Constants
    // --------------------------------------------------------

    /// Width of the resize detection zone along each edge (logical px).
    private const float ResizeZoneSize = 5f;

    /// Absolute minimum size a component can be resized to,
    /// even if MinWidth/MinHeight is not set.
    private const float GlobalMinSize = 40f;

    // --------------------------------------------------------
    //  Cursor callback
    // --------------------------------------------------------

    private Action<CursorType>? _onCursorChanged;
    private CursorType _currentCursor = CursorType.Default;

    /// <summary>
    /// Register a callback for cursor shape changes.
    /// UIManager wires this to SDL_SetCursor.
    /// </summary>
    public void SetCursorCallback(Action<CursorType> callback) => _onCursorChanged = callback;

    private void RequestCursor(CursorType cursor)
    {
        if (cursor == _currentCursor)
            return;

        _currentCursor = cursor;
        _onCursorChanged?.Invoke(cursor);
    }

    // --------------------------------------------------------
    //  Drag state
    // --------------------------------------------------------

    private IComponent? _resizingComponent;
    private ResizeEdge _resizingEdge;
    private SKPoint _resizeDragStart;
    private float _resizeStartWidth;
    private float _resizeStartHeight;

    public bool IsResizing => _resizingComponent != null;

    // --------------------------------------------------------
    //  Integration points — call from existing Handle* methods
    // --------------------------------------------------------

    /// <summary>
    /// Call at the top of HandlePointerDown. If this returns true,
    /// a resize drag has started - skip normal hit-testing.
    /// </summary>
    private bool TryStartResize(SKPoint point)
    {
        if (IsOverScrollbar(point))
            return false;

        var target = FindResizeTargetAcrossLayers(point);
        if (target is null)
            return false;

        var (component, edge) = target.Value;
        var bounds = component.ArrangedBounds;

        _resizingComponent = component;
        _resizingEdge = edge;
        _resizeDragStart = point;
        _resizeStartWidth = bounds.Width;
        _resizeStartHeight = bounds.Height;

        return true;
    }

    /// <summary>
    /// Call at the top of HandlePointerMove. If this returns true,
    /// a resize drag is active - skip normal hover tracking.
    /// </summary>
    private bool TryHandleResizeDrag(SKPoint point)
    {
        if (_resizingComponent is null)
            return false;

        var deltaX = point.X - _resizeDragStart.X;
        var deltaY = point.Y - _resizeDragStart.Y;

        // Horizontal resize
        if (_resizingEdge.HasFlag(ResizeEdge.Right))
        {
            var newWidth = _resizeStartWidth + deltaX;
            ApplyWidth(_resizingComponent, newWidth);
        }
        else if (_resizingEdge.HasFlag(ResizeEdge.Left))
        {
            // Left edge: dragging left increases width
            var newWidth = _resizeStartWidth - deltaX;
            ApplyWidth(_resizingComponent, newWidth);
        }

        // Vertical resize
        if (_resizingEdge.HasFlag(ResizeEdge.Bottom))
        {
            var newHeight = _resizeStartHeight + deltaY;
            ApplyHeight(_resizingComponent, newHeight);
        }
        else if (_resizingEdge.HasFlag(ResizeEdge.Top))
        {
            var newHeight = _resizeStartHeight - deltaY;
            ApplyHeight(_resizingComponent, newHeight);
        }

        Invalidate();
        return true;
    }

    /// <summary>
    /// Call at the top of HandlePointerUp. If this returns true,
    /// a resize drag has ended - skip normal hit-testing.
    /// </summary>
    private bool TryEndResize()
    {
        if (_resizingComponent is null)
            return false;

        _resizingComponent = null;
        _resizingEdge = ResizeEdge.None;
        return true;
    }

    /// <summary>
    /// Call from HandlePointerMove when not dragging.
    /// Updates the cursor based on resize edge proximity.
    /// Returns the cursor that was set.
    /// </summary>
    private CursorType UpdateResizeCursor(SKPoint point)
    {
        if (IsOverScrollbar(point))
        {
            RequestCursor(CursorType.Default);
            return CursorType.Default;
        }

        var target = FindResizeTargetAcrossLayers(point);
        if (target is null)
        {
            RequestCursor(CursorType.Default);
            return CursorType.Default;
        }

        var cursor = GetCursorForEdge(target.Value.edge);
        RequestCursor(cursor);
        return cursor;
    }

    // --------------------------------------------------------
    //  Scrollbar priority
    // --------------------------------------------------------
    
    private bool IsOverScrollbar(SKPoint point)
    {
        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];

            if (layer.Root is not null && ContainsScrollbar(layer.Root, point))
                return true;

            if (layer.BlocksInput)
                return false;
        }

        return false;
    }

    /// <summary>
    /// Depth-first search for a scrollbar under the point. Pruned by
    /// ArrangedBounds - a scrollbar is always drawn inside its host.
    /// </summary>
    private static bool ContainsScrollbar(IComponent component, SKPoint point)
    {
        if (!component.Present || !component.IsEffectivelyVisible || !component.IsEffectivelyEnabled)
            return false;

        if (!component.ArrangedBounds.Contains(point))
            return false;

        if (component is IScrollable scrollable && scrollable.ScrollbarContains(point))
            return true;

        if (component is IContainer container)
            for (var i = container.Children.Count - 1; i >= 0; i--)
                if (ContainsScrollbar(container.Children[i], point))
                    return true;

        return false;
    }

    // --------------------------------------------------------
    //  Layer-aware resize target search
    // --------------------------------------------------------

    /// <summary>
    /// Searches layers top-down for a resize target.
    /// Respects BlocksInput - a blocking layer prevents
    /// resize detection in layers below.
    /// </summary>
    private (IComponent component, ResizeEdge edge)? FindResizeTargetAcrossLayers(SKPoint point)
    {
        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            if (layer.Root is null)
                continue;

            var target = FindResizeTarget(layer.Root, point);
            if (target.HasValue)
                return target;

            if (layer.BlocksInput)
                return null;
        }

        return null;
    }

    // --------------------------------------------------------
    //  Edge detection
    // --------------------------------------------------------

    /// <summary>
    /// Walks the component tree root-first (parent before children).
    /// Returns the first resizable component whose edge zone contains
    /// the point. Parent edges take priority over child content.
    /// </summary>
    private static (IComponent component, ResizeEdge edge)? FindResizeTarget(
        IComponent component, SKPoint point)
    {
        // Check this component first (parent wins over children)
        if (component.ResizeEdges != ResizeEdge.None)
        {
            var edge = GetEdgeAtPoint(component, point);
            if (edge != ResizeEdge.None)
                return (component, edge);
        }

        // Then recurse into children (back-to-front for z-order)
        if (component is IContainer container)
        {
            for (var i = container.Children.Count - 1; i >= 0; i--)
            {
                var child = container.Children[i];

                if (!child.Present || !child.IsEffectivelyVisible || !child.IsEffectivelyEnabled)
                    continue;

                var result = FindResizeTarget(child, point);
                if (result.HasValue)
                    return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Tests whether a point falls within the resize zone of any
    /// enabled edge on the given component.
    /// </summary>
    private static ResizeEdge GetEdgeAtPoint(IComponent component, SKPoint point)
    {
        var bounds = component.ArrangedBounds;
        var edges = component.ResizeEdges;

        var expanded = new SKRect(
            bounds.Left - ResizeZoneSize,
            bounds.Top - ResizeZoneSize,
            bounds.Right + ResizeZoneSize,
            bounds.Bottom + ResizeZoneSize);

        if (!expanded.Contains(point))
            return ResizeEdge.None;

        var result = ResizeEdge.None;

        if (edges.HasFlag(ResizeEdge.Left) && MathF.Abs(point.X - bounds.Left) <= ResizeZoneSize)
            result |= ResizeEdge.Left;

        if (edges.HasFlag(ResizeEdge.Right) && MathF.Abs(point.X - bounds.Right) <= ResizeZoneSize)
            result |= ResizeEdge.Right;

        if (edges.HasFlag(ResizeEdge.Top) && MathF.Abs(point.Y - bounds.Top) <= ResizeZoneSize)
            result |= ResizeEdge.Top;

        if (edges.HasFlag(ResizeEdge.Bottom) && MathF.Abs(point.Y - bounds.Bottom) <= ResizeZoneSize)
            result |= ResizeEdge.Bottom;

        return result;
    }

    // --------------------------------------------------------
    //  Size application
    // --------------------------------------------------------

    private static void ApplyWidth(IComponent component, float newWidth)
    {
        var min = MathF.Max(GlobalMinSize, component.MinWidth ?? 0f);
        var max = component.MaxWidth ?? float.MaxValue;
        component.Width = Math.Clamp(newWidth, min, max);
        component.HorizontalSize = SizeMode.Fixed;
    }

    private static void ApplyHeight(IComponent component, float newHeight)
    {
        var min = MathF.Max(GlobalMinSize, component.MinHeight ?? 0f);
        var max = component.MaxHeight ?? float.MaxValue;
        component.Height = Math.Clamp(newHeight, min, max);
        component.VerticalSize = SizeMode.Fixed;
    }

    // --------------------------------------------------------
    //  Cursor mapping
    // --------------------------------------------------------

    private static CursorType GetCursorForEdge(ResizeEdge edge)
    {
        var hasLeft = edge.HasFlag(ResizeEdge.Left);
        var hasRight = edge.HasFlag(ResizeEdge.Right);
        var hasTop = edge.HasFlag(ResizeEdge.Top);
        var hasBottom = edge.HasFlag(ResizeEdge.Bottom);

        // Corner combinations
        if ((hasTop && hasLeft) || (hasBottom && hasRight))
            return CursorType.ResizeNWSE;

        if ((hasTop && hasRight) || (hasBottom && hasLeft))
            return CursorType.ResizeNESW;

        // Single axis
        if (hasLeft || hasRight)
            return CursorType.ResizeEW;

        if (hasTop || hasBottom)
            return CursorType.ResizeNS;

        return CursorType.Default;
    }
}