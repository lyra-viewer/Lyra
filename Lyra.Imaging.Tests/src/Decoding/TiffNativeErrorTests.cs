using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class TiffNativeErrorTests : IDisposable
{
    private readonly string _corrupt = Path.Combine(Path.GetTempPath(), $"lyra-corrupt-{Guid.NewGuid():N}.tif");

    private static readonly Lazy<bool> Native = new(() => NativeWrapper.TryLoad("libtiff_native", typeof(TiffNative).Assembly));

    [Fact]
    public void ARefusedFileStillSaysWhy()
    {
        Assert.SkipUnless(Native.Value, "libtiff_native not loadable");
        
        var bytes = new byte[512];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        bytes[2] = 42;
        File.WriteAllBytes(_corrupt, bytes);

        var thrown = Assert.Throws<InvalidOperationException>(() => new TiffDecoder().DecodeThumbnail(_corrupt, 64, TestContext.Current.CancellationToken));

        var reason = thrown.Message[(thrown.Message.IndexOf(_corrupt, StringComparison.Ordinal) + _corrupt.Length)..].TrimStart('.', ' ');

        Assert.False(string.IsNullOrWhiteSpace(reason), $"no reason survived from the read thread; got: {thrown.Message}");
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_corrupt);
        }
        catch
        {
            // Best effort.
        }
    }
}
