using System.Runtime.InteropServices;
using Lyra.Common;
using Lyra.FileLoader;
using static Lyra.Common.Events.EventManager;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public partial class SdlCore
{
    private readonly List<string> _currentDroppedPaths = [];
    private bool _collectingDrop = false; // TODO
    
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
                case EventType.WindowDisplayScaleChanged:
                    OnWindowDisplayScaleChange();
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

    private void OnDropBegin()
    {
        Logger.Info("[EventHandler] File drop started.");
        _currentDroppedPaths.Clear();
        _collectingDrop = true;
    }

    private void OnDropFile(Event e)
    {
        var droppedFilePtr = e.Drop.Data;
        var droppedFilePath = Marshal.PtrToStringUTF8(droppedFilePtr);
        if (droppedFilePath != null)
            _currentDroppedPaths.Add(droppedFilePath);
    }

    private void OnDropComplete()
    {
        Logger.Info($"[EventHandler] File drop completed. Paths count: {_currentDroppedPaths.Count}");
        _collectingDrop = false;

        if (_currentDroppedPaths.Count == 0)
            return;

        if (_currentDroppedPaths.Count == 1)
            DirectoryNavigator.SearchImages(_currentDroppedPaths[0]);
        else
            DirectoryNavigator.SearchImages(_currentDroppedPaths);

        LoadImage();
        _currentDroppedPaths.Clear();

        DeferUntilWarm(() => RaiseWindow(_window));
    }

    private void OnWindowResized()
    {
        var bounds = DimensionHelper.GetDrawableSize(_window, out var scale);
        Logger.Debug($"[EventHandler] Drawable size changed: {bounds.Width}x{bounds.Height}; Scale: x{scale}");

        if (_displayMode == DisplayMode.FitToScreen && _composite != null)
            UpdateFitToScreen();

        Publish(new DrawableSizeChangedEvent(bounds.Width, bounds.Height, scale));
    }

    private void OnWindowDisplayScaleChange()
    {
        Logger.Info("[EventHandler] Window shown or display scale changed.");
        Publish(new DisplayScaleChangedEvent(GetWindowDisplayScale(_window)));
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