using System.Buffers.Binary;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Skia reads .NET streams from native code, where an exception ends the process. Whatever the
/// stream does, Skia must see an ordinary short read.
/// </summary>
public class SkiaStreamTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ABmpWhosePixelsLieFarPastItsEnd_FailsInsteadOfEndingTheProcess()
    {
        var path = MinimalImageBuilder.WriteBmp();

        try
        {
            var bytes = File.ReadAllBytes(path);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 0xB6000036);
            File.WriteAllBytes(path, bytes);

            using var composite = new Composite(new FileInfo(path));
            var thrown = Assert.Throws<LoadFailureException>(() => new SkiaDecoder().DecodeAsync(composite, Ct).GetAwaiter().GetResult());

            Assert.True(LoadFailure.From(thrown).IsExpected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AStreamThatThrows_ReadsAsAFileWithNoImage()
    {
        using var stream = new Failing();
        using var codec = SkiaStream.Codec(stream, out _);

        Assert.Null(codec);
    }

    private sealed class Failing : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => throw new IOException("The network name is no longer available.");
        public override long Position { get => throw new IOException("gone"); set => throw new IOException("gone"); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("gone");
        public override long Seek(long offset, SeekOrigin origin) => throw new IOException("gone");
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}