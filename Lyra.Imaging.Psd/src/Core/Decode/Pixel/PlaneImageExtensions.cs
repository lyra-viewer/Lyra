namespace Lyra.Imaging.Psd.Core.Decode.Pixel;

public static class PlaneImageExtensions
{
    public static bool TryGetPlane(this in PlaneImage img, PlaneRole role, out Plane plane)
    {
        // Avoid LINQ
        for (var i = 0; i < img.Planes.Count; i++)
        {
            if (img.Planes[i].Role == role)
            {
                plane = img.Planes[i];
                return true;
            }
        }

        plane = default;
        return false;
    }

    public static Plane GetPlaneOrThrow(this in PlaneImage img, PlaneRole role)
    {
        return img.TryGetPlane(role, out var p) ? p : throw new InvalidOperationException($"Missing required plane: {role}");
    }
}