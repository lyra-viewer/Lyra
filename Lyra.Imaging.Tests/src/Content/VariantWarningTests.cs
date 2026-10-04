using Lyra.Imaging.Content;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

public class VariantWarningTests
{
    private static readonly LoadWarning Damaged = LoadWarning.PartiallyDecoded("Frame 2 holds 1 of its 4 pixels");

    private sealed class FakeContent : ICompositeContent
    {
        public bool IsResolutionIndependent => false;
        public float? DecodedWidth => 10;
        public float? DecodedHeight => 10;
        public long ByteSize => 64;

        public void Dispose() { }
    }

    private static VariantRasterContent Set(int count)
    {
        var variants = Enumerable.Range(0, count).Select(i => new ImageVariant($"Frame {i + 1}", 10, 10, "detail", 64)).ToList();
        return new VariantRasterContent(variants, variants.Select(ICompositeContent (_) => new FakeContent()).ToList(), active: 0);
    }

    [Fact]
    public void AWarning_IsKeptPerIndex_AndMovesTheVersionOnlyWhenItChanges()
    {
        using var set = Set(3);
        var start = set.WarningVersion;

        set.RecordWarning(1, Damaged);
        var recorded = set.WarningVersion;

        set.RecordWarning(1, Damaged);

        Assert.Equal(Damaged, set.WarningOf(1));
        Assert.Null(set.WarningOf(0));
        Assert.NotEqual(start, recorded);
        Assert.Equal(recorded, set.WarningVersion);
    }

    [Fact]
    public void AWarning_StaysWhenItsRenditionIsShown()
    {
        using var set = Set(3);
        set.RecordWarning(2, Damaged);

        Assert.True(set.Select(2));

        Assert.Equal(Damaged, set.WarningOf(2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void AWarningOutOfRange_IsIgnored(int index)
    {
        using var set = Set(3);
        var start = set.WarningVersion;

        set.RecordWarning(index, Damaged);

        Assert.Equal(start, set.WarningVersion);
    }
}
