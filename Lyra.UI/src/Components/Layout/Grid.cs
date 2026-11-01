using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Layout;

public class Grid : ComponentBase, IContainer
{
    private readonly List<IComponent> _children = [];
    public IReadOnlyList<IComponent> Children => _children;

    private int _columns = 2;

    /// <summary>
    /// Number of columns. Clamped to at least 1 - a zero or negative count has no
    /// layout meaning and would divide by zero when mapping index to cell, which
    /// would take down the render loop rather than degrade.
    /// </summary>
    public int Columns
    {
        get => _columns;
        set => _columns = Math.Max(1, value);
    }

    public float HorizontalSpacing { get; set; }
    public float VerticalSpacing { get; set; }

    // When true, all cells share the same width and height.
    // UniformConstraints applies min/max clamping to the uniform size.
    public bool UniformCells { get; set; }
    public Constraints UniformConstraints { get; set; } = new();

    // Cached per-column widths and per-row heights from last measure
    private float[] _columnWidths = [];
    private float[] _rowHeights = [];

    // Reused by PresentChildren; see the note there.
    private readonly List<IComponent> _presentBuffer = [];

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
    //  Measure
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var present = PresentChildren();
        if (present.Count == 0)
        {
            _columnWidths = [];
            _rowHeights = [];
            return SKSize.Empty;
        }

        var cols = Math.Min(Columns, present.Count);
        var rows = (int)Math.Ceiling((double)present.Count / cols);

        _columnWidths = new float[cols];
        _rowHeights = new float[rows];

        // Measure all children
        foreach (var child in present)
            child.Measure(availableSize);

        // Compute per-column widths and per-row heights
        for (var i = 0; i < present.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;

            _columnWidths[col] = Math.Max(_columnWidths[col], present[i].DesiredSize.Width);
            _rowHeights[row] = Math.Max(_rowHeights[row], present[i].DesiredSize.Height);
        }

        // Apply uniform sizing with constraints
        if (UniformCells)
        {
            var cellWidth = Max(_columnWidths);
            var cellHeight = Max(_rowHeights);

            if (UniformConstraints.MinWidth.HasValue) 
                cellWidth = Math.Max(cellWidth, UniformConstraints.MinWidth.Value);
            
            if (UniformConstraints.MaxWidth.HasValue) 
                cellWidth = Math.Min(cellWidth, UniformConstraints.MaxWidth.Value);
            
            if (UniformConstraints.MinHeight.HasValue) 
                cellHeight = Math.Max(cellHeight, UniformConstraints.MinHeight.Value);
            
            if (UniformConstraints.MaxHeight.HasValue) 
                cellHeight = Math.Min(cellHeight, UniformConstraints.MaxHeight.Value);

            Array.Fill(_columnWidths, cellWidth);
            Array.Fill(_rowHeights, cellHeight);
        }

        var totalWidth = Sum(_columnWidths) + Math.Max(0, cols - 1) * HorizontalSpacing;
        var totalHeight = Sum(_rowHeights) + Math.Max(0, rows - 1) * VerticalSpacing;

        return new SKSize(totalWidth, totalHeight);
    }

    // --------------------------------------------------------
    //  Resolve
    // --------------------------------------------------------

    protected override void ResolveContent()
    {
        foreach (var child in _children)
        {
            if (child.Present)
                child.Resolve();
        }
    }

    // --------------------------------------------------------
    //  Arrange
    // --------------------------------------------------------

    protected override void ArrangeContent(SKRect contentBounds)
    {
        var present = PresentChildren();
        if (present.Count == 0)
            return;

        var cols = Math.Min(Columns, present.Count);
        var y = contentBounds.Top;

        for (var i = 0; i < present.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;

            var x = contentBounds.Left;
            for (var c = 0; c < col; c++)
                x += _columnWidths[c] + HorizontalSpacing;

            if (col == 0 && row > 0)
                y += _rowHeights[row - 1] + VerticalSpacing;

            present[i].Arrange(new SKRect(
                x, y,
                x + _columnWidths[col],
                y + _rowHeights[row])
            );
        }
    }

    // --------------------------------------------------------
    //  Render
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
    //  Dispose
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

    // --------------------------------------------------------
    //  Helpers
    // --------------------------------------------------------
    
    private List<IComponent> PresentChildren()
    {
        _presentBuffer.Clear();

        foreach (var child in _children)
        {
            if (child.Present)
                _presentBuffer.Add(child);
        }

        return _presentBuffer;
    }

    private static float Sum(float[] values)
    {
        var total = 0f;
        foreach (var v in values)
            total += v;
        return total;
    }

    private static float Max(float[] values)
    {
        var max = 0f;
        foreach (var v in values)
        {
            if (v > max) max = v;
        }
        return max;
    }
}