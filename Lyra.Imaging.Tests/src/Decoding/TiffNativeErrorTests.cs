using System.Runtime.InteropServices;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Interop;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class TiffNativeErrorTests : IDisposable
{
    private readonly string _corrupt = Path.Combine(Path.GetTempPath(), $"lyra-corrupt-{Guid.NewGuid():N}.tif");

    private static readonly Lazy<bool> Native = new(() =>
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "libtiff_native.so"),
                     "/home/nineveh/dev/Lyra/release/native/dist-linux/libtiff_native.so"
                 })
        {
            if (!File.Exists(candidate))
                continue;

            // Loaded by absolute path so it is in the process under its SONAME either way.
            var handle = NativeLibrary.Load(candidate);

            try
            {
                NativeLibrary.SetDllImportResolver(typeof(TiffNative).Assembly,
                    (name, _, _) => name.Contains("tiff_native") ? handle : IntPtr.Zero);
            }
            catch (InvalidOperationException)
            {
                // Only one resolver is allowed per assembly and another test class in this
                // assembly got there first. The preload above may still satisfy the import by
                // SONAME; if it does not, the test skips rather than failing on run order.
            }

            return true;
        }

        return false;
    });

    [Fact]
    public void ARefusedFileStillSaysWhy()
    {
        Assert.SkipUnless(Native.Value, "libtiff_native not loadable");
        
        var bytes = new byte[512];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        bytes[2] = 42;
        File.WriteAllBytes(_corrupt, bytes);

        InvalidOperationException thrown;

        try
        {
            thrown = Assert.Throws<InvalidOperationException>(() => new TiffDecoder().DecodeThumbnail(_corrupt, 64, TestContext.Current.CancellationToken));
        }
        catch (DllNotFoundException)
        {
            Assert.Skip("another test class owns this assembly's import resolver and the preload did not satisfy it");
            return;
        }
        
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
