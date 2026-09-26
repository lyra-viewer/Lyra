namespace Lyra.Imaging.Content;

public enum LoadFailureKind
{
    NotFound,
    AccessDenied,
    InUse,
    SourceUnavailable,
    TooLarge,
    DecodeFailed,
    OutOfMemory,
    Unknown
}

public sealed record LoadFailure(LoadFailureKind Kind, string Detail, string? Path = null)
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
        LoadFailureKind.OutOfMemory       => "Not enough memory to open the image",
        _                                 => "Could not open the image"
    };

    /// <summary>The detail as shown to the user.</summary>
    public string Description => Kind switch
    {
        LoadFailureKind.NotFound when Path is not null     => $"File not found: {Path}",
        LoadFailureKind.AccessDenied when Path is not null => $"Access denied: {Path}",
        LoadFailureKind.InUse when Path is not null        => $"In use: {Path}",
        _ => ForDisplay(Detail)
    };

    /// <summary>Whether loading again might succeed, so the image is retried when it is shown again.</summary>
    public bool IsTransient => Kind is LoadFailureKind.NotFound
        or LoadFailureKind.AccessDenied
        or LoadFailureKind.InUse
        or LoadFailureKind.SourceUnavailable
        or LoadFailureKind.OutOfMemory;

    /// <summary>Whether the cause lies with the file or its source rather than with the program.</summary>
    public bool IsExpected => Kind != LoadFailureKind.Unknown;

    public static LoadFailure NothingDecoded { get; } = new(LoadFailureKind.DecodeFailed, "The decoder produced no image.");

    public static LoadFailure From(Exception ex, string? path = null) => new(KindOf(ex), ex.Message, path);

    private static LoadFailureKind KindOf(Exception ex) => ex switch
    {
        LoadFailureException known => known.Kind,
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
}

/// <summary>A load failure whose kind the thrower already knows.</summary>
public sealed class LoadFailureException(LoadFailureKind kind, string message) : InvalidOperationException(message)
{
    public LoadFailureKind Kind { get; } = kind;
}
