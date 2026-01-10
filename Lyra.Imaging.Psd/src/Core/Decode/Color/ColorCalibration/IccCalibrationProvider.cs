using System.Collections.Concurrent;
using Lyra.Imaging.Psd.Core.Common;
using Wacton.Unicolour;
using Wacton.Unicolour.Icc;

namespace Lyra.Imaging.Psd.Core.Decode.Color.ColorCalibration;

public sealed class IccCalibrationProvider
{
    private readonly ConcurrentDictionary<(string key, int grid), RgbLuts> _cache = new();

    public RgbLuts GetCalibration(ColorCalibrationRequest req, Func<Configuration, RgbLuts> buildLuts, out string? iccProfileUsed)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(buildLuts);

        iccProfileUsed = null;
        
        if (!req.PreferColorManagement)
            return RgbLuts.Identity();

        string profileKey;
        Configuration config;

        if (req.EmbeddedIccProfile is { Length: > 0 })
        {
            profileKey = Hash(req.EmbeddedIccProfile);
            config = new Configuration(iccConfig: new IccConfiguration(req.EmbeddedIccProfile, Intent.RelativeColorimetric));

            iccProfileUsed = "Embedded ICC Profile";
        }
        else
        {
            // Only CMYK has a reasonable OS-default profile fallback at the moment.
            // For RGB, if there's no embedded profile, treat it as already in output space (identity).
            if (req.SourceColorMode != ColorMode.Cmyk)
                return RgbLuts.Identity();

            var path = OsCmykProfileLocator.TryGetDefaultCmykIccPath();
            if (path == null)
                return RgbLuts.Identity();

            profileKey = path;
            config = new Configuration(iccConfig: new IccConfiguration(path, Intent.RelativeColorimetric));
            
            iccProfileUsed = Path.GetFileNameWithoutExtension(path);
        }

        return _cache.GetOrAdd((profileKey, req.GridSize), _ => buildLuts(config));
    }

    private static string Hash(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
}