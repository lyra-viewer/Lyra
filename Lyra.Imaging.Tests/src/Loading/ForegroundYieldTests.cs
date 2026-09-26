using System.Diagnostics;
using Lyra.Imaging.Loading;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

/// <summary>Preload work waits between reads while the image on screen is loading, and only then.</summary>
public class ForegroundYieldTests
{
    /// <summary>Cancels a wait that should never have started, so a wrong wait fails rather than hangs.</summary>
    private static CancellationToken Guard() => new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;

    [Fact]
    public void OutsidePreloadWork_NothingWaits()
    {
        ForegroundYield.WaitForForeground(Guard());
    }

    [Fact]
    public void APreloadWaitsUntilTheForegroundIsThrough_AndSaysHowLong()
    {
        var busyUntil = Stopwatch.StartNew();
        var paused = new List<double>();

        using (ForegroundYield.EnterLoad("neighbor.tif", () => busyUntil.ElapsedMilliseconds < 200, paused.Add))
            ForegroundYield.WaitForForeground(TestContext.Current.CancellationToken);

        Assert.True(busyUntil.ElapsedMilliseconds >= 200, "Returned while the foreground was still loading");
        Assert.Single(paused);
        Assert.True(paused[0] >= 150, $"Reported {paused[0]:F0} ms for a wait of about 200");
    }

    [Fact]
    public void APreloadWithNothingToWaitFor_ReportsNoPause()
    {
        var paused = new List<double>();

        using (ForegroundYield.EnterLoad("neighbor.tif", () => false, paused.Add))
            ForegroundYield.WaitForForeground(Guard());

        Assert.Empty(paused);
    }

    [Fact]
    public void CancellingAWaitingPreload_StopsTheWait()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        using (ForegroundYield.EnterLoad("neighbor.tif", () => true, _ => { }))
            Assert.ThrowsAny<OperationCanceledException>(() => ForegroundYield.WaitForForeground(cts.Token));
    }

    [Fact]
    public void LeavingThePreloadScope_EndsIt()
    {
        using (ForegroundYield.EnterLoad("neighbor.tif", () => true, _ => { }))
        {
        }

        ForegroundYield.WaitForForeground(Guard());
    }
}