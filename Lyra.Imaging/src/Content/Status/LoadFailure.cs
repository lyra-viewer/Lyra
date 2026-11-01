using System.Text.RegularExpressions;
using Lyra.Common;

namespace Lyra.Imaging.Content.Status;

public enum LoadFailureKind
{
    NotFound,
    AccessDenied,
    InUse,
    SourceUnavailable,
    TooLarge,
    DecodeFailed,
    NothingToShow,
    OutOfMemory,
    ComponentUnavailable,   // native library could not be loaded
    Unknown
}

public sealed partial record LoadFailure(LoadFailureKind Kind, string Detail, string? Path = null)
{
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    public string Message => Kind switch
    {
        LoadFailureKind.NotFound          => "File not found",
        LoadFailureKind.AccessDenied      => "Access denied",
        LoadFailureKind.InUse             => "File is in use by another program",
        LoadFailureKind.SourceUnavailable => "Could not read the file",
        LoadFailureKind.TooLarge          => "Image too large to open",
        LoadFailureKind.DecodeFailed      => "Could not decode the image",
        LoadFailureKind.NothingToShow     => "Nothing to show",
        LoadFailureKind.OutOfMemory       => "Not enough memory to open the image",
        _                                 => "Application error"
    };

    /// <summary>The detail as shown to the user.</summary>
    public string Description => Kind switch
    {
        LoadFailureKind.NotFound when Path is not null     => $"File not found: {Path}",
        LoadFailureKind.AccessDenied when Path is not null => $"Access denied: {Path}",
        LoadFailureKind.InUse when Path is not null        => $"In use: {Path}",
        _ when IsApplicationFault                          => $"{ErrorType ?? "Error"}: {Summary(Kind, Detail)}. Full error in the log: {ShortLogPath()}",
        _ => ForDisplay(Detail)
    };

    public string? ErrorType { get; init; }

    public bool IsApplicationFault => Kind is LoadFailureKind.Unknown or LoadFailureKind.ComponentUnavailable;

    /// <summary>Whether loading again might succeed, so the image is retried when it is shown again.</summary>
    public bool IsTransient => Kind is LoadFailureKind.NotFound
        or LoadFailureKind.AccessDenied
        or LoadFailureKind.InUse
        or LoadFailureKind.SourceUnavailable
        or LoadFailureKind.OutOfMemory;
    
    public bool MayBeProgramFault { get; init; }

    /// <summary>Whether the cause lies with the file or its source rather than with the program.</summary>
    public bool IsExpected => !IsApplicationFault && !MayBeProgramFault;

    public static LoadFailure NothingDecoded { get; } = new(LoadFailureKind.DecodeFailed, "The decoder produced no image.");

    public static LoadFailure From(Exception ex, string? path = null)
    {
        var kind = KindOf(ex);

        return new LoadFailure(kind, ex.Message, path)
        {
            MayBeProgramFault = ex is InvalidOperationException and not LoadFailureException,
            ErrorType = kind is LoadFailureKind.Unknown or LoadFailureKind.ComponentUnavailable ? ex.GetType().Name : null
        };
    }

    private static LoadFailureKind KindOf(Exception ex) => ex switch
    {
        LoadFailureException known => known.Kind,
        DllNotFoundException or EntryPointNotFoundException or BadImageFormatException => LoadFailureKind.ComponentUnavailable,
        UnauthorizedAccessException => LoadFailureKind.AccessDenied,
        FileNotFoundException or DirectoryNotFoundException => LoadFailureKind.NotFound,
        EndOfStreamException or InvalidDataException => LoadFailureKind.DecodeFailed,
        IOException io when IsWin32Error(io, SharingViolation) || IsWin32Error(io, LockViolation) => LoadFailureKind.InUse,
        IOException => LoadFailureKind.SourceUnavailable,
        OutOfMemoryException => LoadFailureKind.OutOfMemory,
        OverflowException => LoadFailureKind.TooLarge,
        InvalidOperationException or NotSupportedException or FormatException => LoadFailureKind.DecodeFailed,
        _ => LoadFailureKind.Unknown
    };

    private static bool IsWin32Error(IOException io, int code) => (io.HResult & 0xFFFF) == code && (io.HResult >>> 16) == 0x8007;

    /// <summary>System messages end in a period; the display line does not.</summary>
    private static string ForDisplay(string detail) => detail.Trim().TrimEnd('.').TrimEnd();

    private const int MaxSummaryLength = 140;

    internal static string Summary(LoadFailureKind kind, string detail)
    {
        if (kind == LoadFailureKind.ComponentUnavailable && LibraryName().Match(detail) is { Success: true } library)
        {
            var name = System.IO.Path.GetFileName(library.Groups[1].Value);
            return MissingDependency().Match(detail) is { Success: true } dependency
                ? $"{name} could not be loaded; it needs {System.IO.Path.GetFileName(dependency.Groups["library"].Value)}"
                : $"{name} could not be loaded";
        }

        var line = ForDisplay(detail.Split('\n', 2)[0]);
        return line.Length <= MaxSummaryLength ? line : line[..MaxSummaryLength].TrimEnd() + "…";
    }

    private static string ShortLogPath()
    {
        var path = LyraIO.GetLogFile();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    [GeneratedRegex("^Unable to load (?:shared library|DLL) '([^']+)'")]
    private static partial Regex LibraryName();

    [GeneratedRegex(@"Library not loaded: (?<library>\S+)|(?<library>\S+\.so[\w.]*): cannot open")]
    private static partial Regex MissingDependency();
}

/// <summary>A load failure whose kind the thrower already knows.</summary>
public class LoadFailureException(LoadFailureKind kind, string message, Exception? inner = null) : InvalidOperationException(message, inner)
{
    public LoadFailureKind Kind { get; } = kind;
}
