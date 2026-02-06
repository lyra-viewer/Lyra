namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration;

/// <summary>
/// Per-channel 8-bit lookup tables used as a fast approximation of an ICC transform.
/// <list type="bullet">
///   <item><description>Contains one 256-entry LUT per RGB channel.</description></item>
///   <item><description><see cref="Identity"/> returns a shared identity LUT instance.</description></item>
///   <item><description><see cref="IsIdentity"/> uses reference equality for a fast-path check.</description></item>
/// </list>
/// <para>
/// The LUT arrays are treated as immutable. Do not modify the arrays returned by this type.
/// </para>
/// </summary>
public readonly record struct RgbLuts(byte[] R, byte[] G, byte[] B)
{
    private static readonly byte[] IdentityArray = BuildIdentityArray();
    private static readonly RgbLuts IdentityLuts = new(IdentityArray, IdentityArray, IdentityArray);

    public static RgbLuts Identity() => IdentityLuts;

    public bool IsIdentity => ReferenceEquals(R, IdentityArray) && ReferenceEquals(G, IdentityArray) && ReferenceEquals(B, IdentityArray);

    private static byte[] BuildIdentityArray()
    {
        var arr = new byte[256];
        for (var i = 0; i < 256; i++)
            arr[i] = (byte)i;
        
        return arr;
    }
}