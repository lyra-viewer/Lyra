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
        SettingsManager.LoadSettings();

        // NOTE: Some earlier Debug logs might escape capture in Release builds due to execution order.
        if (SettingsManager.AppSettings.Debug)
            Logger.SetLogDebugMode(true);

        try
        {
            using var viewer = new SdlCore.SdlCore();
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
        Logger.SetLogStrategy(Logger.LogStrategy.File);
#endif
        Logger.ClearLog();
    }
}