using Lyra.Common.Settings;
using Lyra.Imaging.Content;
using Lyra.SdlCore;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public class HelpBarOverlay : IOverlay<(Composite? composite, ApplicationStates states)>
{
    private readonly SKFont _font;
    private readonly TaggedTextRenderer _text = new();

    public HelpBarOverlay()
    {
        _font = FontHelper.GetMonoFont(SettingsManager.AppSettings.HelpTextSize);
        _font.Edging = SKFontEdging.Antialias;
        _font.Subpixel = false;
    }

    public void Render(SKCanvas canvas, float logicalWidth, float logicalHeight, SKColor textColor, (Composite? composite, ApplicationStates states) data)
    {
        if (data.composite == null)
            return;
        
        var padding = OverlayTextMetrics.Padding();

        // Two-line help bar pinned to the bottom of the drawable area.
        var line2Y = logicalHeight - padding - OverlayTextMetrics.BaseLineGap;
        var line1Y = line2Y - OverlayTextMetrics.LineHeight(_font);

        var anyInfoHidden = !data.states.ShowExif && (data.composite.ExifInfo != null && data.composite.ExifInfo.HasData() || data.composite.FormatSpecific.Count > 0);
        var multiDir = data.states is { DirectoryCount: not null, DirectoryIndex: not null };
        var vector = data.composite.Content?.Kind == CompositeContentKind.Vector;

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
            "[0]  Fit Screen / Orig. Size",
            "                            "
        );

        var column5 = (
            "[I]  Toggle Info " + (anyInfoHidden ? "·" : " "),
            "[H]  Toggle Help  "
        );

        var column6 = (
            "[B]  Toggle Background",
            !vector ? "[S]  Toggle Sampling  " : "                      "
        );

        var column7 = (
            "[F]    Toggle Fullscreen",
            "[Esc]  Quit             "
        );

        var columns = new[] { column1, column2, column3, column4, column5, column6,  column7 };
        var gap = _font.Size * 1.8f;
        var x = padding;

        _text.SetTextColor(textColor);

        foreach (var col in columns)
        {
            _text.Draw(canvas, col.Item1, x, line1Y, _font);
            _text.Draw(canvas, col.Item2, x, line2Y, _font);

            var colWidth = MathF.Max(
                _text.Measure(col.Item1, _font),
                _text.Measure(col.Item2, _font)
            );

            x += colWidth + gap;

            if (x > logicalWidth - padding)
                break;
        }
    }
}