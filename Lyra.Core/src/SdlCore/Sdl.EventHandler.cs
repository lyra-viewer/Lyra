using Lyra.Common;
using Lyra.Common.Events;
using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using static Lyra.Common.Events.EventManager;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public partial class SdlCore
{
    private void HandleEvents()
    {
        while (PollEvent(out var e))
        {
            switch ((EventType)e.Type)
            {
                case EventType.KeyDown:
                    if (!e.Key.Repeat)
                        HandleScancode(e.Key.Scancode, e.Key.Mod);
                    break;

                case EventType.MouseButtonDown:
                    OnMouseButtonDown(e);
                    break;

                case EventType.MouseButtonUp:
                    OnMouseButtonUp(e);
                    break;

                case EventType.MouseMotion:
                    OnMouseMotion(e);
                    break;

                case EventType.MouseWheel:
                    OnMouseWheel(e);
                    break;

                case EventType.DropBegin:
                    OnDropBegin();
                    break;

                case EventType.DropFile:
                    OnDropFile(e);
                    break;

                case EventType.DropComplete:
                    OnDropComplete();
                    break;

                case EventType.WindowResized:
                    OnWindowResized();
                    break;

                case EventType.WindowShown:
                case EventType.WindowDisplayChanged:
                case EventType.WindowDisplayScaleChanged:
                    RefreshDisplayInfo();
                    break;

                case EventType.WindowEnterFullscreen:
                    _isFullscreen = true;
                    Logger.Debug($"[EventHandler] Fullscreen: {_isFullscreen}");
                    break;

                case EventType.WindowLeaveFullscreen:
                    _isFullscreen = false;
                    Logger.Debug($"[EventHandler] Fullscreen: {_isFullscreen}");
                    break;

                case EventType.Quit:
                    ExitApplication();
                    break;
            }
        }
    }

    private void OnWindowResized()
    {
        var bounds = DimensionHelper.GetDrawableSize(_window);
        Logger.Info($"[EventHandler] Drawable size changed: {bounds.PixelWidth}x{bounds.PixelHeight}; Scale: x{bounds.Scale}");

        if (_displayMode == DisplayMode.FitToScreen && _composite != null)
            UpdateFitToScreen();

        Publish(new DrawableSizeChangedEvent(bounds.PixelWidth, bounds.PixelHeight, bounds.Scale));
    }

    private void RefreshDisplayInfo()
    {
        Logger.Info("[EventHandler] Refreshing display info.");

        var displayScale = GetWindowDisplayScale(_window);
        var displayId = GetDisplayForWindow(_window);
        GetDisplayBounds(displayId, out var displayBounds);
        Publish(new DisplayBoundsChangedEvent((int)(displayBounds.W * displayScale), (int)(displayBounds.H * displayScale), displayId));
    }

    private void OnMouseButtonDown(Event e)
    {
        if (_renderer.UIManager.HandlePointerDown(e.Motion.X, e.Motion.Y))
            return;

        switch (e.Button.Button)
        {
            case ButtonLeft:
            case ButtonMiddle when SettingsManager.AppSettings.MidMouseButtonFunction == MidMouseButtonFunction.Pan:
                StartPanning(e.Motion.X, e.Motion.Y);
                break;
        }
    }

    private void OnMouseButtonUp(Event e)
    {
        if (_isPanning && IsPanButton(e.Button.Button))
        {
            StopPanning();
            return;
        }

        if (_renderer.UIManager.HandlePointerUp(e.Motion.X, e.Motion.Y))
            return;

        switch (e.Button.Button)
        {
            case ButtonMiddle when SettingsManager.AppSettings.MidMouseButtonFunction == MidMouseButtonFunction.Exit:
                HandleEscape();
                break;
        }
    }

    private void OnMouseMotion(Event e)
    {
        _renderer.UIManager.HandlePointerMove(e.Motion.X, e.Motion.Y);

        if (_isPanning)
            HandlePanning(e.Motion.X, e.Motion.Y);
    }

    private void OnMouseWheel(Event e)
    {
        if (_renderer.UIManager.HandleScroll(e.Wheel.MouseX, e.Wheel.MouseY, e.Wheel.X, e.Wheel.Y))
            return;


        GetMouseState(out var mouseX, out var mouseY);
        ZoomAtPoint(mouseX, mouseY, e.Wheel.Y);
    }
    
    private static bool IsPanButton(byte button) =>
        button == ButtonLeft ||
        (button == ButtonMiddle && SettingsManager.AppSettings.MidMouseButtonFunction == MidMouseButtonFunction.Pan);
}