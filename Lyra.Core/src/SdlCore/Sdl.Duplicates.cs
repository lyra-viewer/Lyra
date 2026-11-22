using Lyra.FileLoader.Duplicates;
using Lyra.GUI;

namespace Lyra.SdlCore;

// Duplicates UI orchestration: the scan itself lives in DuplicateScanService (SDL-free) and works
// on a copy; its result is applied here, on the main thread, the only thread that touches the store.
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

        var store = _session.Navigator.Store;
        _duplicateScanService.Start(store.Records, store.Generation);
    }

    private void OnDuplicatesGoBack()
    {
        if (CurrentViewMode() == ViewMode.Duplicates)
            TryLeaveViewMode();
    }

    /// <summary>Scan aborted (raised on the scan thread) - keep what it learned, drop back to the pre-scan state.</summary>
    private void OnDuplicateScanAborted(DuplicateScanResult result)
    {
        DispatchToMain(() =>
        {
            _session.ApplyScan(result, finished: false);
            _ui.SetNoDuplicatesFound(false);
        });
    }

    /// <summary>Scan finished (raised on the scan thread) - apply the result on the main thread.</summary>
    private void OnDuplicateScanCompleted(DuplicateScanResult result)
    {
        DispatchToMain(() =>
        {
            if (!_session.ApplyScan(result, finished: true))
            {
                _ui.SetNoDuplicatesFound(false);
                return;
            }

            var entered = result.GroupCount > 0 && TryEnterDuplicatesView();
            _ui.SetNoDuplicatesFound(!entered);
        });
    }
}