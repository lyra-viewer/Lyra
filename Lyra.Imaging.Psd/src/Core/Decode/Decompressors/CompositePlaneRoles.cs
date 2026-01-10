using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

internal static class CompositePlaneRoles
{
    public static PlaneRole[] Get(ColorMode mode, int channelCount)
    {
        if (channelCount <= 0)
            throw new InvalidOperationException($"Invalid channelCount: {channelCount}");

        return mode switch
        {
            ColorMode.Rgb => channelCount switch
            {
                3 => new[] { PlaneRole.R, PlaneRole.G, PlaneRole.B },
                _ => new[] { PlaneRole.R, PlaneRole.G, PlaneRole.B, PlaneRole.A } // ignore extras for MVP
            },

            ColorMode.Cmyk => channelCount switch
            {
                4 => new[] { PlaneRole.C, PlaneRole.M, PlaneRole.Y, PlaneRole.K },
                _ => new[] { PlaneRole.C, PlaneRole.M, PlaneRole.Y, PlaneRole.K, PlaneRole.A }
            },

            ColorMode.Grayscale => channelCount switch
            {
                1 => new[] { PlaneRole.Gray },
                _ => new[] { PlaneRole.Gray, PlaneRole.A }
            },

            ColorMode.Indexed => channelCount switch
            {
                1 => new[] { PlaneRole.Index },
                _ => new[] { PlaneRole.Index, PlaneRole.A }
            },

            // MVP: Treat Duotone as grayscale
            ColorMode.Duotone => channelCount switch
            {
                1 => new[] { PlaneRole.Gray },
                _ => new[] { PlaneRole.Gray, PlaneRole.A }
            },

            // MVP: Lab -> L,a,b (+A)
            ColorMode.Lab => channelCount switch
            {
                3 => new[] { PlaneRole.L, PlaneRole.LabA, PlaneRole.LabB },
                _ => new[] { PlaneRole.L, PlaneRole.LabA, PlaneRole.LabB, PlaneRole.A }
            },

            // MVP: Multichannel -> first as Gray, rest as Spot
            ColorMode.Multichannel => BuildMultichannel(channelCount),
            
            ColorMode.Bitmap => throw new NotSupportedException("Bitmap color mode is not compositable and must be handled by a dedicated decoder."),

            _ => throw new NotSupportedException($"ColorMode {mode} not supported.")
        };
    }

    private static PlaneRole[] BuildMultichannel(int channelCount)
    {
        var roles = new PlaneRole[channelCount];
        roles[0] = PlaneRole.Gray;
        for (var i = 1; i < channelCount; i++)
            roles[i] = PlaneRole.Spot;
        
        return roles;
    }
}