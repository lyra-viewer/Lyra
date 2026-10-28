using Lyra.GUI;

namespace Lyra.SdlCore;

// Duplicates UI orchestration: the scan itself lives in DuplicateScanService (SDL-free);
// this partial only switches duplicates mode, refreshes the view, and reflects section state.
public partial class SdlCore
{
    private void OnDuplicatesExactOnlyChanged(bool exactOnly) => _duplicateScanService.ExactOnly = exactOnly;

    private void OnDuplicatesHashToleranceChanged(int tolerance) => _duplicateScanService.HashTolerance = tolerance;

    private void OnFindDuplicatesRequested()
    {
        // "New Search"/"Regroup": scan the full collection, never the subset.
        if (CurrentViewMode() == ViewMode.Duplicates)
            TryLeaveViewMode();

        _ui.SetNoDuplicatesFound(false);
        _duplicateScanService.Start();
    }

    private void OnDuplicatesGoBack()
    {
        if (CurrentViewMode() == ViewMode.Duplicates)
            TryLeaveViewMode();
    }

    /// <summary>Scan aborted (raised on the scan thread) - drop back to the pre-scan state.</summary>
    private void OnDuplicateScanAborted()
    {
        DispatchToMain(() => _ui.SetNoDuplicatesFound(false));
    }

    /// <summary>Scan finished (raised on the scan thread) - apply the result on the main thread.</summary>
    private void OnDuplicateScanCompleted(int groupCount)
    {
        DispatchToMain(() =>
        {
            var entered = groupCount > 0 && TryEnterDuplicatesView();
            _ui.SetNoDuplicatesFound(!entered);
        });
    }
}