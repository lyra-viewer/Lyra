using Lyra.Common;
using Lyra.FileLoader.Navigation;
using Lyra.GUI;

namespace Lyra.SdlCore;

// View mode owner: the only place that writes _viewMode. The UI requests changes and reads
// the mode through ApplicationStates; DirectoryNavigator only carries out the record changes.
// Main thread only.
public partial class SdlCore
{
    private ViewMode _viewMode = ViewMode.Default;

    private ViewMode CurrentViewMode() => _viewMode;

    /// <summary>Enters duplicates view from the default mode. Returns false if nothing is grouped.</summary>
    private bool TryEnterDuplicatesView()
    {
        if (_viewMode != ViewMode.Default || !DirectoryNavigator.ApplyDuplicatesSubset())
            return false;

        _viewMode = ViewMode.Duplicates;
        LoadImage();
        return true;
    }

    /// <summary>Leaves the current mode one level (Back). Returns false in the default mode.</summary>
    private bool TryLeaveViewMode()
    {
        switch (_viewMode)
        {
            case ViewMode.Duplicates:
                DirectoryNavigator.RestoreFullCollection();
                _viewMode = ViewMode.Default;
                LoadImage();
                return true;

            case ViewMode.Default:
            case ViewMode.Container:
            default:
                return false;
        }
    }

    private void ResetViewMode() => _viewMode = ViewMode.Default;

    /// <summary>
    /// Brings the mode and the records back in line after records changed underneath it.
    /// Returns true if the records were changed, so the caller re-validates the cursor.
    /// </summary>
    private bool ReconcileViewMode()
    {
        var hasSubset = DirectoryNavigator.HasDuplicatesSubset;

        switch (_viewMode)
        {
            case ViewMode.Duplicates when !hasSubset:
                Logger.Warning("[ViewMode] Duplicates mode without a duplicates subset; resetting to default.");
                _viewMode = ViewMode.Default;
                return false;

            case ViewMode.Duplicates when DirectoryNavigator.GetCurrent() is null:
                Logger.Info("[ViewMode] No duplicates left; restoring the full collection.");
                DirectoryNavigator.RestoreFullCollection();
                _viewMode = ViewMode.Default;
                return true;

            case not ViewMode.Duplicates when hasSubset:
                Logger.Warning($"[ViewMode] Duplicates subset applied in {_viewMode} mode; restoring the full collection.");
                DirectoryNavigator.RestoreFullCollection();
                return true;

            default:
                return false;
        }
    }
}