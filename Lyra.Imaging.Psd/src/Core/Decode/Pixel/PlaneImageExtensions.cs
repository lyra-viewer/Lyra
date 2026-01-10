namespace Lyra.Imaging.Psd.Core.Decode.Pixel;

public static class PlaneImageExtensions
{
    public static bool TryGetPlane(this in PlaneImage img, PlaneRole role, out Plane plane)
    {
        // Avoid LINQ
        foreach (var p in img.Planes)
        {
            if (p.Role != role)
                continue;

            plane = p;
            return true;
        }

        plane = default;
        return false;
    }

    public static Plane GetPlaneOrThrow(this in PlaneImage img, PlaneRole role)
    {
        return !img.TryGetPlane(role, out var p)
            ? throw new InvalidOperationException($"Missing required plane: {role}")
            : p;
    }
}