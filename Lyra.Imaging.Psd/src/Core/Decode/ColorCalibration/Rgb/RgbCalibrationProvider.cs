using System.Collections.Concurrent;
using Wacton.Unicolour;
using Wacton.Unicolour.Icc;

namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Rgb;

public sealed class RgbCalibrationProvider
{
    private readonly ConcurrentDictionary<(string key, int grid), RgbLuts> _cache = new();

    public RgbLuts GetCalibration(RgbCalibrationRequest req, Func<Configuration, RgbLuts> buildLuts, out string? iccProfileUsed)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(buildLuts);

        iccProfileUsed = null;

        if (!req.PreferColorManagement)
            return RgbLuts.Identity;

        Configuration config;

        if (req.EmbeddedIccProfile is { Length: > 0 })
        {
            var profileKey = Hash(req.EmbeddedIccProfile);
            config = new Configuration(iccConfig: new IccConfiguration(req.EmbeddedIccProfile, Intent.RelativeColorimetric));

            iccProfileUsed = "Embedded ICC Profile";

            return _cache.GetOrAdd((profileKey, req.GridSize), _ => buildLuts(config));
        }

        return RgbLuts.Identity;
    }

    private static string Hash(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
}