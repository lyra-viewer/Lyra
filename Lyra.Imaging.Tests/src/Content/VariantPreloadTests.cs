using System.Collections.Concurrent;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Variants;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>
/// Neighbours decoded ahead, one at a time and only within the budget; a request for the one on its
/// way waits for it; and lent content outlives being trimmed or the container going away.
/// </summary>
public class VariantPreloadTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private sealed class FakeContent(long bytes = 64) : ICompositeContent
    {
        public bool IsDisposed { get; private set; }
        public bool IsResolutionIndependent => false;
        public float? DecodedWidth => 10;
        public float? DecodedHeight => 10;
        public long ByteSize => bytes;
        public void Dispose() => IsDisposed = true;
    }

    /// <summary>Decodes on request, counting each; can be held so a decode stays in flight.</summary>
    private sealed class Provider(long bytes = 64) : IVariantProvider
    {
        public ConcurrentQueue<int> Decoded { get; } = new();
        public ManualResetEventSlim Release { get; } = new(true);
        public ManualResetEventSlim Started { get; } = new(false);
        public ConcurrentQueue<int> Cancelled { get; } = new();

        public ICompositeContent Decode(int index, CancellationToken ct)
        {
            Started.Set();

            try
            {
                Release.Wait(ct);
            }
            catch (OperationCanceledException)
            {
                Cancelled.Enqueue(index);
                throw;
            }

            Decoded.Enqueue(index);
            return new FakeContent(bytes);
        }
    }

    private static IReadOnlyList<ImageVariant> Variants(int count, int size = 0) => 
        [.. Enumerable.Range(0, count).Select(i => new ImageVariant($"{i}", size, size, "", 64))];

    private static VariantContainer Container(Provider provider, int count, int active = 0, long budget = 1 << 20, bool preloads = true, int size = 0) =>
        new(Variants(count, size), active, new FakeContent(), provider, budget) { PreloadsNeighbours = preloads };

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the condition never held");
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void NothingIsPreloadedUntilTheContainerIsShown()
    {
        var provider = new Provider();
        using var set = Container(provider, 3);

        Thread.Sleep(100);
        Assert.Empty(provider.Decoded);

        set.StartPreloading();
        WaitFor(() => provider.Decoded.Contains(1));

        Assert.True(set.Select(1));
        Assert.False(set.IsWaiting);
        Assert.Equal(1, set.ShownIndex);
    }

    [Fact]
    public void BothNeighboursArePreloadedNextFirst()
    {
        var provider = new Provider();
        using var set = Container(provider, 3, active: 1);

        set.StartPreloading();
        WaitFor(() => provider.Decoded.Count == 2);

        Assert.Equal([2, 0], provider.Decoded);
    }

    [Fact]
    public void AContainerThatDoesNotPreloadDecodesOnlyWhatIsAskedFor()
    {
        var provider = new Provider();
        using var set = Container(provider, 3, preloads: false);

        set.StartPreloading();
        Thread.Sleep(100);

        Assert.Empty(provider.Decoded);
    }

    [Fact]
    public void NothingIsPreloadedThatWouldNotFitTheBudget()
    {
        var provider = new Provider();

        // Known to be 1000 x 1000, four megabytes, against a budget of one.
        using var set = Container(provider, 3, budget: 1 << 20, size: 1000);

        set.StartPreloading();
        Thread.Sleep(100);

        Assert.Empty(provider.Decoded);
    }

    [Fact]
    public void AskingForTheRenditionBeingPreloadedWaitsForItRatherThanStartingAgain()
    {
        var provider = new Provider();
        provider.Release.Reset();
        using var set = Container(provider, 3);

        set.StartPreloading();
        Assert.True(provider.Started.Wait(Patience));

        Assert.True(set.Select(1));
        var wait = Assert.IsType<VariantWait>(set.Waiting);
        Assert.Equal(1, wait.Index);

        using var shown = new ManualResetEventSlim();
        set.VariantReady += _ => shown.Set();
        provider.Release.Set();

        Assert.True(shown.Wait(Patience));
        Assert.Equal(1, set.ShownIndex);
        Assert.Null(set.Waiting);
        Assert.Equal(1, provider.Decoded.Count(index => index == 1));
    }

    [Fact]
    public void AskingForAnotherRenditionCancelsThePreload()
    {
        var provider = new Provider();
        provider.Release.Reset();
        using var set = Container(provider, 5);

        set.StartPreloading();
        Assert.True(provider.Started.Wait(Patience));

        Assert.True(set.Select(3));
        WaitFor(() => provider.Cancelled.Contains(1));

        provider.Release.Set();
        WaitFor(() => set.ShownIndex == 3);
    }

    [Fact]
    public void LentContentOutlivesTheContainer()
    {
        var provider = new Provider();
        var set = Container(provider, 2);

        var lease = set.Borrow(0);
        Assert.NotNull(lease);
        Assert.Null(set.Borrow(1));

        var content = Assert.IsType<FakeContent>(lease.Content);
        set.Dispose();
        Assert.False(content.IsDisposed);

        lease.Dispose();
        Assert.True(content.IsDisposed);

        // Returning twice does nothing more.
        lease.Dispose();
    }

    [Fact]
    public void LentContentThatIsTrimmedIsReleasedOnlyOnceReturned()
    {
        var provider = new Provider(bytes: 600);
        using var set = new VariantContainer(Variants(3), 0, new FakeContent(600), provider, residentByteBudget: 1000);

        var lease = set.Borrow(0)!;
        var content = (FakeContent)lease.Content;

        using var shown = new ManualResetEventSlim();
        set.VariantReady += _ => shown.Set();
        Assert.True(set.Select(1));
        Assert.True(shown.Wait(Patience));

        // Over budget, so the first was trimmed; lent, so it survives the release pass.
        set.ReleaseRetired();
        Assert.False(content.IsDisposed);

        lease.Dispose();
        set.ReleaseRetired();
        Assert.True(content.IsDisposed);
    }
}