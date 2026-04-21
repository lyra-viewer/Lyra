using Lyra.FileLoader;
using Lyra.Imaging.Content;

namespace Lyra.Renderer.GUI;

public sealed record UIState(
    Composite? Composite,
    CompositeState CompositeState,
    ApplicationStates AppStates,
    DirectorySnapshot Directories,
    DirectoryNavigator.Navigation Navigation)
{
    public static UIState Create(
        Composite? composite,
        ApplicationStates appStates,
        DirectorySnapshot directories,
        DirectoryNavigator.Navigation navigation) =>
        new(composite,
            composite?.State ?? CompositeState.Disposed,
            appStates,
            directories,
            navigation
        );
}