using System.Collections.Concurrent;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Variants;
using Lyra.Imaging.Decoding.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>
/// A rendition dropped from the budget leaves a reduced copy, shown at once when it is asked for
/// again and replaced when its full decode arrives; a failed decode goes back to the last whole one.
/// </summary>
public class VariantStandInTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private sealed class FakeContent(long bytes, bool reduced = false) : ICompositeContent
    {
        public bool IsReduced => reduced;
        public bool IsDisposed { get; private set; }
        public bool IsResolutionIndependent => false;
        public float? DecodedWidth => 10;
        public float? DecodedHeight => 10;
        public long ByteSize => bytes;
        public void Dispose() => IsDisposed = true;
    }

    /// <summary>Decodes on request; can be held so a decode stays in flight, or told to fail.</summary>
    private sealed class Provider : IVariantProvider
    {
        public ManualResetEventSlim Release { get; } = new(true);
        public ConcurrentDictionary<int, bool> Failing { get; } = new();

        public ICompositeContent Decode(int index, CancellationToken ct)
        {
            Release.Wait(ct);

            if (Failing.ContainsKey(index))
                throw new InvalidDataException("gone");

            return new FakeContent(64);
        }
    }

    private static IReadOnlyList<ImageVariant> Variants(int count) => [.. Enumerable.Range(0, count).Select(i => new ImageVariant($"{i}", 0, 0, "", 64))];

    private static readonly ConcurrentBag<FakeContent> Made = [];

    private static FakeContent Reduce(ICompositeContent _)
    {
        var copy = new FakeContent(16, reduced: true);
        Made.Add(copy);
        return copy;
    }

    /// <summary>Room for one full rendition, so each arrival drops the one before.</summary>
    private static VariantContainer Container(Provider provider, int count, long standInBudget = 1024, bool keepsStandIns = true) =>
        new(Variants(count), 0, new FakeContent(64), provider, residentByteBudget: 100)
        {
            MakeStandIn = keepsStandIns ? Reduce : null,
            StandInBudgetBytes = standInBudget
        };

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
    public void ADroppedRendition_IsShownReducedAtOnce_ThenWhole()
    {
        var provider = new Provider();
        using var set = Container(provider, 2);

        Assert.True(set.Select(1));
        WaitFor(() => set.HasStandIn(0));

        provider.Release.Reset();
        Assert.True(set.Select(0));

        Assert.True(set.IsWaiting);
        Assert.True(set.IsShowingStandIn);
        Assert.Equal(0, set.ShownIndex);
        Assert.True(set.Active is FakeContent { IsReduced: true });

        provider.Release.Set();
        WaitFor(() => !set.IsWaiting);

        Assert.False(set.IsShowingStandIn);
        Assert.False(set.HasStandIn(0));
        Assert.True(set.Active is FakeContent { IsReduced: false });
    }

    [Fact]
    public void AFailedFullDecode_GoesBackToTheLastWholeRendition()
    {
        var provider = new Provider();
        using var set = Container(provider, 2);

        Assert.True(set.Select(1));
        WaitFor(() => set.HasStandIn(0));

        provider.Failing[0] = true;
        Assert.True(set.Select(0));
        WaitFor(() => !set.IsWaiting);

        Assert.Equal(1, set.ShownIndex);
        Assert.Equal(1, set.ActiveIndex);
        Assert.False(set.IsShowingStandIn);
        Assert.False(set.HasStandIn(0));
        Assert.NotNull(set.FailureOf(0));
    }

    [Fact]
    public void AStandInLeftOnScreen_IsNotStrandedWhenTheNextRequestFails()
    {
        var provider = new Provider();
        using var set = Container(provider, 3);

        Assert.True(set.Select(1));
        WaitFor(() => set.HasStandIn(0));

        // Showing 0 reduced, then asking for 2 before 0 arrives; 2 fails.
        provider.Release.Reset();
        provider.Failing[2] = true;
        Assert.True(set.Select(0));
        Assert.True(set.Select(2));
        provider.Release.Set();
        WaitFor(() => !set.IsWaiting);

        Assert.Equal(1, set.ShownIndex);
        Assert.Equal(1, set.ActiveIndex);
        Assert.False(set.IsShowingStandIn);
    }

    [Fact]
    public void StandIns_StayWithinTheirBudget_OldestFirstOut()
    {
        var provider = new Provider();
        using var set = Container(provider, 4, standInBudget: 32);

        for (var i = 1; i < 4; i++)
        {
            Assert.True(set.Select(i));
            var dropped = i - 1;
            WaitFor(() => set.HasStandIn(dropped));
        }

        Assert.False(set.HasStandIn(0));
        Assert.True(set.HasStandIn(1));
        Assert.True(set.HasStandIn(2));
    }

    [Fact]
    public void ASheetCanBorrowAStandIn_OnlyWhenItAsks()
    {
        var provider = new Provider();
        using var set = Container(provider, 2);

        Assert.True(set.Select(1));
        WaitFor(() => set.HasStandIn(0));

        Assert.Null(set.Borrow(0));

        using var lease = set.Borrow(0, orStandIn: true);
        Assert.True(lease?.Content is FakeContent { IsReduced: true });
    }

    [Fact]
    public void AnOfferedStandIn_IsKeptForARenditionNotResident()
    {
        var provider = new Provider();
        using var set = Container(provider, 2);
        using var decoded = new FakeContent(64);

        set.OfferStandIn(0, decoded);
        Assert.False(set.HasStandIn(0));

        set.OfferStandIn(1, decoded);
        Assert.True(set.HasStandIn(1));
        Assert.False(decoded.IsDisposed);
    }

    [Fact]
    public void WithoutAMaker_NoStandInIsKept()
    {
        var provider = new Provider();
        using var set = Container(provider, 2, keepsStandIns: false);

        Assert.True(set.Select(1));
        WaitFor(() => !set.IsWaiting);
        Thread.Sleep(50);

        Assert.False(set.HasStandIn(0));
    }

    [Fact]
    public void Disposing_DisposesTheStandIns()
    {
        var provider = new Provider();
        var set = Container(provider, 2);

        Assert.True(set.Select(1));
        WaitFor(() => set.HasStandIn(0));

        var lease = set.Borrow(0, orStandIn: true)!;
        var standIn = (FakeContent)lease.Content;
        lease.Dispose();

        set.Dispose();
        Assert.True(standIn.IsDisposed);
    }

    [Theory]
    [InlineData(1536L, 384L)]
    [InlineData(2048L, 512L)]
    [InlineData(8192L, 2048L)]
    [InlineData(512L, 256L)]
    public void TheTextureBudget_IsAQuarterOfTheCache_WithinBounds(long cacheMb, long expectedMb)
    {
        const long mb = 1024 * 1024;
        Assert.Equal(expectedMb * mb, DecodePolicy.ResidentTextureBudget(cacheMb * mb));
    }
}