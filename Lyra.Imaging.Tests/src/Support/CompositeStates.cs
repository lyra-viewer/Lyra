using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;

namespace Lyra.Imaging.Tests.Support;

internal static class CompositeStates
{
    /// <summary>Walks a fresh composite to <paramref name="target"/> the way a load would.</summary>
    public static void MoveTo(this Composite composite, CompositeState target)
    {
        CompositeState[] path = target switch
        {
            CompositeState.Pending => [],
            CompositeState.Ready => [CompositeState.Loading, CompositeState.Ready],
            CompositeState.Complete => [CompositeState.Loading, CompositeState.Complete],
            _ => [target]
        };

        foreach (var step in path)
        {
            if (!composite.TryTransition(step))
                throw new InvalidOperationException($"{composite.State} cannot move to {step}.");
        }
    }
}