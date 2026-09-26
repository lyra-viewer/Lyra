using Lyra.Imaging.Content;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

/// <summary>When a load steps aside for the image on screen - and, as much, when it must not.</summary>
public class ForegroundYieldRuleTests : IDisposable
{
    private readonly TempFile _file = new([0]);
    private readonly List<Composite> _composites = [];

    public void Dispose()
    {
        foreach (var composite in _composites)
            composite.Dispose();

        _file.Dispose();
    }

    private Composite Image(CompositeState state)
    {
        var composite = new Composite(new FileInfo(_file.Path));
        _composites.Add(composite);

        if (state != CompositeState.Pending)
            composite.BeginLoadTiming();

        composite.State = state;
        return composite;
    }

    [Fact]
    public void AnotherImageLoadingOnScreen_IsWaitedFor()
    {
        Assert.True(ImageLoader.ShouldYield(Image(CompositeState.Loading), Image(CompositeState.Loading), yieldAfterMs: 0));
    }

    [Fact]
    public void AnImageOnScreenThatIsNotYetStarted_IsNotWaitedFor()
    {
        Assert.False(ImageLoader.ShouldYield(Image(CompositeState.Pending), Image(CompositeState.Loading), yieldAfterMs: 0));
    }

    [Fact]
    public void TheLoadOnScreen_NeverWaitsOnItself()
    {
        var onScreen = Image(CompositeState.Loading);

        Assert.False(ImageLoader.ShouldYield(onScreen, onScreen, yieldAfterMs: 0));
    }

    [Fact]
    public void AnOrdinaryLoadStillInsideTheGrace_IsNotWaitedFor()
    {
        Assert.False(ImageLoader.ShouldYield(Image(CompositeState.Loading), Image(CompositeState.Loading), yieldAfterMs: 60_000));
    }

    [Theory]
    [InlineData(CompositeState.Ready)]
    [InlineData(CompositeState.Complete)]
    [InlineData(CompositeState.Failed)]
    [InlineData(CompositeState.Cancelled)]
    public void AnImageOnScreenNoLongerLoading_IsNotWaitedFor(CompositeState state)
    {
        Assert.False(ImageLoader.ShouldYield(Image(state), Image(CompositeState.Loading), yieldAfterMs: 0));
    }

    [Fact]
    public void NothingOnScreen_IsNotWaitedFor()
    {
        Assert.False(ImageLoader.ShouldYield(null, Image(CompositeState.Loading), yieldAfterMs: 0));
    }
}
