using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

public class CompositeStateTests : IDisposable
{
    private readonly TempFile _file = new([0]);
    private readonly List<Composite> _composites = [];

    public void Dispose()
    {
        foreach (var composite in _composites)
            composite.Dispose();

        _file.Dispose();
    }

    private Composite Image(CompositeState state = CompositeState.Pending)
    {
        var composite = new Composite(new FileInfo(_file.Path));
        _composites.Add(composite);
        composite.MoveTo(state);
        return composite;
    }

    public static TheoryData<CompositeState> Ended => new()
    {
        CompositeState.Complete,
        CompositeState.Failed,
        CompositeState.Cancelled
    };

    [Theory]
    [MemberData(nameof(Ended))]
    public void AnEndedLoad_MovesOnlyToDisposed(CompositeState ended)
    {
        foreach (var to in Enum.GetValues<CompositeState>())
            Assert.Equal(to == CompositeState.Disposed, Composite.CanMove(ended, to));
    }

    [Fact]
    public void ADisposedComposite_NeverMovesAgain()
    {
        foreach (var to in Enum.GetValues<CompositeState>())
            Assert.False(Composite.CanMove(CompositeState.Disposed, to));
    }

    [Fact]
    public void ACancelledLoad_IsNotOverwrittenByALateReadySignal()
    {
        var composite = Image(CompositeState.Cancelled);

        composite.SignalReady();
        composite.SignalComplete();

        Assert.Equal(CompositeState.Cancelled, composite.State);
    }

    [Fact]
    public void AFinishedLoad_IsNotTurnedIntoACancelledOne()
    {
        var composite = Image(CompositeState.Complete);

        Assert.False(composite.TryTransition(CompositeState.Cancelled));
        Assert.Equal(CompositeState.Complete, composite.State);
    }

    [Fact]
    public void ADisposedComposite_CannotFail_AndKeepsNoFailure()
    {
        var composite = Image();
        composite.Dispose();

        Assert.False(composite.Fail(new LoadFailure(LoadFailureKind.DecodeFailed, "late")));
        Assert.Equal(CompositeState.Disposed, composite.State);
        Assert.Null(composite.Failure);
    }

    [Fact]
    public void TheFirstFailure_IsTheOneKept()
    {
        var composite = Image(CompositeState.Loading);

        Assert.True(composite.Fail(new LoadFailure(LoadFailureKind.DecodeFailed, "first")));
        Assert.False(composite.Fail(new LoadFailure(LoadFailureKind.NotFound, "second")));

        Assert.Equal(CompositeState.Failed, composite.State);
        Assert.Equal("first", composite.Failure?.Detail);
    }

    [Fact]
    public void DisposingTwice_DisposesTheContentOnce()
    {
        var composite = Image(CompositeState.Complete);
        var content = new CountingContent();
        composite.Content = content;

        composite.Dispose();
        composite.Dispose();

        Assert.Equal(1, content.Disposals);
        Assert.Null(composite.Content);
    }

    [Fact]
    public void RacingTransitions_EndInExactlyOneTerminalState()
    {
        for (var round = 0; round < 200; round++)
        {
            var composite = Image(CompositeState.Loading);
            using var start = new Barrier(3);

            var outcomes = new bool[3];
            var racers = new[]
            {
                new Thread(() => { start.SignalAndWait(); outcomes[0] = composite.TryTransition(CompositeState.Complete); }),
                new Thread(() => { start.SignalAndWait(); outcomes[1] = composite.Fail(new LoadFailure(LoadFailureKind.DecodeFailed, "raced")); }),
                new Thread(() => { start.SignalAndWait(); outcomes[2] = composite.TryTransition(CompositeState.Cancelled); })
            };

            foreach (var racer in racers)
                racer.Start();

            foreach (var racer in racers)
                racer.Join();

            Assert.Equal(1, outcomes.Count(won => won));
            Assert.Equal(composite.State == CompositeState.Failed, composite.Failure is not null);
        }
    }

    [Fact]
    public void BothSidesOfTheFullSize_ArriveTogether()
    {
        var composite = Image();

        composite.SetFullSize(4000, 3000);

        Assert.Equal((4000f, 3000f), (composite.FullWidth, composite.FullHeight));
    }

    private sealed class CountingContent : ICompositeContent
    {
        public int Disposals;

        public bool IsResolutionIndependent => false;
        public float? DecodedWidth => 1;
        public float? DecodedHeight => 1;

        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
}