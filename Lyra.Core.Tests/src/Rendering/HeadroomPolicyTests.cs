using Lyra.Renderer.Display;
using Lyra.Renderer.Drawing;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// How much headroom the display transform offers to spend, which decides whether EDR starts at
/// all.
///
/// Spending only what the panel currently grants deadlocks: macOS opens the budget once content
/// above SDR white is on screen, and the granted value reads 1.0 until it does. Emit nothing above
/// white, never earn the budget, keep reading 1.0.
/// </summary>
public class HeadroomPolicyTests
{
    private static DisplayCapabilities Panel(double potential, double current) => DisplayCapabilities.Create(1, "panel", potential, current, reference: 1);

    /// A capable panel that has granted nothing yet must still be offered more than white, or the
    /// ramp has no reason to start.
    [Fact]
    public void ACapablePanelIsOfferedMoreThanWhite_BeforeItGrantsAnything()
    {
        var atStartup = Panel(potential: 16, current: 1);

        Assert.True(HeadroomPolicy.Spendable(atStartup) > SurfaceProfile.SdrWhite, "nothing above white would ever be drawn, so macOS would never open the budget.");
    }

    /// Once the budget is open, follow it - that is what the panel says it will actually show.
    [Theory]
    [InlineData(4.805)]
    [InlineData(8)]
    [InlineData(16)]
    public void AGrantedBudgetIsSpentInFull(double granted)
    {
        Assert.Equal((float)granted, HeadroomPolicy.Spendable(Panel(potential: 16, current: granted)), 3);
    }

    /// An SDR panel is offered nothing however the question is asked: highlights would only be
    /// clipped, and the SDR curve is the better rendering there.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 1.2)]
    public void AnSdrPanelIsNeverOfferedHeadroom(double potential, double current)
    {
        Assert.Equal(SurfaceProfile.SdrWhite, HeadroomPolicy.Spendable(Panel(potential, current)));
    }

    /// A panel that can only do a little never gets asked for more than it has.
    [Fact]
    public void AModestPanelIsNotAskedForMoreThanItHas()
    {
        var modest = Panel(potential: 1.4, current: 1);

        Assert.Equal(1.4f, HeadroomPolicy.Spendable(modest), 3);
    }

    /// Across the measured ramp - 1.0 to 4.805 over about a second - the offer never decreases,
    /// so highlights brighten steadily.
    [Fact]
    public void TheOfferNeverGoesBackwardsAcrossTheRamp()
    {
        var previous = 0f;
        for (var step = 0; step <= 62; step++)
        {
            var current = 1.0 + (4.805 - 1.0) * step / 62;
            var spendable = HeadroomPolicy.Spendable(Panel(potential: 16, current));

            Assert.True(spendable >= previous, $"step {step} went backwards: {previous} then {spendable}.");
            previous = spendable;
        }

        Assert.Equal(4.805f, previous, 3);
    }

    /// The bootstrap is a nudge, not a claim on the whole panel: overshoot is clipped, so it stays
    /// small enough to be a brief flattening rather than a flash.
    [Fact]
    public void TheBootstrapIsModest()
    {
        var spendable = HeadroomPolicy.Spendable(Panel(potential: 16, current: 1));

        Assert.InRange(spendable, 1.1f, 3f);
    }
}