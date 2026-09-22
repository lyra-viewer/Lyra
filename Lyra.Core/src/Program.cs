using System.Runtime.InteropServices;
using Lyra.Common;
using Lyra.Common.Settings;
using Lyra.SystemUtils;

namespace Lyra;

static class Program
{
    private static int Main(string[] args)
    {
        LogSetup();
        Logger.Info($"[Application] Application started on {RuntimeInformation.RuntimeIdentifier}");

        NativeLibraryLoader.Initialize();
        SettingsManager.LoadSettings();

        // NOTE: Some earlier Debug logs might escape capture in Release builds due to execution order.
        if (SettingsManager.AppSettings.Debug)
            Logger.SetLogDebugMode(true);
        
        SdlCore.SdlCore? lyraCore = null;

        try
        {
            lyraCore = new SdlCore.SdlCore(args);
            lyraCore.Run();

            return 0;
        }
        catch (Exception ex)
        {
            FatalError.Report(lyraCore is null ? "Lyra Viewer could not start." : "Lyra Viewer has stopped.", ex);
            return 1;
        }
        finally
        {
            Shutdown(lyraCore);
        }
    }

    private static void Shutdown(IDisposable? viewer)
    {
        try
        {
            viewer?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error($"[Application] Shutting down failed: {ex}");
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
        Logger.StartNewLog();
    }
}