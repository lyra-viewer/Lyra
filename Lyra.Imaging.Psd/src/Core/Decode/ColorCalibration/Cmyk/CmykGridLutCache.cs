using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Wacton.Unicolour.Icc;

namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Cmyk;

/// <summary>
/// Thread-safe cache for CMYK grid LUTs, keyed by ICC profile identity + transform parameters.
/// </summary>
internal sealed class CmykGridLutCache
{
    private readonly ConcurrentDictionary<string, Lazy<CmykGridLut>> _cache = new();

    private byte[]? _lastProfileRef;
    private string? _lastProfileHash;
    private readonly Lock _hashLock = new();

    public CmykGridLut GetOrCreate(
        byte[]? embeddedIccProfile,
        string? fallbackProfilePath,
        Intent intent,
        bool invert,
        int gridSize,
        Func<CmykGridLut> factory
    )
    {
        var key = BuildKey(embeddedIccProfile, fallbackProfilePath, intent, invert, gridSize);

        var lazy = _cache.GetOrAdd(key, _ => new Lazy<CmykGridLut>(factory, LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazy.Value;
        }
        catch
        {
            // If the factory threw, remove the faulted Lazy so the next call retries.
            _cache.TryRemove(key, out _);
            throw;
        }
    }

    private string BuildKey(byte[]? embeddedIccProfile, string? fallbackProfilePath, Intent intent, bool invert, int gridSize)
    {
        var sb = new StringBuilder(256);

        sb.Append("intent=").Append(intent)
            .Append("|invert=").Append(invert)
            .Append("|grid=").Append(gridSize);

        if (embeddedIccProfile is { Length: > 0 })
        {
            sb.Append("|icc=");
            sb.Append(GetOrCacheHash(embeddedIccProfile));
        }
        else
        {
            sb.Append("|path=").Append(fallbackProfilePath ?? "<none>");
        }

        return sb.ToString();
    }

    private string GetOrCacheHash(byte[] profile)
    {
        lock (_hashLock)
        {
            if (ReferenceEquals(profile, _lastProfileRef) && _lastProfileHash is not null)
                return _lastProfileHash;

            var hash = Convert.ToHexString(SHA256.HashData(profile));
            _lastProfileRef = profile;
            _lastProfileHash = hash;
            return hash;
        }
    }
}