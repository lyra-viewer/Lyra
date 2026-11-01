using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Decoding;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>How work a decode leaves running ends up recorded on the image it belongs to.</summary>
public class BackgroundWorkTests : IDisposable
{
    private readonly TempFile _file = new([0]);
    private readonly List<Composite> _composites = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var composite in _composites)
            composite.Dispose();

        _file.Dispose();
    }

    /// <summary>An image already on screen, as when a decode hands work to the background.</summary>
    private (Composite Composite, DecodeContext Context) Shown()
    {
        var composite = new Composite(new FileInfo(_file.Path));
        _composites.Add(composite);
        composite.MoveTo(CompositeState.Ready);

        return (composite, DecodeContext.For(composite));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinishedWork_CompletesTheImage(bool onThreadPool)
    {
        var (composite, context) = Shown();

        context.ContinueInBackground(_ => Task.CompletedTask, onThreadPool ? BackgroundSchedule.ThreadPool : BackgroundSchedule.WithLoad, Ct);
        await composite.BackgroundDecodeTask;

        Assert.Equal(CompositeState.Complete, composite.State);
        Assert.Null(composite.Warning);
    }

    [Fact]
    public async Task FailedWork_LeavesWhatWasShown_WithAWarning()
    {
        var (composite, context) = Shown();

        context.ContinueInBackground(_ => throw new InvalidDataException("Tile row 3 is corrupt."), BackgroundSchedule.ThreadPool, Ct);
        await composite.BackgroundDecodeTask;

        Assert.Equal(CompositeState.Complete, composite.State);
        Assert.Equal(LoadWarning.PartiallyDecoded("Tile row 3 is corrupt"), composite.Warning);
    }

    [Fact]
    public async Task FailedWork_KeepsAWarningTheDecodeAlreadyGave()
    {
        var (composite, context) = Shown();
        var earlier = LoadWarning.PartiallyDecoded("The file ends before the image does");
        composite.Warning = earlier;

        context.ContinueInBackground(_ => throw new InvalidDataException("later"), BackgroundSchedule.ThreadPool, Ct);
        await composite.BackgroundDecodeTask;

        Assert.Same(earlier, composite.Warning);
    }

    [Fact]
    public async Task CancelledWork_MarksTheLoadCancelled_AndNeverCompletesIt()
    {
        var (composite, context) = Shown();
        var completed = false;
        composite.Completed += _ => completed = true;

        context.ContinueInBackground(_ => throw new OperationCanceledException(), BackgroundSchedule.WithLoad, Ct);
        await composite.BackgroundDecodeTask;

        Assert.Equal(CompositeState.Cancelled, composite.State);
        Assert.False(completed);
    }

    [Fact]
    public async Task WorkHandedToTheBackground_RunsEvenWhenTheLoadIsAlreadyCancelled()
    {
        var (composite, context) = Shown();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var released = false;
        context.ContinueInBackground(token =>
        {
            released = true;
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, BackgroundSchedule.WithLoad, cancelled.Token);

        await composite.BackgroundDecodeTask;

        Assert.True(released);
        Assert.Equal(CompositeState.Cancelled, composite.State);
    }

    [Fact]
    public async Task ADecodeContinuesInTheBackgroundOnlyOnce()
    {
        var (composite, context) = Shown();
        context.ContinueInBackground(_ => Task.CompletedTask, BackgroundSchedule.ThreadPool, Ct);

        Assert.Throws<InvalidOperationException>(() => context.ContinueInBackground(_ => Task.CompletedTask, BackgroundSchedule.ThreadPool, Ct));

        await composite.BackgroundDecodeTask;
    }
}
