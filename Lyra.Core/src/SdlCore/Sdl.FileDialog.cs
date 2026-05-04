using System.Runtime.InteropServices;
using Lyra.Common;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public partial class SdlCore
{
    // SDL keeps a native pointer to the delegate while the dialog is open,
    // so it must stay GC-rooted for that entire time.
    private DialogFileCallback? _fileDialogCallback;
    
    private void OnOpenFileRequested() => ShowDialog(FileDialogType.OpenFile, "Open File");

    private void OnOpenDirectoryRequested() => ShowDialog(FileDialogType.OpenFolder, "Open Directory");

    private void ShowDialog(FileDialogType type, string title)
    {
        if (_fileDialogCallback is not null)
        {
            Logger.Warning("[FileDialog] Open ignored - a dialog is already open.");
            return;
        }

        _fileDialogCallback = OnFileDialogResult;

        var props = CreateProperties();
        try
        {
            SetBooleanProperty(props, Props.FileDialogManyBoolean, true);
            SetPointerProperty(props, Props.FileDialogWindowPointer, _window);
            SetStringProperty(props, Props.FileDialogTitleString, title);

            ShowFileDialogWithProperties(type, _fileDialogCallback, IntPtr.Zero, props);
        }
        finally
        {
            DestroyProperties(props);
        }
    }
    
    private void OnFileDialogResult(IntPtr userdata, IntPtr filelist, int filter)
    {
        try
        {
            if (filelist == IntPtr.Zero)
            {
                Logger.Error($"[FileDialog] Dialog error: {GetError()}");
                return;
            }

            // filelist is a null-terminated array of UTF-8 string pointers.
            // First entry == null means the user cancelled.
            var paths = new List<string>();
            var cursor = filelist;
            while (true)
            {
                var strPtr = Marshal.ReadIntPtr(cursor);
                if (strPtr == IntPtr.Zero)
                    break;

                var s = Marshal.PtrToStringUTF8(strPtr);
                if (!string.IsNullOrWhiteSpace(s))
                    paths.Add(s);

                cursor = IntPtr.Add(cursor, IntPtr.Size);
            }

            if (paths.Count == 0)
                return;

            // Callback may be on an arbitrary thread; marshal ingest back to main.
            DispatchToMain(() => IngestPaths(paths));
        }
        finally
        {
            _fileDialogCallback = null;
        }
    }
}