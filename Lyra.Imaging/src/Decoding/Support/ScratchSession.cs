using Lyra.Common;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Marks the scratch files of a run that is still going, so another run can tell what is left over
/// from what is in use.
/// </summary>
internal static class ScratchSession
{
    internal const string LockSuffix = ".lock";

    private static readonly Lock Gate = new();

    private static string? _token;

    /// <summary>Where the mark for <see cref="_token"/> was made.</summary>
    private static string? _directory;

    /// <summary>Held for the life of the process. Never read; existing under lock is its whole job.</summary>
    private static FileStream? _held;

    /// <summary>This run's token once it has claimed one, null before that and if it could not.</summary>
    public static string? CurrentToken
    {
        get
        {
            lock (Gate)
                return _token;
        }
    }

    /// <summary>
    /// Claims a token for this run, creating the file that marks it as running.
    /// </summary>
    /// <returns>
    /// The token, or null when the mark could not be made - in which case the caller must not
    /// write scratch files at all, since nothing would stop another run from sweeping them away
    /// mid-decode.
    /// </returns>
    public static string? Claim(string directory)
    {
        lock (Gate)
        {
            if (_token is not null && string.Equals(_directory, directory, StringComparison.Ordinal))
                return _token;
            
            Release();

            var token = Guid.NewGuid().ToString("N");
            var path = System.IO.Path.Combine(directory, token + LockSuffix);

            try
            {
                // DeleteOnClose so a clean exit leaves nothing behind; a kill leaves the file but
                // not the lock, which the sweep reads as ended just the same.
                _held = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);

                _token = token;
                _directory = directory;

                return _token;
            }
            catch (Exception ex)
            {
                Logger.Info($"[ScratchSession] Could not mark this run as running in {directory}: {ex.Message}; decoding from source paths instead.");
                return null;
            }
        }
    }

    private static void Release()
    {
        try
        {
            _held?.Dispose(); // DeleteOnClose takes the file with it
        }
        catch (Exception ex)
        {
            Logger.Debug($"[ScratchSession] Could not release the mark for {_token}: {ex.Message}");
        }

        _held = null;
        _token = null;
        _directory = null;
    }

    /// <summary>
    /// Whether the run that owns <paramref name="lockPath"/> is over - true when the file can be
    /// opened exclusively, or is not there at all.
    /// </summary>
    public static bool HasEnded(string lockPath)
    {
        try
        {
            using var _ = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
            return true;
        }
        catch (FileNotFoundException)
        {
            return true; // nothing marks that run anymore
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (IOException)
        {
            return false; // still held
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
