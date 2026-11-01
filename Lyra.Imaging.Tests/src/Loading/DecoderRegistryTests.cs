using Lyra.Common;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Decoding;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Detection;
using Lyra.Imaging.Loading;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

public class DecoderRegistryTests
{
    public static TheoryData<ImageFormatType> NamedFormats => new(Enum.GetValues<ImageFormatType>().Where(format => format != ImageFormatType.Unknown));

    [Theory]
    [MemberData(nameof(NamedFormats))]
    public void EveryNamedFormat_HasADecoder(ImageFormatType format)
    {
        var decoder = DecoderManager.Find(format);

        Assert.NotNull(decoder);
        Assert.Contains(format, decoder.Formats);
    }

    [Fact]
    public void AFormatClaimedTwice_IsRefusedWhenTheRegistryIsBuilt()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => DecoderManager.Index([new SkiaDecoder(), new SkiaDecoder()]));

        Assert.Contains("claimed by both", thrown.Message);
    }

    [Fact]
    public void AnUnrecognisedFormat_FailsAsTheFiles_NotAsAnotherDecoderGuessing()
    {
        Assert.Null(DecoderManager.Find(ImageFormatType.Unknown));

        var thrown = Assert.Throws<LoadFailureException>(() => DecoderManager.GetDecoder(ImageFormatType.Unknown));

        Assert.Equal("The image's format is not recognised.", thrown.Message);
        Assert.True(LoadFailure.From(thrown).IsExpected);
    }

    [Fact]
    public void TheFormatsReadFromMemory_AreTheOnesWhoseDecodersSayTheyCan()
    {
        ImageFormatType[] expected =
        [
            ImageFormatType.Bmp, ImageFormatType.Jpeg, ImageFormatType.Jfif, ImageFormatType.Png, ImageFormatType.Gif,
            ImageFormatType.Webp, ImageFormatType.Tga, ImageFormatType.Qoi, ImageFormatType.Dds, ImageFormatType.Ktx,
            ImageFormatType.Hdr
        ];

        var declared = DecoderManager.Formats.Where(StandaloneDecode.CanDecodeFromMemory);

        Assert.Equal(expected.Order(), declared.Order());
    }

    [Fact]
    public void OnlyRawAndModels_OpenOnlyDirectly()
    {
        var declared = DecoderManager.Formats.Where(format => DecoderManager.Can(format, DecoderCapabilities.OpensOnlyDirectly));

        Assert.Equal(new[] { ImageFormatType.Raw, ImageFormatType.Fbx, ImageFormatType.Gltf }.Order(), declared.Order());
    }

    [Fact]
    public void ANameThatGivesNoFormat_IsOutweighedByAWeakSignature()
    {
        Assert.Equal(ImageFormatType.Bmp, FormatDetector.Resolve(ImageFormatType.Unknown, "BM\0\0\0\0\0\0\0\0"u8));
    }

    [Fact]
    public void AWeakSignature_StillDoesNotOverruleAName()
    {
        Assert.Equal(ImageFormatType.Png, FormatDetector.Resolve(ImageFormatType.Png, "BM\0\0\0\0\0\0\0\0"u8));
    }
}