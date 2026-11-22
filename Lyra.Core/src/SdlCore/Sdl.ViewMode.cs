using Lyra.GUI;

namespace Lyra.SdlCore;

// The mode itself lives in ViewerSession; these reload the image after it changes. Main thread only.
public partial class SdlCore
{
    private ViewMode CurrentViewMode() => _session.ViewMode;

    /// <summary>Enters duplicates view from the default mode. Returns false if nothing is grouped.</summary>
    private bool TryEnterDuplicatesView()
    {
        if (!_session.TryEnterDuplicatesView())
            return false;

        LoadImage();
        return true;
    }

    /// <summary>Leaves the current mode one level (Back). Returns false in the default mode.</summary>
    private bool TryLeaveViewMode()
    {
        if (!_session.TryLeaveViewMode())
            return false;

        LoadImage();
        return true;
    }
}