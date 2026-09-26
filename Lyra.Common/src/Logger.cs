namespace Lyra.Common;

public static class Logger
{
    private static readonly string LogFilePath = LyraIO.GetLogFile();
    private static readonly string PreviousLogFilePath = LyraIO.GetPreviousLogFile();

    private const long MaxLogFileSize = 5 * 1024 * 1024; // 5 MB max log size

    private static readonly Lock Lock = new();

    private static LogStrategy _currentStrategy = LogStrategy.Console;
    private static bool _debugMode;

    private static string _lastDebugMessage = string.Empty;

    public static void Info(string message) => LogInternal(message, LogLevelInternal.Info, false);
    public static void Warning(string message) => LogInternal(message, LogLevelInternal.Warn, false);
    public static void Error(string message) => LogInternal(message, LogLevelInternal.Error, false);
    public static void Debug(string message, bool preventRepeat = false)
    {
        if (_debugMode)
            LogInternal(message, LogLevelInternal.Debug, preventRepeat);
    }

    public static void Info(Type source, string message) => Info(Tagged(source, message));
    public static void Warning(Type source, string message) => Warning(Tagged(source, message));
    public static void Error(Type source, string message) => Error(Tagged(source, message));
    public static void Debug(Type source, string message, bool preventRepeat = false) => Debug(Tagged(source, message), preventRepeat);

    internal static string Tagged(Type source, string message)
    {
        var name = source.Name;
        var arity = name.IndexOf('`');

        return $"[{(arity < 0 ? name : name[..arity])}] {message}";
    }

    private static void LogInternal(string message, LogLevelInternal level, bool preventRepeat)
    {
        if (_currentStrategy == LogStrategy.Disabled)
            return;

        lock (Lock) // Ensure thread safety
        {
            if(level == LogLevelInternal.Debug)
                if (preventRepeat && _lastDebugMessage == message)
                    return;
                else
                    _lastDebugMessage = message;

            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            
            if (_currentStrategy is LogStrategy.Console or LogStrategy.Both)
            {
                Console.ForegroundColor = level switch
                {
                    LogLevelInternal.Info => ConsoleColor.White,
                    LogLevelInternal.Warn => ConsoleColor.Yellow,
                    LogLevelInternal.Error => ConsoleColor.Red,
                    LogLevelInternal.Debug => ConsoleColor.DarkGreen,
                    _ => ConsoleColor.Gray
                };

                Console.WriteLine(logEntry);
                Console.ResetColor();
            }

            if (_currentStrategy is LogStrategy.File or LogStrategy.Both)
            {
                try
                {
                    // Rotated rather than truncated.
                    if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogFileSize)
                        RotateWhileLocked();

                    File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Logger] Failed to write to log: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }
    }

    /// <summary>
    /// Begins a fresh log for this run, keeping the last one as
    /// <see cref="LyraIO.GetPreviousLogFile"/>.
    /// </summary>
    public static void StartNewLog()
    {
        lock (Lock)
            RotateWhileLocked();
    }
    
    private static void RotateWhileLocked()
    {
        if (TryKeepAsPrevious(LogFilePath, PreviousLogFilePath))
            return;

        try
        {
            File.WriteAllText(LogFilePath, string.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Logger] Failed to clear log: {ex.Message}");
        }
    }
    
    internal static bool TryKeepAsPrevious(string current, string previous)
    {
        try
        {
            if (!File.Exists(current))
                return true;

            File.Move(current, previous, overwrite: true);
            Console.WriteLine($"[Logger] Previous log kept as {previous}");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Logger] Could not keep the previous log: {ex.Message}");
            return false;
        }
    }

    public static void SetLogStrategy(LogStrategy strategy)
    {
        _currentStrategy = strategy;
    }

    public static void SetLogDebugMode(bool debugMode)
    {
        _debugMode = debugMode;
    }

    public enum LogStrategy
    {
        Disabled,
        Console,
        File,
        Both
    }

    private enum LogLevelInternal
    {
        Info,
        Warn,
        Error,
        Debug
    }
}