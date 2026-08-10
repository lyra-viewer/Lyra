using System.Reflection;
using Lyra.SystemUtils;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.Renderer.GUI.Sections;

public sealed class AboutSection : IDisposable
{
    private const string RepoUrl = "https://github.com/lyra-viewer/Lyra";
    private const string RepoLabel = "github.com/lyra-viewer/Lyra";
    
    private const float IconSize = 96f;

    private readonly VStack _panel;
    private readonly SKImage? _iconSource;

    public IComponent Root => _panel;

    public AboutSection()
    {
        _iconSource = LoadIcon();

        _panel = new VStack()
            .MinWidth(300)
            .Spacing(10f)
            .Padding(28f, 24f, 28f, 24f)
            .Background(Palette.Panel)
            .Children(
                new Image(IconSize, IconSize)
                    .Source(_iconSource)
                    .Align(HAlign.Center),
                new Label("Lyra Viewer")
                    .FontSize(18f)
                    .Bold()
                    .Color(Palette.Foreground)
                    .Align(HAlign.Center),
                new Label($"Version {ResolveVersion()}")
                    .FontSize(12f)
                    .Color(Palette.Dim)
                    .Align(HAlign.Center),
                new Button(RepoLabel, ButtonVariant.Link)
                    .Align(HAlign.Center)
                    .OnClick(() => UrlOpener.Open(RepoUrl)),
                new Label("Copyright © 2026 Nineveh · MIT License")
                    .FontSize(11f)
                    .Color(Palette.Dim)
                    .Align(HAlign.Center));
    }

    private static SKImage? LoadIcon()
    {
        try
        {
            var asm = typeof(AboutSection).Assembly;
            using var stream = asm.GetManifestResourceStream("LyraViewer.AppIcon.png");
            if (stream is null)
                return null;

            using var bitmap = SKBitmap.Decode(stream);
            return bitmap is null ? null : SKImage.FromBitmap(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveVersion()
    {
        var info = typeof(AboutSection).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(info))
            return "0.0.0";

        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }

    public void Dispose()
    {
        _panel.Dispose();
        _iconSource?.Dispose();
    }
}