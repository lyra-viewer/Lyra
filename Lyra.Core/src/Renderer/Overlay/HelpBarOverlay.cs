using Lyra.Imaging.Content;
using Lyra.SdlCore;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public class HelpBarOverlay : IOverlay<(Composite? composite, ApplicationStates states)>
{
    public float Scale { get; set; }
    public SKFont? Font { get; set; }

    private readonly TaggedTextRenderer _text = new();

    public HelpBarOverlay()
    {
        ReloadFont();
    }

    public void ReloadFont()
    {
        Font = FontHelper.GetScaledMonoFont(13, Scale);
        Font.Edging = SKFontEdging.Antialias;
        Font.Subpixel = false;
    }

    public void Render(SKCanvas canvas, DrawableBounds drawableBounds, SKColor textColor, (Composite? composite, ApplicationStates states) data)
    {
        if (Font == null || data.composite == null)
            return;
        
        var padding = OverlayTextMetrics.Padding(Scale);

        // Two-line help bar pinned to the bottom of the drawable area.
        var line2Y = drawableBounds.Height - padding - (OverlayTextMetrics.BaseLineGap * Scale);
        var line1Y = line2Y - OverlayTextMetrics.LineHeight(Font, Scale);

        var anyInfoHidden = !data.states.ShowExif && data.composite.ExifInfo != null && (data.composite.FormatSpecific.Count > 0 || data.composite.ExifInfo.HasData());
        var multiDir = data.states is { DirectoryCount: not null, DirectoryIndex: not null };

        var column1 = (
            "[ ← / → ]  Prev. / Next Image",
            "[⌘← / ⌘→]  First / Last Image"
        );

        var column2 = multiDir
            ? (
                "<e>[⌥← / →⌥]  Prev. / Next Dir Edge</>",
                "[↲]        Reveal In Finder     ")
            : (
                "[↲]  Reveal In Finder",
                "                     "
            );

        var column3 = (
            "[+ / -]        Zoom In / Zoom Out",
            "[Mouse Wheel]  Zoom At Cursor    "
        );

        var column4 = (
            "[I]  Toggle Info " + (anyInfoHidden ? "·" : " "),
            "[H]  Toggle Help  "
        );

        var column5 = (
            "[B]  Toggle Background",
            "[S]  Toggle Sampling  "
        );

        var column6 = (
            "[F]    Toggle Fullscreen",
            "[Esc]  Quit             "
        );

        var columns = new[] { column1, column2, column3, column4, column5, column6 };
        var gap = 33f * Scale;
        var x = padding;

        _text.SetTextColor(textColor);

        foreach (var col in columns)
        {
            _text.Draw(canvas, col.Item1, x, line1Y, Font);
            _text.Draw(canvas, col.Item2, x, line2Y, Font);

            var colWidth = MathF.Max(
                _text.Measure(col.Item1, Font),
                _text.Measure(col.Item2, Font)
            );

            x += colWidth + gap;

            if (x > drawableBounds.Width - padding)
                break;
        }
    }
}