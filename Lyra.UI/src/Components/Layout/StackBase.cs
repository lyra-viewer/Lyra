using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Layout;

public abstract class StackBase : ComponentBase, IContainer
{
    private readonly List<IComponent> _children = [];
    public IReadOnlyList<IComponent> Children => _children;

    private float _spacing;
    public float Spacing { get => _spacing; set => Set(ref _spacing, value); }

    /// <summary>
    /// Main-axis alignment of children within the container.
    /// Only visible when children don't fill the full main axis.
    /// </summary>
    private HAlign _contentAlign = HAlign.Left;
    public HAlign ContentAlign { get => _contentAlign; set => Set(ref _contentAlign, value); }

    // Unconstrained content sizes for Flexible children,
    // captured during Measure and used by Resolve for growth caps.
    private Dictionary<IComponent, float>? _flexContentMain;

    // --------------------------------------------------------
    //  Children
    // --------------------------------------------------------

    public void AddComponent(IComponent child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    public void AddComponents(params IComponent[] children)
    {
        foreach (var child in children)
            AddComponent(child);
    }

    // --------------------------------------------------------
    //  Axis abstraction - subclasses map main/cross to H/V
    // --------------------------------------------------------

    protected abstract float GetMain(SKSize size);
    protected abstract float GetCross(SKSize size);
    protected abstract SKSize MakeSize(float main, float cross);

    protected abstract SKRect MakeChildRect(
        float mainOffset, float crossOffset,
        float mainLength, float crossLength,
        SKRect containerBounds);

    protected abstract SizeMode GetMainSizeMode(IComponent child);
    protected abstract SizeMode GetCrossSizeMode(IComponent child);
    protected abstract float GetCrossOffset(IComponent child, float childCross, float availableCross);
    protected abstract float? GetMainMaxSize(IComponent child);

    // --------------------------------------------------------
    //  Measure
    // --------------------------------------------------------
    //  Three passes:
    //    1. Shrink / Fixed - measure at full available
    //    2. Flexible - budget-constrained to remaining space
    //    3. Expand - share whatever is left
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var availableMain = GetMain(availableSize);
        var availableCross = GetCross(availableSize);

        var presentCount = 0;
        foreach (var child in _children)
        {
            if (child.Present) 
                presentCount++;
        }

        var totalSpacing = Math.Max(0, presentCount - 1) * Spacing;

        // Pass 1: measure Shrink and Fixed children
        var fixedConsumed = 0f;
        var expandCount = 0;
        List<IComponent>? flexibles = null;

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            var mode = GetMainSizeMode(child);
            
            if (mode == SizeMode.Expand)
            {
                expandCount++;
                continue;
            }

            if (mode == SizeMode.Flexible)
            {
                flexibles ??= [];
                flexibles.Add(child);
                continue;
            }

            child.Measure(MakeSize(availableMain, availableCross));
            fixedConsumed += GetMain(child.DesiredSize);
        }

        // Pass 2: measure Flexible children within their budget
        _flexContentMain = null;

        if (flexibles is { Count: > 0 })
        {
            _flexContentMain = new Dictionary<IComponent, float>();

            foreach (var child in flexibles)
            {
                child.Measure(MakeSize(availableMain, availableCross));
                _flexContentMain[child] = GetMain(child.DesiredSize);
            }

            var flexBudget = Math.Max(0, availableMain - fixedConsumed - totalSpacing);
            var totalFlexDesired = 0f;

            foreach (var child in flexibles)
                totalFlexDesired += GetMain(child.DesiredSize);

            // If Flexible children exceed their budget, constrain them
            if (totalFlexDesired > flexBudget + 0.5f)
                ConstrainFlexibleMeasure(flexibles, flexBudget, availableCross);
        }

        // Pass 3: distribute remaining space to Expand children
        if (expandCount > 0)
        {
            var consumed = fixedConsumed;
            if (flexibles != null)
            {
                foreach (var child in flexibles)
                    consumed += GetMain(child.DesiredSize);
            }

            var remaining = Math.Max(0, availableMain - consumed - totalSpacing);
            var perExpand = remaining / expandCount;

            foreach (var child in _children)
            {
                if (!child.Present)
                    continue;

                if (GetMainSizeMode(child) != SizeMode.Expand)
                    continue;

                child.Measure(MakeSize(perExpand, availableCross));
            }
        }

