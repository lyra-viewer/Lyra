using Lyra.Common;
using Lyra.FileLoader;
using Lyra.SystemUtils;
using SkiaSharp;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public partial class SdlCore
{
    private Dictionary<Scancode, Action> _scanActions;

    private PanHelper? _panHelper;

    private bool _isFullscreen;
    private bool _isPanning;

    private const float ZoomFactor = 1.05f;
    private const int MinZoom = 10;
    private const int MaxZoom = 10000;

    private void InitializeInput()
    {
        _scanActions = new Dictionary<Scancode, Action>
        {
            { Scancode.Escape, ExitApplication },
            { Scancode.Right, NextImage },
            { Scancode.Left, PreviousImage },
            { Scancode.Home, FirstImage },
            { Scancode.End, LastImage },
            { Scancode.I, ToggleInfo },
            { Scancode.B, ToggleBackground },
            { Scancode.F, ToggleFullscreen },
            { Scancode.Minus, ZoomOut },
            { Scancode.Equals, ZoomIn },
            { Scancode.Alpha0, ToggleDisplayMode },
            { Scancode.S, ToggleSampling },
            { Scancode.H, ToggleHelp },
            { Scancode.Return, OpenFileExplorer }
        };
    }

    private void HandleScancode(Scancode scancode, Keymod mods)
    {
        if ((mods & Keymod.Ctrl) != 0 || (mods & Keymod.GUI) != 0)
            switch (scancode)
            {
                case Scancode.Left:
                    FirstImage();
                    return;
                case Scancode.Right:
                    LastImage();
                    return;
            }
        else if (_scanActions.TryGetValue(scancode, out var scanAction))
        {
            scanAction.Invoke();
        }
    }

    private void NextImage()
    {
        if (DirectoryNavigator.HasNext())
        {
            DirectoryNavigator.MoveToNext();
            LoadImage();
        }
    }

    private void PreviousImage()
    {
        if (DirectoryNavigator.HasPrevious())
        {
            DirectoryNavigator.MoveToPrevious();
            LoadImage();
        }
    }

    private void FirstImage()
    {
        if (!DirectoryNavigator.IsFirst())
        {
            DirectoryNavigator.MoveToFirst();
            LoadImage();
        }
    }

    private void LastImage()
    {
        if (!DirectoryNavigator.IsLast())
        {
            DirectoryNavigator.MoveToLast();
            LoadImage();
        }
    }

    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private int _lastWindowX;
    private int _lastWindowY;

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;

        if (_isFullscreen)
        {
            GetWindowSize(_window, out _lastWindowWidth, out _lastWindowHeight);
            GetWindowPosition(_window, out _lastWindowX, out _lastWindowY);

            // SetWindowBordered(_window, false);
            // SetWindowResizable(_window, false);
            SetWindowFullscreen(_window, true);
            SetWindowPosition(_window, 0, 0);
        }
        else
        {
            // SetWindowBordered(_window, true);
            // SetWindowResizable(_window, true);
            SetWindowFullscreen(_window, false);
            SetWindowSize(_window, _lastWindowWidth, _lastWindowHeight);
            SetWindowPosition(_window, _lastWindowX, _lastWindowY);
        }

        Logger.Debug($"[Input] Fullscreen: {_isFullscreen}");
    }

    private void ToggleSampling()
    {
        _renderer.ToggleSampling();
    }

    private void ToggleBackground()
    {
        _renderer.ToggleBackground();
    }

    private void ToggleInfo()
    {
        _renderer.ToggleInfo();
    }

    private void ToggleHelp()
    {
        // TODO
    }

    private void OpenFileExplorer()
    {
        var path = DirectoryNavigator.GetCurrent() ?? DirectoryNavigator.GetTopDirectory();
        if (path != null)
            FileExplorerOpener.RevealPath(path);
    }

    private void ToggleDisplayMode()
    {
        if (_composite == null || _panHelper == null)
            return;

        if (_displayMode is DisplayMode.Free or DisplayMode.Undefined)
            _displayMode = DimensionHelper.GetInitialDisplayMode(_window, _composite, out _zoomPercentage);
        else if (_zoomPercentage == 100)
        {
            UpdateFitToScreen();
        }
        else
        {
            _displayMode = DisplayMode.OriginalImageSize;
            _zoomPercentage = 100;
        }

        _renderer.SetDisplayMode(_displayMode);
        _renderer.SetZoom(_zoomPercentage);

        _panHelper.UpdateZoom(_zoomPercentage);
        _panHelper.CurrentOffset = SKPoint.Empty; // reset offset on mode toggle
        _panHelper.Clamp();
        _renderer.SetOffset(_panHelper.CurrentOffset);
    }

    private void ZoomIn() => ApplyZoom((int)MathF.Round(_zoomPercentage * ZoomFactor, MidpointRounding.AwayFromZero));
    private void ZoomOut() => ApplyZoom((int)MathF.Round(_zoomPercentage / ZoomFactor, MidpointRounding.AwayFromZero));

    private void ApplyZoom(int newZoom)
    {
        if (_composite == null || _composite.IsEmpty || _panHelper == null)
            return;

        _zoomPercentage = Math.Clamp(newZoom, MinZoom, MaxZoom);
        _displayMode = _zoomPercentage == 100 ? DisplayMode.OriginalImageSize : DisplayMode.Free;

        _renderer.SetDisplayMode(_displayMode);
        _renderer.SetZoom(_zoomPercentage);

        _panHelper.UpdateZoom(_zoomPercentage);
        ClampOrCenterOffset();
    }

    private void ZoomAtPoint(float mouseX, float mouseY, float direction)
    {
        if (_composite == null || _composite.IsEmpty || _panHelper == null)
            return;

        _panHelper.UpdateZoom(_zoomPercentage);
        var newZoom = direction > 0
            ? (int)MathF.Round(_zoomPercentage * ZoomFactor, MidpointRounding.AwayFromZero)
            : (int)MathF.Round(_zoomPercentage / ZoomFactor, MidpointRounding.AwayFromZero);

        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (newZoom == _zoomPercentage)
            return;

        DimensionHelper.GetDrawableSize(_window, out var scale);
        var mouse = new SKPoint(mouseX * scale, mouseY * scale);

        var newOffset = _panHelper.GetOffsetForZoomAtCursor(mouse, newZoom);

        // Apply everything
        _zoomPercentage = newZoom;
        _displayMode = _zoomPercentage == 100 ? DisplayMode.OriginalImageSize : DisplayMode.Free;

        _renderer.SetDisplayMode(_displayMode);
        _renderer.SetZoom(_zoomPercentage);

        _panHelper.UpdateZoom(_zoomPercentage);
        _panHelper.CurrentOffset = newOffset;
        _panHelper.Clamp();
        _renderer.SetOffset(_panHelper.CurrentOffset);
    }

    private void UpdateFitToScreen()
    {
        if (_composite == null || _composite.IsEmpty)
            return;

        _zoomPercentage = DimensionHelper.GetZoomToFitScreen(_window, _composite.ContentWidth, _composite.ContentHeight);
        _displayMode = _zoomPercentage == 100 ? DisplayMode.OriginalImageSize : DisplayMode.FitToScreen;
        _renderer.SetDisplayMode(_displayMode);
        _renderer.SetZoom(_zoomPercentage);
    }

    private void StartPanning(float x, float y)
    {
        if (_composite == null || _composite.IsEmpty || _panHelper == null)
            return;

        if (_panHelper.CanPan())
        {
            _isPanning = true;
            _panHelper.Start(x, y);
        }
    }

    private void StopPanning()
    {
        _isPanning = false;
    }

    private void HandlePanning(float x, float y)
    {
        if (_composite == null || _composite.IsEmpty || !_isPanning || _panHelper == null)
            return;

        _panHelper.Move(x, y);
        _renderer.SetOffset(_panHelper.CurrentOffset);
    }

    private void ClampOrCenterOffset()
    {
        if (_panHelper == null)
            return;

        _panHelper.Clamp();
        _renderer.SetOffset(_panHelper.CurrentOffset);
    }
}