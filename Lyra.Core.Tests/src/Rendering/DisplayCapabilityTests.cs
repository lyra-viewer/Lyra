using Lyra.Renderer.Display;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// The rules the display capability service runs on, tested without a display: which number decides
/// that an extended surface is worth creating, what happens to a value AppKit did not supply, and
/// how much of the headroom ramp is worth reporting onward.
/// </summary>
public class DisplayCapabilityTests
{
    [Fact]
    public void SdrDisplay_OffersNothingAboveWhite()
    {
        Assert.False(DisplayCapabilities.Sdr.SupportsExtendedRange);
        Assert.False(DisplayCapabilities.Sdr.HasHeadroomNow);
    }
    
    [Fact]
    public void PanelWithHeadroom_SupportsExtendedRange_EvenWhileTheCurrentValueIsStillOne()
    {
        var atStartup = DisplayCapabilities.Create(1, "Built-in Retina Display", potential: 16, current: 1.0, reference: 1.0);

        Assert.True(atStartup.SupportsExtendedRange);
        Assert.False(atStartup.HasHeadroomNow);
    }

    [Fact]
    public void ExternalSdrPanel_SupportsNothing_HoweverItIsAsked()
    {
        var external = DisplayCapabilities.Create(2, "LS34A650U", potential: 1, current: 1, reference: 0);

        Assert.False(external.SupportsExtendedRange);
        Assert.False(external.HasHeadroomNow);
    }

    [Fact]
    public void RealValues_SurviveIntact()
    {
        var ramped = DisplayCapabilities.Create(1, "Built-in Retina Display", potential: 16, current: 4.805, reference: 1.6);

        Assert.Equal(16f, ramped.PotentialHeadroom);
        Assert.Equal(4.805f, ramped.CurrentHeadroom, 3);
        Assert.Equal(1.6f, ramped.ReferenceHeadroom, 3);
        Assert.True(ramped.HasHeadroomNow);
    }
    
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ValuesThatAreNotAMultiplierOverWhite_BecomeExactlyOne(double raw)
    {
        var capabilities = DisplayCapabilities.Create(1, "screen", potential: raw, current: raw, reference: raw);

        Assert.Equal(1f, capabilities.PotentialHeadroom);
        Assert.Equal(1f, capabilities.CurrentHeadroom);
        Assert.Equal(1f, capabilities.ReferenceHeadroom);
        Assert.False(capabilities.SupportsExtendedRange);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ADisplayWithoutAName_StillHasOne(string? name)
    {
        Assert.Equal("unknown", DisplayCapabilities.Create(1, name, 1, 1, 1).DisplayName);
    }
    
    [Fact]
    public void CurrentAbovePotential_IsReportedAsGiven()
    {
        var odd = DisplayCapabilities.Create(1, "screen", potential: 2, current: 5, reference: 1);

        Assert.Equal(5f, odd.CurrentHeadroom);
        Assert.Equal(2f, odd.PotentialHeadroom);
    }

    [Fact]
    public void TheFirstSampleAlwaysReports()
    {
        var tracker = new HeadroomTracker();

        Assert.True(tracker.Observe(DisplayCapabilities.Sdr));
    }

    [Fact]
    public void AnUnchangedSampleReportsNothing()
    {
        var tracker = new HeadroomTracker();
        var sample = DisplayCapabilities.Create(1, "screen", 16, 1, 1);

        tracker.Observe(sample);

        Assert.False(tracker.Observe(sample));
    }

    [Fact]
    public void ASmallMoveIsNotReported_ButIsStillTheCurrentValue()
    {
        var tracker = new HeadroomTracker();
        tracker.Observe(DisplayCapabilities.Create(1, "screen", 16, 4.0, 1));

        var nudged = DisplayCapabilities.Create(1, "screen", 16, 4.05, 1);

        Assert.False(tracker.Observe(nudged));
        Assert.Equal(4.05f, tracker.Current.CurrentHeadroom, 3);
    }

    [Fact]
    public void ALargeMoveIsReported()
    {
        var tracker = new HeadroomTracker();
        tracker.Observe(DisplayCapabilities.Create(1, "screen", 16, 1.0, 1));

        Assert.True(tracker.Observe(DisplayCapabilities.Create(1, "screen", 16, 2.5, 1)));
    }
    
    [Fact]
    public void MovingToAnotherDisplayIsReported_EvenWithIdenticalHeadroom()
    {
        var tracker = new HeadroomTracker();
        tracker.Observe(DisplayCapabilities.Create(1, "Built-in Retina Display", 16, 1, 1));

        Assert.True(tracker.Observe(DisplayCapabilities.Create(2, "LS34A650U", 16, 1, 1)));
    }

    [Fact]
    public void APanelGainingOrLosingItsHeadroomIsReported()
    {
        var tracker = new HeadroomTracker();
        tracker.Observe(DisplayCapabilities.Create(1, "screen", potential: 16, current: 1, reference: 1));

        Assert.True(tracker.Observe(DisplayCapabilities.Create(1, "screen", potential: 1, current: 1, reference: 1)));
    }
    
    [Fact]
    public void CrossingIntoExtendedRangeIsReported_HoweverSmallTheMove()
    {
        var tracker = new HeadroomTracker();
        tracker.Observe(DisplayCapabilities.Create(1, "screen", potential: 1, current: 1, reference: 1));

        var barely = DisplayCapabilities.Create(1, "screen", potential: 1.02, current: 1, reference: 1);

        Assert.True(barely.SupportsExtendedRange);
        Assert.True(tracker.Observe(barely));
    }
    
    [Fact]
    public void TheMeasuredRamp_IsFollowedExactly_AndReportedSparingly()
    {
        var tracker = new HeadroomTracker();
        const int steps = 62;
        const double top = 4.805;

        var reports = 0;
        for (var step = 0; step <= steps; step++)
        {
            var current = 1.0 + (top - 1.0) * step / steps;
            if (tracker.Observe(DisplayCapabilities.Create(1, "screen", 16, current, 1)))
                reports++;
        }

        Assert.Equal((float)top, tracker.Current.CurrentHeadroom, 3);
        Assert.InRange(reports, 1, steps / 2);
    }
}