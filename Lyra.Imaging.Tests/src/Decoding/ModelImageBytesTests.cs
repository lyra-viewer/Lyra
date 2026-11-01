using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Content.Variants;
using Lyra.Imaging.Decoding.Decoders.Model;
using Lyra.Imaging.Tests.Support;
using Lyra.ManagedCodecs.Scene;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>A model's texture bytes, fetched from wherever the model keeps them, checked against the files as they are now.</summary>
public class ModelImageBytesTests
{
    private const string ModelPath = "/nowhere/model.glb";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TextureSource Embedded = new(TextureLocation.Embedded, null, null);

    private static ModelTexture Texture(ImageSource image, TextureSource source) =>
        new(new SceneImage(0, "texture", null, image), source, new ImageVariant("texture", 0, 0, "", null));

    [Fact]
    public void AnEmbeddedImageIsReadFromItsRangeOfTheModel()
    {
        var payload = TempFile.Pattern(40)[..40];
        using var model = new TempFile([.. TempFile.Pattern(100), .. payload, .. TempFile.Pattern(7)]);

        var bytes = ModelImageBytes.Read(Texture(new EmbeddedSource(new ByteRange(100, payload.Length)), Embedded), model.Path, Ct);

        Assert.Equal(payload, bytes);
    }

    [Fact]
    public void MissingAndBrokenImagesFailWithTheirReason()
    {
        var missing = Texture(new ExternalSource(new FileReference("D:/textures/wood.png"), null), new TextureSource(TextureLocation.Missing, "D:/textures/wood.png", null));
        var notFound = Assert.Throws<LoadFailureException>(() => ModelImageBytes.Read(missing, ModelPath, Ct));
        Assert.Equal(LoadFailureKind.NotFound, notFound.Kind);
        Assert.Contains("D:/textures/wood.png", notFound.Message);

        var broken = Texture(new BrokenSource("Buffer 2 has no data."), new TextureSource(TextureLocation.Broken, null, null));
        Assert.Equal("Buffer 2 has no data.", Assert.Throws<LoadFailureException>(() => ModelImageBytes.Read(broken, ModelPath, Ct)).Message);
    }

    [Fact]
    public void AnImageThatIsAFileOfItsOwnHasNoBytesHere()
    {
        var linked = Texture(new ExternalSource(new FileReference("wood.png"), null), new TextureSource(TextureLocation.Linked, "wood.png", "/textures/wood.png"));

        Assert.Null(ModelImageBytes.Read(linked, ModelPath, Ct));
    }

    [Fact]
    public void ARangeBeyondTheFileAsItIsNowFails()
    {
        using var buffer = new TempFile(TempFile.Pattern(32));
        var source = new TextureSource(TextureLocation.Linked, "scene.bin", buffer.Path);

        var past = Texture(new ExternalSource(new FileReference("scene.bin"), new ByteRange(16, 32)), source);
        Assert.Contains("past the end", Assert.Throws<LoadFailureException>(() => ModelImageBytes.Read(past, ModelPath, Ct)).Message);

        var inside = Texture(new ExternalSource(new FileReference("scene.bin"), new ByteRange(8, 16)), source);
        Assert.Equal(TempFile.Pattern(32)[8..24], ModelImageBytes.Read(inside, ModelPath, Ct));
    }
}