        // Compute total desired size from children
        var desiredMain = totalSpacing;
        var desiredCross = 0f;

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            desiredMain += GetMain(child.DesiredSize);
            desiredCross = Math.Max(desiredCross, GetCross(child.DesiredSize));
        }

        return MakeSize(desiredMain, desiredCross);
    }

    /// <summary>
    /// Re-measures Flexible children so their total fits within the budget.
    /// Children with less content than their fair share release excess
    /// to those with more content (iterative redistribution).
    /// </summary>
    private void ConstrainFlexibleMeasure(List<IComponent> flexibles, float budget, float availableCross)
    {
        // Build allocation map with iterative fair-share distribution
        var allocations = new Dictionary<IComponent, float>();
        var remaining = budget;
        var candidates = flexibles.ToList();

        while (remaining > 0.5f && candidates.Count > 0)
        {
            var share = remaining / candidates.Count;
            var nextCandidates = new List<IComponent>();
            var consumed = 0f;

            foreach (var child in candidates)
            {
                var contentMain = _flexContentMain!.GetValueOrDefault(child);
                var priorAlloc = allocations.GetValueOrDefault(child);
                var needed = Math.Max(0, contentMain - priorAlloc);
                var grant = Math.Min(share, needed);

                consumed += grant;

                if (!allocations.TryAdd(child, grant))
                    allocations[child] += grant;

                // Child still has more content - keep in next round
                if (needed > grant + 0.5f)
                    nextCandidates.Add(child);
            }

            remaining -= consumed;
            candidates = nextCandidates;
        }

        // Re-measure each Flexible child at its allocated budget
        foreach (var child in flexibles)
        {
            var alloc = allocations.GetValueOrDefault(child, 0);
            child.Measure(MakeSize(alloc, availableCross));
        }
    }

    // --------------------------------------------------------
    //  Resolve
    // --------------------------------------------------------
    //  Distributes surplus/deficit among Flexible children,
    //  re-measures them at adjusted sizes, then cascades.
    //  After Resolve, every child's DesiredSize reflects its
    //  final allocated size - Arrange just positions them.
    // --------------------------------------------------------

    protected override void ResolveContent()
    {
        var mainPad = GetMain(new SKSize(Padding.Horizontal, Padding.Vertical));
        var crossPad = GetCross(new SKSize(Padding.Horizontal, Padding.Vertical));
        var mainTotal = GetMain(DesiredSize) - mainPad;
        var crossTotal = GetCross(DesiredSize) - crossPad;

        // Collect flexible children and total consumed space
        var consumedMain = 0f;
        List<(IComponent Child, float DesiredMain)>? flexibles = null;
        var first = true;

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            if (!first)
                consumedMain += Spacing;

            first = false;

            var childMain = GetMain(child.DesiredSize);
            consumedMain += childMain;

            if (GetMainSizeMode(child) == SizeMode.Flexible)
            {
                flexibles ??= [];
                flexibles.Add((child, childMain));
            }
        }

        // Compute and apply adjustments
        if (flexibles is { Count: > 0 })
        {
            var delta = mainTotal - consumedMain;
            Dictionary<IComponent, float>? adjustMap = null;

            if (delta > 0.5f)
                adjustMap = DistributeGrowth(flexibles, delta);
            else if (delta < -0.5f)
                adjustMap = DistributeShrink(flexibles, -delta);

            // Re-measure adjusted children so their internal layout
            // recalculates for the adjusted size
            if (adjustMap != null)
            {
                foreach (var child in _children)
                {
                    if (!child.Present)
                        continue;

                    if (!adjustMap.TryGetValue(child, out var adjust))
                        continue;

                    if (MathF.Abs(adjust) < 0.5f)
                        continue;

                    var targetMain = Math.Max(0, GetMain(child.DesiredSize) + adjust);
                    child.Measure(MakeSize(targetMain, crossTotal));
                }
            }
        }

        foreach (var child in _children)
        {
            if (child.Present)
                child.Resolve();
        }
    }

    private Dictionary<IComponent, float> DistributeGrowth(List<(IComponent Child, float DesiredMain)> flexibles, float surplus)
    {
        var adjustMap = new Dictionary<IComponent, float>();
        var remaining = surplus;
        var candidates = flexibles.ToList();

        while (remaining > 0.5f && candidates.Count > 0)
        {
            var share = remaining / candidates.Count;
            var nextCandidates = new List<(IComponent Child, float DesiredMain)>();
            var consumed = 0f;

            foreach (var (child, desiredMain) in candidates)
            {
                var childPad = GetMain(new SKSize(child.Padding.Horizontal, child.Padding.Vertical));

                var contentCeiling = _flexContentMain?.GetValueOrDefault(child) ?? float.MaxValue;
                var maxMain = GetMainMaxSize(child);
                var maxCeiling = maxMain.HasValue ? maxMain.Value + childPad : float.MaxValue;
                var ceiling = Math.Min(contentCeiling, maxCeiling);

                var grant = Math.Min(share, ceiling);
                consumed += grant;

                if (!adjustMap.TryAdd(child, grant))
                    adjustMap[child] += grant;

                if (grant < share - 0.5f)
                    continue;

                if (ceiling - grant > 0.5f)
                    nextCandidates.Add((child, desiredMain));
            }

            remaining -= consumed;
            candidates = nextCandidates;
        }

        return adjustMap;
    }

    private Dictionary<IComponent, float> DistributeShrink(List<(IComponent Child, float DesiredMain)> flexibles, float deficit)
    {
        var adjustMap = new Dictionary<IComponent, float>();
        var remaining = deficit;
        var candidates = flexibles.ToList();
        const float defaultMinMain = 60f;

        while (remaining > 0.5f && candidates.Count > 0)
        {
            var share = remaining / candidates.Count;
            var nextCandidates = new List<(IComponent Child, float DesiredMain)>();
            var consumed = 0f;

            foreach (var (child, desiredMain) in candidates)
            {
                var minMain = GetMain(new SKSize(child.MinWidth ?? 0, child.MinHeight ?? 0));
                var childPad = GetMain(new SKSize(child.Padding.Horizontal, child.Padding.Vertical));
                var floor = Math.Max(minMain, defaultMinMain) + childPad;

                var currentMain = desiredMain + adjustMap.GetValueOrDefault(child);
                var shrinkRoom = Math.Max(0, currentMain - floor);

                var reduction = Math.Min(share, shrinkRoom);
                consumed += reduction;

                if (!adjustMap.TryAdd(child, -reduction))
                    adjustMap[child] -= reduction;

                if (reduction < share - 0.5f)
                    continue;

                if (shrinkRoom - reduction > 0.5f)
                    nextCandidates.Add((child, desiredMain));
            }

            remaining -= consumed;
            candidates = nextCandidates;
        }

        return adjustMap;
    }

    // --------------------------------------------------------
    //  Arrange
    // --------------------------------------------------------
    //  Sequential along main axis, aligned on cross axis.
    //  ContentAlign shifts the entire group along main axis.
    //  After Resolve, DesiredSize reflects final sizes -
    //  this method only positions children.
    // --------------------------------------------------------

    protected override void ArrangeContent(SKRect contentBounds)
    {
        var mainTotal = GetMain(new SKSize(contentBounds.Width, contentBounds.Height));
        var crossTotal = GetCross(new SKSize(contentBounds.Width, contentBounds.Height));

        // Compute total consumed main for ContentAlign offset
        var consumedMain = 0f;
        var first = true;

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            if (!first)
                consumedMain += Spacing;

            first = false;

            consumedMain += GetMain(child.DesiredSize);
        }

        var mainOffset = ContentAlign switch
        {
            HAlign.Center => (mainTotal - consumedMain) / 2f,
            HAlign.Right => mainTotal - consumedMain,
            _ => 0f
        };

        first = true;

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            if (!first)
                mainOffset += Spacing;

            first = false;

            var childMain = GetMain(child.DesiredSize);

            // Scrollable children must be constrained to remaining space
            // so their ViewportSize reflects the actual visible area.
            // Without this, a Shrink-sized ListView inside a constrained
            // container gets arranged at full content height, making
            // ViewportSize == ContentSize and the scrollbar inoperative.
            if (child is IScrollable)
            {
                var remainingMain = Math.Max(0, mainTotal - mainOffset);
                childMain = Math.Min(childMain, remainingMain);
            }

            var childCross = GetCrossSizeMode(child) == SizeMode.Expand
                ? crossTotal
                : GetCross(child.DesiredSize);

            var crossOffset = GetCrossSizeMode(child) == SizeMode.Expand
                ? 0f
                : GetCrossOffset(child, childCross, crossTotal);

            child.Arrange(MakeChildRect(
                mainOffset, crossOffset,
                childMain, childCross,
                contentBounds));

            mainOffset += childMain;
        }
    }

    // --------------------------------------------------------
    //  Render - children only, stack itself has no visuals
    // --------------------------------------------------------

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            child.Render(canvas);
        }
    }

    // --------------------------------------------------------
    //  Dispose - cascade to children
    // --------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var child in _children)
                child.Dispose();
        }

        base.Dispose(disposing);
    }
}