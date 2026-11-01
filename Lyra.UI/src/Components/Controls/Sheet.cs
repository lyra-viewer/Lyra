using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

/// <param name="Caption">Shown under the image.</param>
/// <param name="ImageWidth">The image's own width, so it is fitted with its shape kept; 0 when there is none.</param>
/// <param name="ImageHeight">The image's own height; 0 when there is none.</param>
/// <param name="Failure">Why there is no image, shown in its place.</param>
public sealed record SheetTile(string Caption, float ImageWidth, float ImageHeight, string? Failure)
{
    public bool HasImage => Failure is null && ImageWidth > 0 && ImageHeight > 0;
}

/// <param name="CellSize">The side of each square image box.</param>
/// <param name="Gap">Between cells, and around them.</param>
/// <param name="CaptionHeight">The box under each image that holds its caption.</param>
public readonly record struct SheetMetrics(float CellSize, float Gap, float CaptionHeight);

/// <summary>
/// Captioned images side by side in a grid of square cells, each image fitted to its cell with its
/// shape kept, or the reason it is missing in its place. Images are drawn by the owner through
/// <see cref="DrawImage"/>, so it decides how. Laid out in whatever units the metrics are in.
/// </summary>
public sealed class Sheet : Grid
{
    private readonly SheetMetrics _metrics;

    /// <summary>Draws tile <c>index</c>'s image into the box given, already fitted to its shape.</summary>
    public Action<SKCanvas, int, SKRect>? DrawImage { get; set; }

    public Sheet(IReadOnlyList<SheetTile> tiles, int columns, SheetMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        _metrics = metrics;

        Columns = columns;
        UniformCells = true;
        HorizontalSpacing = metrics.Gap;
        VerticalSpacing = metrics.Gap;
        Padding = new Padding(metrics.Gap);

        for (var i = 0; i < tiles.Count; i++)
            AddComponent(new Tile(this, i, tiles[i]));
    }

    /// <summary>Lays the sheet out at its own size, origin at 0, 0. Returns that size.</summary>
    public SKSize Layout()
    {
        var size = Measure(new SKSize(float.MaxValue, float.MaxValue));
        Resolve();
        Arrange(SKRect.Create(size));
        return size;
    }

    private sealed class Tile : ComponentBase, IContainer
    {
        private readonly Sheet _sheet;
        private readonly int _index;
        private readonly SheetTile _tile;
        private readonly Label _caption;
        private readonly Label? _failure;
        private readonly IComponent[] _children;

        private SKRect _imageBox;
        private SKRect _captionBox;

        public IReadOnlyList<IComponent> Children => _children;

        public Tile(Sheet sheet, int index, SheetTile tile)
        {
            _sheet = sheet;
            _index = index;
            _tile = tile;

            var fontSize = sheet._metrics.CaptionHeight * 0.4f;

            _caption = TileLabel(tile.Caption, fontSize, Palette.Foreground);

            if (!tile.HasImage)
                _failure = TileLabel(tile.Failure ?? "", fontSize, Palette.Danger);

            _children = _failure is null ? [_caption] : [_caption, _failure];

            foreach (var child in _children)
                child.Parent = this;
        }

        public void AddComponent(IComponent child) =>
            throw new NotSupportedException("A tile's content is defined via its constructor.");

        public void AddComponents(params IComponent[] children) =>
            throw new NotSupportedException("A tile's content is defined via its constructor.");

        private static Label TileLabel(string text, float fontSize, SKColor color) => new(text)
        {
            FontSize = fontSize,
            Color = color,
            Ellipsize = true,
            HorizontalAlign = HAlign.Center,
            VerticalAlign = VAlign.Center
        };

        protected override SKSize MeasureContent(SKSize availableSize)
        {
            var m = _sheet._metrics;

            foreach (var child in _children)
                child.Measure(new SKSize(m.CellSize, m.CaptionHeight));

            return new SKSize(m.CellSize, m.CellSize + m.CaptionHeight);
        }

        protected override void ArrangeContent(SKRect contentBounds)
        {
            var m = _sheet._metrics;
            var inset = m.CellSize * 0.03f;

            _imageBox = SKRect.Create(contentBounds.Left, contentBounds.Top, m.CellSize, m.CellSize);
            _captionBox = SKRect.Create(contentBounds.Left, _imageBox.Bottom, m.CellSize, m.CaptionHeight);

            _caption.Arrange(Inset(_captionBox, inset));
            _failure?.Arrange(Inset(_imageBox, inset));
        }

        protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
        {
            if (_tile.HasImage)
            {
                _sheet.DrawImage?.Invoke(canvas, _index, Fit(_tile.ImageWidth, _tile.ImageHeight, _imageBox));
            }
            else
            {
                using var frame = new SKPaint();
                frame.Color = Palette.Faint;
                frame.Style = SKPaintStyle.Stroke;
                frame.StrokeWidth = Math.Max(1f, _sheet._metrics.CellSize / 256f);
                frame.IsAntialias = true;

                canvas.DrawRect(_imageBox, frame);
                _failure?.Render(canvas);
            }

            // A backing, so the caption reads on any background.
            using var backing = new SKPaint();
            backing.Color = Palette.Panel;
            canvas.DrawRect(_captionBox, backing);

            _caption.Render(canvas);
        }

        private static SKRect Inset(SKRect box, float by) => new(box.Left + by, box.Top, box.Right - by, box.Bottom);

        private static SKRect Fit(float width, float height, SKRect into)
        {
            var scale = Math.Min(into.Width / width, into.Height / height);
            var fitted = SKRect.Create(0, 0, width * scale, height * scale);
            fitted.Offset(into.MidX - fitted.MidX, into.MidY - fitted.MidY);
            return fitted;
        }

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
}