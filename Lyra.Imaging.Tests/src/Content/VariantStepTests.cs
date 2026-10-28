using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Variants;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>Stepping through a file's renditions in index order, stopping at either end.</summary>
public class VariantStepTests
{
    private sealed class FakeContent : ICompositeContent
    {
        public bool IsResolutionIndependent => false;
        public float? DecodedWidth => 10;
        public float? DecodedHeight => 10;
        public void Dispose() { }
    }

    private static VariantContainer Set(int count, int active) =>
        new([.. Enumerable.Range(0, count).Select(i => new ImageVariant($"{i}", 10, 10, "", 64))],
            [.. Enumerable.Range(0, count).Select(_ => (ICompositeContent)new FakeContent())],
            active)
        {
            StepOrder = [.. Enumerable.Range(0, count)]
        };

    [Fact]
    public void PagesStepInOrderAndStopAtTheEnds()
    {
        var set = Set(3, active: 0);

        Assert.Equal([0, 1, 2], set.StepOrder);
        Assert.Null(set.StepFrom(-1));
        Assert.Equal(1, set.StepFrom(+1));

        Assert.True(set.Select(2));
        Assert.Equal(1, set.StepFrom(-1));
        Assert.Null(set.StepFrom(+1));
    }

    [Fact]
    public void ASingleRenditionHasNowhereToStep()
    {
        var set = Set(1, active: 0);

        Assert.Null(set.StepFrom(+1));
        Assert.Null(set.StepFrom(-1));
    }
}
