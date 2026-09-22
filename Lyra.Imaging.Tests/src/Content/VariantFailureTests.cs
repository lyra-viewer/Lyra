using Lyra.Imaging.Content;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>A page that fails to decode must not leave the selection on a page that is not shown.</summary>
public class VariantFailureTests
{
    private sealed class FakeContent : ICompositeContent
    {
        public bool IsResolutionIndependent => false;
        public float? DecodedWidth => 10;
        public float? DecodedHeight => 10;
        public long ByteSize => 64;

        public void Dispose() { }
    }

    private sealed class FakeProvider(Func<int, ICompositeContent> make) : IVariantProvider
    {
        public ICompositeContent Decode(int index, CancellationToken ct) => make(index);
    }

    private static IReadOnlyList<ImageVariant> Variants(int count) => [.. Enumerable.Range(0, count).Select(i => new ImageVariant($"Page {i + 1}", 100, 100, "detail", 64))];

    /// <summary>Selects <paramref name="index"/> and waits for it to arrive or fail; true when it failed.</summary>
    private static bool SelectAndSettle(VariantRasterContent set, int index)
    {
        using var settled = new ManualResetEventSlim(false);
        var failed = false;

        void OnReady(VariantRasterContent _) => settled.Set();
        void OnFailed(VariantRasterContent _)
        {
            failed = true;
            settled.Set();
        }

        set.VariantReady += OnReady;
        set.VariantFailed += OnFailed;

        try
        {
            Assert.True(set.Select(index));
            Assert.True(settled.Wait(TimeSpan.FromSeconds(10)), "the rendition never settled");
            return failed;
        }
        finally
        {
            set.VariantReady -= OnReady;
            set.VariantFailed -= OnFailed;
        }
    }

    [Fact]
    public void AFailedPage_ReturnsTheSelectionToThePageOnScreen()
    {
        var first = new FakeContent();
        using var set = new VariantRasterContent(Variants(3), active: 0, first,
            new FakeProvider(_ => throw new InvalidOperationException("unreadable page")), residentByteBudget: long.MaxValue);

        Assert.True(SelectAndSettle(set, 2));

        Assert.Equal(0, set.ActiveIndex);
        Assert.Equal(0, set.ShownIndex);
        Assert.False(set.IsWaiting);
        Assert.Same(first, set.Active);
    }

    [Fact]
    public void AFailedPage_CanBeAskedForAgain()
    {
        var attempts = 0;
        var second = new FakeContent();

        using var set = new VariantRasterContent(Variants(2), active: 0, new FakeContent(),
            new FakeProvider(_ => ++attempts == 1 ? throw new IOException("share went away") : second), residentByteBudget: long.MaxValue);

        Assert.True(SelectAndSettle(set, 1));
        Assert.False(SelectAndSettle(set, 1));

        Assert.Equal(1, set.ShownIndex);
        Assert.Same(second, set.Active);
    }

    [Fact]
    public void AnArrivedPage_IsTheOneShown()
    {
        using var set = new VariantRasterContent(Variants(2), active: 0, new FakeContent(),
            new FakeProvider(_ => new FakeContent()), residentByteBudget: long.MaxValue);

        Assert.False(SelectAndSettle(set, 1));

        Assert.Equal(1, set.ActiveIndex);
        Assert.Equal(1, set.ShownIndex);
    }

    [Fact]
    public void AFailureAlreadySuperseded_LeavesTheNewerSelectionAlone()
    {
        using var release = new ManualResetEventSlim(false);
        using var failedOnce = new ManualResetEventSlim(false);

        using var set = new VariantRasterContent(Variants(3), active: 0, new FakeContent(),
            new FakeProvider(index =>
            {
                if (index != 1)
                    return new FakeContent();

                release.Wait(TimeSpan.FromSeconds(10));
                failedOnce.Set();
                throw new InvalidOperationException("unreadable page");
            }), residentByteBudget: long.MaxValue);

        var failures = 0;
        set.VariantFailed += _ => Interlocked.Increment(ref failures);

        Assert.True(set.Select(1));
        Assert.False(SelectAndSettle(set, 2));

        release.Set();
        Assert.True(failedOnce.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Thread.Sleep(50); // let the fetch finish publishing after the throw

        Assert.Equal(0, Volatile.Read(ref failures));
        Assert.Equal(2, set.ActiveIndex);
        Assert.Equal(2, set.ShownIndex);
    }
}
