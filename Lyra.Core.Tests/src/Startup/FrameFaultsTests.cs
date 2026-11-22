using Lyra.SdlCore;
using Xunit;

namespace Lyra.Core.Tests.Startup;

public class FrameFaultsTests
{
    [Fact]
    public void FailuresAreSkipped_UntilARunReachesTheLimit()
    {
        var faults = new FrameFaults(limit: 3);

        Assert.True(faults.TrySkip());
        Assert.True(faults.TrySkip());
        Assert.False(faults.TrySkip());
    }

    [Fact]
    public void AGoodFrame_EndsTheRun()
    {
        var faults = new FrameFaults(limit: 2);

        Assert.True(faults.TrySkip());
        faults.Succeeded();

        Assert.True(faults.TrySkip());
        Assert.Equal(1, faults.InARow);
    }
}
