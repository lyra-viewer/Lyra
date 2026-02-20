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
    private const int MinZoom = 1;
    private const int MaxZoom = 10000;

    private void InitializeInput()
    {
        _scanActions = new Dictionary<Scancode, Action>
        {
            { Scancode.Escape, HandleEscape },
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
        var command = (mods & (Keymod.Ctrl | Keymod.GUI)) != 0;
        var option = (mods & Keymod.Alt) != 0;

        if (command)
        {
            switch (scancode)
            {
                case Scancode.Left:
                    FirstImage();
                    return;
                case Scancode.Right:
                    LastImage();
                    return;
            }
        }
        else if (option)
        {
            switch (scancode)
            {
                case Scancode.Left:
                    MoveToLeftEdge();
                    return;
                case Scancode.Right:
                    MoveToRightEdge();
                    return;
            }
        }
        else if (_scanActions.TryGetValue(scancode, out var scanAction))
        {
            scanAction.Invoke();
        }
    }
    
    private void HandleEscape()
    {
        if (_dropStats.GetDropStatus().Active)
        {
            CancelDrop();
            return;
        }

        ExitApplication();
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

    private void MoveToLeftEdge()
    {
        if (!DirectoryNavigator.IsFirst())
        {
            DirectoryNavigator.MoveToLeftEdge();
            LoadImage();
        }
    }

    private void MoveToRightEdge()
    {
        if (!DirectoryNavigator.IsLast())
        {
            DirectoryNavigator.MoveToRightEdge();
            LoadImage();
        }
    }
    
    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private int _lastWindowX;
    private int _lastWindowY;
    
    private void ToggleFullscreen() => SetFullscreen(!_isFullscreen);
    
    private void SetFullscreen(bool fullscreen)
    {
        if (fullscreen == _isFullscreen)
            return;
        
        if (fullscreen)
        {
            GetWindowSize(_window, out _lastWindowWidth, out _lastWindowHeight);
            GetWindowPosition(_window, out _lastWindowX, out _lastWindowY);

            DeferUntilWarm(() =>
            {
                // SetWindowBordered(_window, false);
                // SetWindowResizable(_window, false);
                SetWindowFullscreen(_window, true);
                SetWindowPosition(_window, 0, 0);
            });
        }
        else
        {
            DeferUntilWarm(() =>
            {
                // SetWindowBordered(_window, true);
                // SetWindowResizable(_window, true);
                SetWindowFullscreen(_window, false);
                SetWindowSize(_window, _lastWindowWidth, _lastWindowHeight);
                SetWindowPosition(_window, _lastWindowX, _lastWindowY);
            });
        }
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
        _renderer.ToggleHelpBar();
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

    private void ZoomIn() => ApplyZoom(GetNextZoom(_zoomPercentage, +1));

    private void ZoomOut() => ApplyZoom(GetNextZoom(_zoomPercentage, -1));

    private void ZoomAtPoint(float mouseX, float mouseY, float direction)
    {
        if (_composite == null || _composite.IsEmpty || _panHelper == null)
            return;

        var newZoom = GetNextZoom(_zoomPercentage, direction);
        if (newZoom == _zoomPercentage)
            return;

        var scale = GetWindowDisplayScale(_window);
        var mouse = new SKPoint(mouseX * scale, mouseY * scale);

        _panHelper.UpdateZoom(_zoomPercentage);
        var newOffset = _panHelper.GetOffsetForZoomAtCursor(mouse, newZoom);

        _zoomPercentage = newZoom;
        _displayMode = _zoomPercentage == 100
            ? DisplayMode.OriginalImageSize
            : DisplayMode.Free;

        _renderer.SetDisplayMode(_displayMode);
        _renderer.SetZoom(_zoomPercentage);

        _panHelper.UpdateZoom(_zoomPercentage);
        _panHelper.CurrentOffset = newOffset;
        _panHelper.Clamp();
        _renderer.SetOffset(_panHelper.CurrentOffset);
    }
    
    private static int GetNextZoom(int currentZoom, float direction)
    {
        // direction > 0 → zoom in
        // direction < 0 → zoom out

        var candidate = direction > 0
            ? (int)MathF.Round(currentZoom * ZoomFactor, MidpointRounding.AwayFromZero)
            : (int)MathF.Round(currentZoom / ZoomFactor, MidpointRounding.AwayFromZero);

        candidate = Math.Clamp(candidate, MinZoom, MaxZoom);

        // Force monotonic progress (prevents rounding stalls)
        if (direction > 0 && candidate <= currentZoom)
            candidate = Math.Min(MaxZoom, currentZoom + 1);

        if (direction < 0 && candidate >= currentZoom)
            candidate = Math.Max(MinZoom, currentZoom - 1);

        return candidate;
    }

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

    private void UpdateFitToScreen()
    {
        if (_composite == null || _composite.IsEmpty)
            return;

        _zoomPercentage = DimensionHelper.GetZoomToFitScreen(_window, _composite.LogicalWidth, _composite.LogicalHeight);
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