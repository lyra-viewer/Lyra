using Lyra.Imaging.Content;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

public class LoadFailureTests : IDisposable
{
    private readonly TempFile _file = new([0]);
    private readonly List<Composite> _composites = [];

    public void Dispose()
    {
        foreach (var composite in _composites)
            composite.Dispose();

        _file.Dispose();
    }

    public static TheoryData<Exception, LoadFailureKind> Classified => new()
    {
        { new UnauthorizedAccessException("denied"), LoadFailureKind.AccessDenied },
        { new FileNotFoundException("gone"), LoadFailureKind.NotFound },
        { new DirectoryNotFoundException("gone"), LoadFailureKind.NotFound },
        { new IOException("in use", unchecked((int)0x80070020)), LoadFailureKind.InUse },
        { new IOException("locked", unchecked((int)0x80070021)), LoadFailureKind.InUse },
        { new IOException("network name no longer available", unchecked((int)0x80070040)), LoadFailureKind.SourceUnavailable },
        { new EndOfStreamException("truncated"), LoadFailureKind.DecodeFailed },
        { new InvalidDataException("bad header"), LoadFailureKind.DecodeFailed },
        { new InvalidOperationException("decoder refused"), LoadFailureKind.DecodeFailed },
        { new OutOfMemoryException(), LoadFailureKind.OutOfMemory },
        { new OverflowException(), LoadFailureKind.TooLarge },
        { new LoadFailureException(LoadFailureKind.TooLarge, "over budget"), LoadFailureKind.TooLarge },
        { new NullReferenceException(), LoadFailureKind.Unknown }
    };

    [Theory]
    [MemberData(nameof(Classified))]
    public void ExceptionsAreClassified(Exception ex, LoadFailureKind expected)
    {
        var failure = LoadFailure.From(ex);

        Assert.Equal(expected, failure.Kind);
        Assert.Equal(ex.Message, failure.Detail);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
    }

    [Theory]
    [InlineData("Unsupported VkFormat 67305985.", "Unsupported VkFormat 67305985")]
    [InlineData("Implausible width 0.", "Implausible width 0")]
    [InlineData("Failed to decode: Z:/a.jp2", "Failed to decode: Z:/a.jp2")]
    [InlineData("Stream error while reading JP2 Header box: box length is inconsistent.", "Stream error while reading JP2 Header box: box length is inconsistent")]
    [InlineData("A marker ID was expected (0xff--) instead of 0000aa14", "A marker ID was expected (0xff--) instead of 0000aa14")]
    [InlineData("Dimensions exceed limit (1048575): 203x2097304.", "Dimensions exceed limit (1048575): 203x2097304")]
    [InlineData("Failed to decode Z:/a.tif at native depth. The read did not return in time.", "Failed to decode Z:/a.tif at native depth. The read did not return in time")]
    public void TheDescriptionIsTheDetailWithoutItsClosingPeriod(string thrown, string shown)
    {
        var failure = LoadFailure.From(new InvalidOperationException(thrown));

        Assert.Equal(shown, failure.Description);
        Assert.Equal(thrown, failure.Detail);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "Access denied: Z:/a.png")]
    [InlineData(typeof(FileNotFoundException), "File not found: Z:/a.png")]
    public void AFailureAboutTheFile_NamesTheFile(Type exceptionType, string shown)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "Access to the path 'Z:/a.png' is denied.")!;

        Assert.Equal(shown, LoadFailure.From(ex, "Z:/a.png").Description);
    }

    [Fact]
    public void WithoutAPath_TheDescriptionIsTheCleanedDetail()
    {
        var failure = LoadFailure.From(new UnauthorizedAccessException("Access to the path 'Z:/a.png' is denied."));

        Assert.Equal("Access to the path 'Z:/a.png' is denied", failure.Description);
    }

    [Fact]
    public void ANativeDecodeWithoutAReason_NamesTheFile()
    {
        Assert.Equal("Failed to decode: Z:/a.jp2", Lyra.Imaging.Interop.NativeErrors.DecodeFailed(IntPtr.Zero, "Z:/a.jp2").Message);
    }

    [Fact]
    public void OnlyAProgramFault_IsUnexpected()
    {
        Assert.False(LoadFailure.From(new NullReferenceException()).IsExpected);
        Assert.True(LoadFailure.From(new UnauthorizedAccessException()).IsExpected);
    }

    [Theory]
    [InlineData(LoadFailureKind.AccessDenied, true)]
    [InlineData(LoadFailureKind.NotFound, true)]
    [InlineData(LoadFailureKind.InUse, true)]
    [InlineData(LoadFailureKind.SourceUnavailable, true)]
    [InlineData(LoadFailureKind.OutOfMemory, true)]
    [InlineData(LoadFailureKind.TooLarge, false)]
    [InlineData(LoadFailureKind.DecodeFailed, false)]
    [InlineData(LoadFailureKind.Unknown, false)]
    public void AFailedImage_IsRetriedOnReturn_OnlyWhenTheCauseMayHavePassed(LoadFailureKind kind, bool retried)
    {
        var composite = Image();
        composite.Fail(new LoadFailure(kind, "detail"));

        Assert.Equal(retried, ImageLoader.IsWorthRetrying(composite));
    }

    [Fact]
    public void AnImageThatDidNotFail_IsNotRetried()
    {
        var composite = Image();
        composite.State = CompositeState.Complete;

        Assert.False(ImageLoader.IsWorthRetrying(composite));
    }

    private Composite Image()
    {
        var composite = new Composite(new FileInfo(_file.Path));
        _composites.Add(composite);
        return composite;
    }
}
