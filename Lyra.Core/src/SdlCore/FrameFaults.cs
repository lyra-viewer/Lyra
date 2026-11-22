namespace Lyra.SdlCore;

/// <summary>Decides whether a failed frame is skipped or ends the app: a run of failures means it will not recover.</summary>
internal sealed class FrameFaults(int limit = FrameFaults.DefaultLimit)
{
    internal const int DefaultLimit = 10;

    public int InARow { get; private set; }
    
    public bool TrySkip() => ++InARow < limit;

    public void Succeeded() => InARow = 0;
}