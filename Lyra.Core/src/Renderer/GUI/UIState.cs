using Lyra.FileLoader.Navigation;
using Lyra.FileLoader.Store;
using Lyra.Imaging.Content;

namespace Lyra.Renderer.GUI;

public sealed record UIState(
    Composite? Composite,
    CompositeState CompositeState,
    ApplicationStates AppStates,
    DirectorySnapshot Directories,
    DirectoryNavigator.Navigation Navigation,
    FileRecord? CurrentRecord)
{
    public static UIState Create(
        Composite? composite,
        ApplicationStates appStates,
        DirectorySnapshot directories,
        DirectoryNavigator.Navigation navigation,
        FileRecord? currentRecord) =>
        new(composite,
            composite?.State ?? CompositeState.Disposed,
            appStates,
            directories,
            navigation,
            currentRecord
        );
}