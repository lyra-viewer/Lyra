using Lyra.FileLoader.Navigation;

namespace Lyra.SdlCore;

// Duplicates UI orchestration: the scan itself lives in DuplicateScanService (SDL-free);
// this partial only switches duplicates mode, refreshes the view, and reflects section state.
public partial class SdlCore
{
    private void OnDuplicatesExactOnlyChanged(bool exactOnly) => _duplicateScanService.ExactOnly = exactOnly;

    private void OnDuplicatesHashToleranceChanged(int tolerance) => _duplicateScanService.HashTolerance = tolerance;

    private void OnFindDuplicatesRequested()
    {
        // "New Search"/"Regroup": leave duplicates mode and restore the full collection before rescanning.
        var wasInDuplicatesMode = DirectoryNavigator.IsDuplicatesMode;
        if (wasInDuplicatesMode)
        {
            DirectoryNavigator.ExitDuplicatesMode();
            LoadImage();
        }

        _renderer.UIManager.SetDuplicatesState(inDuplicatesMode: wasInDuplicatesMode, noDuplicatesFound: false);
        _duplicateScanService.Start();
    }

    private void OnDuplicatesGoBack()
    {
        if (!DirectoryNavigator.IsDuplicatesMode)
            return;

        DirectoryNavigator.ExitDuplicatesMode();
        LoadImage();
        _renderer.UIManager.SetDuplicatesState(inDuplicatesMode: false, noDuplicatesFound: false);
    }

    /// <summary>Scan aborted (raised on the scan thread) - drop back to the pre-scan state.</summary>
    private void OnDuplicateScanAborted()
    {
        DispatchToMain(() => _renderer.UIManager.SetDuplicatesState(inDuplicatesMode: false, noDuplicatesFound: false));
    }

    /// <summary>Scan finished (raised on the scan thread) - apply the result on the main thread.</summary>
    private void OnDuplicateScanCompleted(int groupCount)
    {
        DispatchToMain(() =>
        {
            if (groupCount > 0 && DirectoryNavigator.EnterDuplicatesMode())
            {
                LoadImage();
                _renderer.UIManager.SetDuplicatesState(inDuplicatesMode: true, noDuplicatesFound: false);
            }
            else
            {
                _renderer.UIManager.SetDuplicatesState(inDuplicatesMode: false, noDuplicatesFound: true);
            }
        });
    }
}