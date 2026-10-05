using Lyra.Imaging.Decoding.Decoders.Animation;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding.Animation;

public class ExclusiveResourceTests
{
    private sealed class Probe : ExclusiveResource
    {
        public int Releases;

        public T Use<T>(Func<T> work) => Exclusive(work);

        protected override void Release() => Releases++;
    }

    [Fact]
    public void ADisposeDuringWork_WaitsForTheWorkToEnd()
    {
        var probe = new Probe();
        using var working = new ManualResetEventSlim(false);
        using var finish = new ManualResetEventSlim(false);

        var work = Task.Run(() => probe.Use(() =>
        {
            working.Set();
            finish.Wait();
            return probe.Releases;
        }));

        working.Wait(TestContext.Current.CancellationToken);
        probe.Dispose();

        Assert.Equal(0, probe.Releases);

        finish.Set();
        Assert.Equal(0, work.GetAwaiter().GetResult());
        Assert.Equal(1, probe.Releases);
    }

    [Fact]
    public void WorkAfterADispose_IsRefusedAsACancellation()
    {
        var probe = new Probe();
        probe.Dispose();

        Assert.Throws<OperationCanceledException>(() => probe.Use(() => 0));
        Assert.Equal(1, probe.Releases);
    }

    [Fact]
    public void FailedWork_StillReleasesADeferredDispose()
    {
        var probe = new Probe();

        Assert.Throws<InvalidOperationException>(() => probe.Use<int>(() =>
        {
            probe.Dispose();
            throw new InvalidOperationException();
        }));

        Assert.Equal(1, probe.Releases);
    }
}
