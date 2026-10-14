using Lyra.Renderer.Backends;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>A hidden window is not drawn into, but never given up on.</summary>
public class HiddenWindowGateTests
{
    private const long Retry = HiddenWindowGate.RetryIntervalMs;

    [Fact]
    public void AVisibleWindowAlwaysDraws()
    {
        var gate = new HiddenWindowGate();

        Assert.True(gate.ShouldDraw(visible: true, nowMs: 0));
        Assert.True(gate.ShouldDraw(visible: true, nowMs: 1));
        Assert.True(gate.ShouldDraw(visible: true, nowMs: 100_000));
    }

    [Fact]
    public void AHiddenWindowIsSkipped()
    {
        var gate = new HiddenWindowGate();

        Assert.False(gate.ShouldDraw(visible: false, nowMs: 1000));
        Assert.False(gate.ShouldDraw(visible: false, nowMs: 1000 + Retry - 1));
    }

    [Fact]
    public void AWindowThatStaysHiddenIsTriedOncePerInterval()
    {
        var gate = new HiddenWindowGate();

        Assert.False(gate.ShouldDraw(visible: false, nowMs: 0));
        Assert.True(gate.ShouldDraw(visible: false, nowMs: Retry));
        Assert.False(gate.ShouldDraw(visible: false, nowMs: Retry + 1));
        Assert.False(gate.ShouldDraw(visible: false, nowMs: 2 * Retry - 1));
        Assert.True(gate.ShouldDraw(visible: false, nowMs: 2 * Retry));
    }

    [Fact]
    public void BecomingVisibleRestartsTheInterval()
    {
        var gate = new HiddenWindowGate();

        Assert.False(gate.ShouldDraw(visible: false, nowMs: 0));
        Assert.True(gate.ShouldDraw(visible: true, nowMs: Retry - 10));

        // Hidden again: the earlier hidden stretch does not count toward the retry.
        Assert.False(gate.ShouldDraw(visible: false, nowMs: Retry));
        Assert.False(gate.ShouldDraw(visible: false, nowMs: 2 * Retry - 1));
        Assert.True(gate.ShouldDraw(visible: false, nowMs: 2 * Retry));
    }
}
