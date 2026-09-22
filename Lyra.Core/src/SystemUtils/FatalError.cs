using Lyra.Common;
using static SDL3.SDL;

namespace Lyra.SystemUtils;

internal static class FatalError
{
    private const string Title = "Lyra Viewer";

    public static void Report(string summary, Exception cause)
    {
        Logger.Error($"[Fatal] {summary} {cause}");

        var detail = Describe(cause);
        var logFile = TryGetLogFile();

        var report = logFile is null
            ? $"{summary}\n\n{detail}"
            : $"{summary}\n\n{detail}\n\nThe full details are in:\n{logFile}";

        WriteToStandardError(report);

        Show(report);
    }

    private static void Show(string report)
    {
        try
        {
            if (!ShowSimpleMessageBox(MessageBoxFlags.Error, Title, report, IntPtr.Zero))
                Logger.Warning($"[Fatal] The error dialog could not be shown: {GetError()}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"[Fatal] The error dialog could not be shown: {ex.Message}");
        }
    }

    internal static string Describe(Exception cause)
    {
        var innermost = cause;
        while (innermost.InnerException is not null)
            innermost = innermost.InnerException;

        var outer = $"{cause.GetType().Name}: {cause.Message}";

        return ReferenceEquals(innermost, cause)
            ? outer
            : $"{outer}\n{innermost.GetType().Name}: {innermost.Message}";
    }

    private static string? TryGetLogFile()
    {
        try
        {
            return LyraIO.GetLogFile();
        }
        catch (Exception ex)
        {
            Logger.Warning($"[Fatal] Could not resolve the log file path: {ex.Message}");
            return null;
        }
    }

    private static void WriteToStandardError(string report)
    {
        try
        {
            Console.Error.WriteLine(report);
        }
        catch (IOException)
        {
            // No console attached.
        }
    }
}