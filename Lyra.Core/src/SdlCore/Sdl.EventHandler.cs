using Lyra.Common;
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
                    break;

                case EventType.WindowLeaveFullscreen:
                    _isFullscreen = false;
                    break;

                case EventType.Quit:
                    ExitApplication();
                    break;
            }
        }
    }

    private void OnWindowResized()
    {
        var bounds = DimensionHelper.GetDrawableSize(_window, out var scale);
        Logger.Debug($"[EventHandler] Drawable size changed: {bounds.Width}x{bounds.Height}; Scale: x{scale}");

        if (_displayMode == DisplayMode.FitToScreen && _composite != null)
            UpdateFitToScreen();

        Publish(new DrawableSizeChangedEvent(bounds.Width, bounds.Height, scale));
    }

    private void RefreshDisplayInfo()
    {
        Logger.Info("[EventHandler] Refreshing display info.");
        var displayScale = GetWindowDisplayScale(_window);
        Publish(new DisplayScaleChangedEvent(displayScale));
        
        var displayId = GetDisplayForWindow(_window);
        GetDisplayBounds(displayId, out var displayBounds);
        Publish(new DisplayBoundsChangedEvent((int)(displayBounds.W * displayScale), (int)(displayBounds.H * displayScale), displayId));
    }

    private void OnMouseButtonDown(Event e)
    {
        if (e.Button.Button == ButtonLeft)
            StartPanning(e.Motion.X, e.Motion.Y);
    }

    private void OnMouseButtonUp(Event e)
    {
        if (e.Button.Button == ButtonLeft)
            StopPanning();
    }

    private void OnMouseMotion(Event e)
    {
        if (_isPanning)
            HandlePanning(e.Motion.X, e.Motion.Y);
    }

    private void OnMouseWheel(Event e)
    {
        GetMouseState(out var mouseX, out var mouseY);
        ZoomAtPoint(mouseX, mouseY, e.Wheel.Y);
    }
}