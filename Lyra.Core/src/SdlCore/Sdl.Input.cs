using Lyra.FileLoader.Navigation;
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
            { Scancode.U, ToggleSidebar },
            { Scancode.Return, OpenFileExplorer }
        };
    }

    private void HandleScancode(Scancode scancode, Keymod mods)
    {
        var absoluteModifier = OperatingSystem.IsMacOS() && (mods & Keymod.GUI) != 0;
        var edgeModifier = OperatingSystem.IsMacOS()
            ? (mods & Keymod.Alt) != 0
            : (mods & Keymod.Ctrl) != 0;

        if (absoluteModifier)
        {
            if (scancode == Scancode.Left)
                FirstImage();
            else if (scancode == Scancode.Right) 
                LastImage();

            return;
        }

        if (edgeModifier)
        {
            // In duplicates mode the edge modifier steps between groups
            // (decrease / increase the group id) instead of directory edges.
            if (DirectoryNavigator.IsDuplicatesMode)
            {
                if (scancode == Scancode.Left)
                    PreviousGroup();
                else if (scancode == Scancode.Right)
                    NextGroup();
            }
            else
            {
                if (scancode == Scancode.Left)
                    MoveToLeftEdge();
                else if (scancode == Scancode.Right)
                    MoveToRightEdge();
            }

            return;
        }

        if (_scanActions.TryGetValue(scancode, out var scanAction))
            scanAction.Invoke();
    }

    private void HandleEscape()
    {
        if (_dropProgressTracker.GetDropStatus().Active)
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
            LoadImage(NavigationDirection.Forward);
        }
    }

    private void PreviousImage()
    {
        if (DirectoryNavigator.HasPrevious())
        {
            DirectoryNavigator.MoveToPrevious();
            LoadImage(NavigationDirection.Backward);
        }
    }

    private void FirstImage()
    {
        if (!DirectoryNavigator.IsFirst())
        {
            DirectoryNavigator.MoveToFirst();
            // Correct - if the first image was deleted, the direction should be forward.
            LoadImage(NavigationDirection.Forward);
        }
    }

    private void LastImage()
    {
        if (!DirectoryNavigator.IsLast())
        {
            DirectoryNavigator.MoveToLast();
            // Correct - if the first image was deleted, the direction should be backward.
            LoadImage(NavigationDirection.Backward);
        }
    }

    private void MoveToLeftEdge()
    {
        if (!DirectoryNavigator.IsFirst())
        {
            DirectoryNavigator.MoveToLeftEdge();
            LoadImage(NavigationDirection.Backward);
        }
    }

    private void MoveToRightEdge()
    {
        if (!DirectoryNavigator.IsLast())
        {
            DirectoryNavigator.MoveToRightEdge();
            LoadImage(NavigationDirection.Forward);
        }
    }

    private void NextGroup()
    {
        if (DirectoryNavigator.MoveToNextGroup())
            LoadImage(NavigationDirection.Forward);
    }

    private void PreviousGroup()
    {
        if (DirectoryNavigator.MoveToPreviousGroup())
            LoadImage(NavigationDirection.Backward);
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
        if (_renderer.IsCompositeVector)
            return;

        _viewState.ToggleSampling();
    }

    private void ToggleBackground() => _viewState.ToggleBackground();

    private void ToggleInfo() => _viewState.ToggleInfo();

    private void ToggleHelp() => _viewState.ToggleHelp();

    private void ToggleSidebar() => _viewState.ToggleSidebar();

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
            _displayMode = DimensionHelper.GetDisplayMode(_window, _composite, _viewState.InitDisplayMode, out _zoomPercentage);
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

        var scale = DimensionHelper.GetPixelDensity(_window);
        ZoomAnchored(newZoom, new SKPoint(mouseX * scale, mouseY * scale));
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

        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (newZoom == _zoomPercentage)
            return;
        
        var drawable = DimensionHelper.GetDrawableSize(_window);
        ZoomAnchored(newZoom, new SKPoint(drawable.PixelWidth / 2f, drawable.PixelHeight / 2f));
    }
    
    private void ZoomAnchored(int newZoom, SKPoint anchorPixels)
    {
        _panHelper!.UpdateZoom(_zoomPercentage);
        var newOffset = _panHelper.GetOffsetForZoomAtCursor(anchorPixels, newZoom);

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
}