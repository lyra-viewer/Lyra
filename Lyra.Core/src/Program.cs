using System.Runtime.InteropServices;
using Lyra.Common;
using Lyra.Common.Settings;

namespace Lyra;

static class Program
{
    private static void Main()
    {
        LogSetup();
        Logger.Info($"[Application] Application started on {RuntimeInformation.RuntimeIdentifier}");

        NativeLibraryLoader.Initialize();
        var appSettings = SettingsManager.LoadAppSettings();
        var userSettings = appSettings.PreserveUiSettings
            ? SettingsManager.LoadUiSettings()
            : UiSettings.DefaultUiSettings;

        try
        {
            using var viewer = new SdlCore.SdlCore(appSettings, userSettings);
            viewer.Run();
        }
        catch (Exception ex)
        {
            Logger.Error($"[Unhandled Exception]: {ex}");
        }
    }

    private static void LogSetup()
    {
#if DEBUG
        Logger.SetLogDebugMode(true);
        Logger.SetLogStrategy(Logger.LogStrategy.Both);
#else
        Logger.SetLogDebugMode(false);
        Logger.SetLogStrategy(Logger.LogStrategy.File);
#endif
        Logger.ClearLog();
    }
}