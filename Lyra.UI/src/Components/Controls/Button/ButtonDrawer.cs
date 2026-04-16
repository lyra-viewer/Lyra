using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components.Controls.Button;

/// <summary>
/// Static stateless drawer for button visuals.
/// Handles background, border, and color resolution.
/// Text and icon rendering is handled by composed primitives.
/// </summary>
public static class ButtonDrawer
{
    public const float BorderWidth = 1f;
    public const float ContentPadH = 8f;
    public const float ContentPadV = 4f;
    public const float IconTextGap = 6f;

    // ============================================================
    //  Colors
    // ============================================================

    public static SKColor GetBackgroundColor(ButtonVariant variant, bool isHovered, bool isPressed)
    {
        return variant switch
        {
            ButtonVariant.Primary => isPressed ? Palette.PressedAccent
                : isHovered ? Palette.HoverAccent
                : Palette.Accent,

            ButtonVariant.Default => isPressed ? Palette.PressedPrimary
                : isHovered ? Palette.HoverPrimary
                : Palette.Primary,

            ButtonVariant.Danger => isPressed ? Palette.PressedDanger
                : isHovered ? Palette.HoverDanger
                : Palette.Danger,

            ButtonVariant.Ghost => isPressed ? Palette.PressedSubtle
                : isHovered ? Palette.HoverSubtle
                : SKColors.Transparent,

            ButtonVariant.Outline => isPressed ? Palette.PressedSubtle
                : isHovered ? Palette.HoverSubtle
                : SKColors.Transparent,

            ButtonVariant.Link => SKColors.Transparent,

            _ => Palette.Primary
        };
    }

    public static SKColor GetTextColor(ButtonVariant variant)
    {
        return variant switch
        {
            ButtonVariant.Primary => Palette.ForegroundDark,
            _ => Palette.Foreground
        };
    }

    public static SKColor GetBorderColor(ButtonVariant variant, bool isHovered)
    {
        return variant switch
        {
            ButtonVariant.Outline => isHovered ? Palette.BorderActive : Palette.Border,
            _ => SKColors.Transparent
        };
    }

    // ============================================================
    //  Draw background and border
    // ============================================================

    public static void DrawBackground(SKCanvas canvas, SKRect bounds, ButtonVariant variant, float cornerRadius, bool isHovered, bool isPressed)
    {
        var bgColor = GetBackgroundColor(variant, isHovered, isPressed);
        var borderColor = GetBorderColor(variant, isHovered);

        // Background
        if (bgColor != SKColors.Transparent)
        {
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            canvas.DrawRoundRect(bounds, cornerRadius, cornerRadius, bgPaint);
        }

        // Border
        if (borderColor != SKColors.Transparent)
        {
            using var borderPaint = new SKPaint
            {
                Color = borderColor,
                IsAntialias = true,
                IsStroke = true,
                StrokeWidth = BorderWidth
            };

            var inset = SKRect.Inflate(bounds, -BorderWidth / 2f, -BorderWidth / 2f);
            canvas.DrawRoundRect(inset, cornerRadius, cornerRadius, borderPaint);
        }
    }
}