using Lyra.Imaging.Content;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>
/// Regression tests for the trim-while-drawing race: a rendition landing on a background thread
/// used to let Trim dispose the rendition the drawing thread had just taken from Active and was
/// still drawing, which reaches Skia through a freed handle. Trimmed renditions must now be held
/// until the drawing thread releases them at a frame boundary.
/// </summary>
public class VariantRetirementTests
{
    private sealed class FakeContent(long bytes = 64) : ICompositeContent
    {
        public bool Disposed { get; private set; }

        public bool IsResolutionIndependent => false;
        public float? DecodedWidth => 10;
        public float? DecodedHeight => 10;
        public long ByteSize => bytes;

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeProvider(Func<int, ICompositeContent> make) : IVariantProvider
    {
        public ICompositeContent Decode(int index, CancellationToken ct) => make(index);
    }

    private static IReadOnlyList<ImageVariant> Variants(int count) => [.. Enumerable.Range(0, count).Select(i => new ImageVariant($"Page {i + 1}", 100 + i, 200 + i, "detail", 64))];

    /// <summary>Selects <paramref name="index"/> and returns once the fetch has published it.</summary>
    private static void SelectAndWait(VariantRasterContent set, int index)
    {
        using var published = new ManualResetEventSlim(false);

        void OnReady(VariantRasterContent _) => published.Set();

        set.VariantReady += OnReady;
        try
        {
            Assert.True(set.Select(index));
            Assert.True(published.Wait(TimeSpan.FromSeconds(10)), "the rendition never arrived");
        }
        finally
        {
            set.VariantReady -= OnReady;
        }
    }

    [Fact]
    public void TrimmedRendition_SurvivesUntilTheDrawingThreadReleasesIt()
    {
        var first = new FakeContent();
        var second = new FakeContent();

        // A budget below one rendition, so every fetch trims whatever is no longer shown.
        var set = new VariantRasterContent(Variants(2), active: 0, first, new FakeProvider(_ => second), residentByteBudget: 1);

        SelectAndWait(set, 1);

        Assert.Same(second, set.Active);
        Assert.False(first.Disposed); // still the reference a frame in flight could be drawing

        set.ReleaseRetired();

        Assert.True(first.Disposed);
        Assert.False(second.Disposed); // the one on screen is never trimmed, whatever the budget
    }

    [Fact]
    public void ReleaseRetired_IsANoOpWhenNothingWasTrimmed()
    {
        var only = new FakeContent();
        var set = new VariantRasterContent(Variants(1), active: 0, only, new FakeProvider(_ => new FakeContent()), residentByteBudget: 1);

        set.ReleaseRetired();
        set.ReleaseRetired();

        Assert.False(only.Disposed);
        Assert.Same(only, set.Active);
    }

    [Fact]
    public void Dispose_FreesRenditionsThatWereTrimmedButNeverReleased()
    {
        var first = new FakeContent();
        var second = new FakeContent();

        var set = new VariantRasterContent(Variants(2), active: 0, first, new FakeProvider(_ => second), residentByteBudget: 1);

        SelectAndWait(set, 1);
        Assert.False(first.Disposed);

        set.Dispose(); // never drawn again, so nothing ever released it

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public void DecodedSize_ComesFromTheDescription_NotFromLiveContent()
    {
        var set = new VariantRasterContent(Variants(2), active: 0, new FakeContent(), new FakeProvider(_ => new FakeContent()), residentByteBudget: 1);

        Assert.Equal(100, set.DecodedWidth);
        Assert.Equal(200, set.DecodedHeight);

        SelectAndWait(set, 1);

        Assert.Equal(101, set.DecodedWidth);
        Assert.Equal(201, set.DecodedHeight);

        set.Dispose();
    }

    [Fact]
    public void EagerForm_DisposesEveryRendition()
    {
        List<ICompositeContent> contents = [new FakeContent(), new FakeContent(), new FakeContent()];
        var set = new VariantRasterContent(Variants(3), contents, active: 1);

        Assert.Equal(101, set.DecodedWidth);

        set.Dispose();

        Assert.All(contents, content => Assert.True(((FakeContent)content).Disposed));
    }
}
