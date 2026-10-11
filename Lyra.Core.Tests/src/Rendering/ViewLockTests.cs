using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.SdlCore;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

public class ViewLockTests
{
    private static ViewState State(InitDisplayMode mode) => new(UISettings.DefaultUiSettings with { InitDisplayMode = mode });

    [Fact]
    public void TogglingTheLockTwiceRestoresTheModeItReplaced()
    {
        var state = State(InitDisplayMode.FitAll);

        state.ToggleViewLock();
        Assert.True(state.ViewLocked);

        state.ToggleViewLock();
        Assert.Equal(InitDisplayMode.FitAll, state.InitDisplayMode);
    }

    [Fact]
    public void UnlockingRestoresTheModeLastPickedFromTheDropdown()
    {
        var state = State(InitDisplayMode.FitAll);

        state.SetInitDisplayMode(InitDisplayMode.ActualSize);
        state.SetInitDisplayMode(InitDisplayMode.Locked);
        state.ToggleViewLock();

        Assert.Equal(InitDisplayMode.ActualSize, state.InitDisplayMode);
    }

    [Fact]
    public void StartingLockedUnlocksToTheDefaultMode()
    {
        var state = State(InitDisplayMode.Locked);

        state.ToggleViewLock();

        Assert.Equal(UISettings.DefaultUiSettings.InitDisplayMode, state.InitDisplayMode);
    }

    [Theory]
    [InlineData(100f, 50f, DisplayMode.OriginalImageSize)]
    [InlineData(50f, 50f, DisplayMode.FitToScreen)]
    [InlineData(50.01f, 50f, DisplayMode.FitToScreen)]
    [InlineData(75f, 50f, DisplayMode.Free)]
    [InlineData(75f, 0f, DisplayMode.Free)]
    public void AnInheritedZoomIsNamedByWhatItIsOnTheNewImage(float zoom, float fitZoom, DisplayMode expected)
    {
        Assert.Equal(expected, DimensionHelper.GetLockedDisplayMode(zoom, fitZoom));
    }

    // --------------------------------------------------------
    //  Carrying the view to the next image
    // --------------------------------------------------------

    private static readonly ViewLock.View Zoomed = new(250f, new SKPoint(40, -12));

    [Fact]
    public void TheNextImageInheritsTheViewItWasLeftAt()
    {
        var viewLock = new ViewLock();

        viewLock.Capture(locked: true, Zoomed);

        Assert.Equal(Zoomed, viewLock.Inherit(locked: true));
    }

    [Fact]
    public void NothingIsCarriedWhileUnlocked()
    {
        var viewLock = new ViewLock();

        viewLock.Capture(locked: false, Zoomed);

        Assert.Null(viewLock.Inherit(locked: true));
    }

    [Fact]
    public void SkippingPastAnImageStillLoadingKeepsTheView()
    {
        // The loading image has no view of its own; it must not wipe the one in flight.
        var viewLock = new ViewLock();

        viewLock.Capture(locked: true, Zoomed);
        viewLock.Capture(locked: true, outgoing: null);

        Assert.Equal(Zoomed, viewLock.Inherit(locked: true));
    }

    [Fact]
    public void UnlockingForgetsTheViewSoARelockStartsFresh()
    {
        var viewLock = new ViewLock();

        viewLock.Capture(locked: true, Zoomed);
        viewLock.Release();

        Assert.Null(viewLock.Inherit(locked: true));
    }

    [Fact]
    public void AnUnlockedViewerIgnoresAPendingView()
    {
        var viewLock = new ViewLock();

        viewLock.Capture(locked: true, Zoomed);

        Assert.Null(viewLock.Inherit(locked: false));
    }
}
