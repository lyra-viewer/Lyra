using Lyra.ManagedCodecs.Scene;
using Lyra.ManagedCodecs.Scene.Gltf;
using Xunit;
using static Lyra.ManagedCodecs.Tests.Scene.GltfFixture;

namespace Lyra.ManagedCodecs.Tests.Scene;

/// <summary>glTF and GLB: materials to slots to images, wherever the images live, and damage kept to the image it affects.</summary>
public sealed class GltfReaderTests : IDisposable
{
    private readonly GltfFixture _files = new();

    public void Dispose() => _files.Dispose();

    private static SceneTextures Read(string path)
    {
        using var stream = File.OpenRead(path);
        return GltfReader.Read(stream, TestContext.Current.CancellationToken);
    }

    private static T Source<T>(SceneImage image) where T : ImageSource => Assert.IsType<T>(image.Source);

    private const string RustMetal = """
        {
          "asset": { "version": "2.0", "generator": "Test Exporter" },
          "buffers": [ { "byteLength": 48 } ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 16 },
            { "buffer": 0, "byteOffset": 16, "byteLength": 16 },
            { "buffer": 0, "byteOffset": 32, "byteLength": 16 }
          ],
          "images": [
            { "name": "albedo", "bufferView": 0, "mimeType": "image/png" },
            { "name": "orm", "bufferView": 1, "mimeType": "image/png" },
            { "name": "normal", "bufferView": 2, "mimeType": "image/jpeg" }
          ],
          "textures": [ { "source": 0 }, { "source": 1 }, { "source": 2 } ],
          "materials": [
            {
              "name": "Rust_Metal",
              "emissiveTexture": { "index": 0 },
              "occlusionTexture": { "index": 1 },
              "normalTexture": { "index": 2, "scale": 1.0 },
              "pbrMetallicRoughness": {
                "baseColorTexture": { "index": 0 },
                "metallicRoughnessTexture": { "index": 1 }
              }
            }
          ]
        }
        """;

    [Fact]
    public void AGlbsEmbeddedImagesAreSlicesOfItsBinaryChunk()
    {
        byte[] binary = [.. Payload(10), .. Payload(50), .. Payload(90)];
        var path = _files.Write("rust.glb", Glb(RustMetal, binary));

        var scene = Read(path);

        Assert.Equal(SceneFormat.Glb, scene.Format);
        Assert.Equal("Test Exporter", scene.Generator);
        Assert.Empty(scene.Issues);

        var file = File.ReadAllBytes(path);
        var start = BinaryOffset(RustMetal);

        for (var i = 0; i < 3; i++)
        {
            var range = Source<EmbeddedSource>(scene.Images[i]).Range;
            Assert.Equal(new ByteRange(start + i * 16, 16), range);
            Assert.Equal(binary.AsSpan(i * 16, 16), file.AsSpan((int)range.Offset, (int)range.Length));
        }

        Assert.Equal("image/jpeg", scene.Images[2].MimeType);
    }

    [Fact]
    public void CoreSlotsComeInReadingOrderAndASharedImageIsOneInstance()
    {
        var path = _files.Write("rust.glb", Glb(RustMetal, new byte[48]));

        var material = Assert.Single(Read(path).Materials);

        Assert.Equal("Rust_Metal", material.Name);
        Assert.Equal(["Base Color", "Metallic Roughness", "Normal", "Occlusion", "Emissive"], material.Slots.Select(slot => slot.Label));
        Assert.Equal("pbrMetallicRoughness.baseColorTexture", material.Slots[0].Key);

        Assert.Same(material.Slots[0].Image, material.Slots[4].Image);
        Assert.Same(material.Slots[1].Image, material.Slots[3].Image);
    }

    [Fact]
    public void ExtensionSlotsAreFoundWithoutKnowingTheExtension()
    {
        var json = """
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "a.png" }, { "uri": "b.png" }, { "uri": "c.png" } ],
              "textures": [ { "source": 0 }, { "source": 1 }, { "source": 2 } ],
              "materials": [ {
                "extensions": {
                  "KHR_materials_clearcoat": { "clearcoatFactor": 1, "clearcoatNormalTexture": { "index": 0 } },
                  "VENDOR_future_thing": { "nested": { "dustMaskTexture": { "index": 1 } } }
                },
                "extras": { "lightmap_bakedTexture": { "index": 2 } }
              } ]
            }
            """;

        var material = Assert.Single(Read(_files.Write("x.gltf", json)).Materials);

        Assert.Equal(["Clearcoat Normal", "Dust Mask", "Lightmap baked"], material.Slots.Select(slot => slot.Label));
        Assert.Equal("extensions.KHR_materials_clearcoat.clearcoatNormalTexture", material.Slots[0].Key);
        Assert.Equal("extensions.VENDOR_future_thing.nested.dustMaskTexture", material.Slots[1].Key);
    }

    [Theory]
    [InlineData("baseColorTexture", "Base Color")]
    [InlineData("specularGlossinessTexture", "Specular Glossiness")]
    [InlineData("diffuseTransmissionColorTexture", "Diffuse Transmission Color")]
    [InlineData("iridescenceThicknessTexture", "Iridescence Thickness")]
    [InlineData("Texture", "Texture")]
    public void SlotLabelsAreThePropertyNameSpelledOut(string property, string label) => Assert.Equal(label, GltfReader.SlotLabel(property));

    [Fact]
    public void AnExtensionImageIsShownAndThePlainOneKeptAsAnAlternate()
    {
        var json = """
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "wood.png" }, { "uri": "wood.ktx2" }, { "uri": "stray.png" } ],
              "textures": [ { "source": 0, "extensions": { "KHR_texture_basisu": { "source": 1 }, "KHR_texture_transform": { "offset": [0, 0] } } } ],
              "materials": [ { "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } } ]
            }
            """;

        var scene = Read(_files.Write("wood.gltf", json));
        var slot = Assert.Single(scene.Materials[0].Slots);

        Assert.Same(scene.Images[1], slot.Image);
        Assert.Same(scene.Images[0], Assert.Single(slot.Alternates));
        Assert.Same(scene.Images[2], Assert.Single(scene.Unreferenced));
    }

    [Fact]
    public void ExternalImagesAreNamedWithPercentEscapesDecodedAndNotLookedFor()
    {
        var json = """
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "maps/old%20wood.png" }, { "uri": "maps/gone.png" } ]
            }
            """;

        var scene = Read(_files.Write("model.gltf", json));

        Assert.Equal(SceneFormat.Gltf, scene.Format);

        var named = Source<ExternalSource>(scene.Images[0]);
        Assert.Equal("maps/old wood.png", named.File.StoredPath);
        Assert.Equal(["maps/old wood.png"], named.File.Candidates);
        Assert.Null(named.Range);

        Assert.Equal("maps/gone.png", Source<ExternalSource>(scene.Images[1]).File.StoredPath);
    }

    [Fact]
    public void PathsFromOtherMachinesAreKeptAsWritten()
    {
        var json = """
            {
              "asset": { "version": "2.0" },
              "images": [
                { "uri": "D:\\projects\\jeep\\body.png" },
                { "uri": "/Users/somebody/glass.png" },
                { "uri": "https://example.com/remote.png" }
              ]
            }
            """;

        var scene = Read(_files.Write("jeep.gltf", json));

        Assert.Equal(
            [@"D:\projects\jeep\body.png", "/Users/somebody/glass.png", "https://example.com/remote.png"],
            scene.Images.Select(image => Source<ExternalSource>(image).File.StoredPath));
    }

    [Fact]
    public void DataUriImagesAndBuffersAreDecoded()
    {
        var image = Payload(7);
        byte[] buffer = [.. Payload(0, 8), .. Payload(40)];
        var json = $$"""
            {
              "asset": { "version": "2.0" },
              "buffers": [ { "byteLength": {{buffer.Length}}, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(buffer)}}" } ],
              "bufferViews": [ { "buffer": 0, "byteOffset": 8, "byteLength": 16 } ],
              "images": [
                { "uri": "data:image/png;base64,{{Convert.ToBase64String(image)}}" },
                { "bufferView": 0, "mimeType": "image/webp" },
                { "uri": "data:image/png;base64,not base64!" },
                { "uri": "data:image/png,%89PNG" }
              ]
            }
            """;

        var scene = Read(_files.Write("inline.gltf", json));

        Assert.Equal(image, Source<InlineSource>(scene.Images[0]).Bytes.ToArray());
        Assert.Equal("image/png", scene.Images[0].MimeType);
        Assert.Equal(Payload(40), Source<InlineSource>(scene.Images[1]).Bytes.ToArray());
        Assert.Equal("image/webp", scene.Images[1].MimeType);
        Source<BrokenSource>(scene.Images[2]);
        Source<BrokenSource>(scene.Images[3]);
    }

    [Fact]
    public void ImagesInAnExternalBufferPointIntoThatFile()
    {
        var json = """
            {
              "asset": { "version": "2.0" },
              "buffers": [ { "byteLength": 64, "uri": "scene.bin" }, { "byteLength": 64, "uri": "lost.bin" } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 32, "byteLength": 32 },
                { "buffer": 0, "byteOffset": 48, "byteLength": 32 },
                { "buffer": 1, "byteLength": 8 }
              ],
              "images": [ { "bufferView": 0 }, { "bufferView": 1 }, { "bufferView": 2 } ]
            }
            """;

        var scene = Read(_files.Write("scene.gltf", json));

        var inFile = Source<ExternalSource>(scene.Images[0]);
        Assert.Equal("scene.bin", inFile.File.StoredPath);
        Assert.Equal(new ByteRange(32, 32), inFile.Range);
        Assert.Equal(new ByteRange(48, 32), Source<ExternalSource>(scene.Images[1]).Range);

        var other = Source<ExternalSource>(scene.Images[2]);
        Assert.Equal("lost.bin", other.File.StoredPath);
        Assert.Equal(new ByteRange(0, 8), other.Range);
    }

    [Fact]
    public void BadIndicesBreakOnlyWhatTheyTouch()
    {
        var json = """
            {
              "asset": { "version": "2.0" },
              "buffers": [ { "byteLength": 16 } ],
              "bufferViews": [ { "buffer": 0, "byteOffset": 8, "byteLength": 16 }, { "buffer": 0, "byteOffset": -1, "byteLength": 4 } ],
              "images": [ { "bufferView": 0 }, { "bufferView": 1 }, { "bufferView": 9 }, { "name": "empty" }, { "uri": "fine.png" } ],
              "textures": [ { "source": 4 }, { "source": 99 }, { "sampler": 0 } ],
              "materials": [
                {
                  "name": "Partly broken",
                  "normalTexture": { "index": 1 },
                  "occlusionTexture": { "index": 2 },
                  "emissiveTexture": { "index": 42 },
                  "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 }, "metallicRoughnessTexture": { "index": "zero" } }
                },
                "not an object",
                {}
              ]
            }
            """;

        var scene = Read(_files.Write("broken.glb", Glb(json, new byte[16])));

        Assert.Contains("binary chunk", Source<BrokenSource>(scene.Images[0]).Reason);
        Assert.Contains("malformed", Source<BrokenSource>(scene.Images[1]).Reason);
        Assert.Contains("does not exist", Source<BrokenSource>(scene.Images[2]).Reason);
        Assert.Contains("neither", Source<BrokenSource>(scene.Images[3]).Reason);

        var material = scene.Materials[0];
        Assert.Equal(["Base Color"], material.Slots.Select(slot => slot.Label));
        Assert.Same(scene.Images[4], material.Slots[0].Image);

        Assert.Empty(scene.Materials[1].Slots);
        Assert.Null(scene.Materials[2].Name);

        Assert.Equal(5, scene.Issues.Count);
        Assert.Contains(scene.Issues, issue => issue.Contains("image 99"));
        Assert.Contains(scene.Issues, issue => issue.Contains("texture 42"));
        Assert.Contains(scene.Issues, issue => issue.Contains("malformed texture index"));
        Assert.Contains(scene.Issues, issue => issue.Contains("Texture 2 has no image"));
        Assert.Contains(scene.Issues, issue => issue.Contains("Texture 1 has no image"));
    }

    [Fact]
    public void ATruncatedGlbKeepsWhatItHas()
    {
        byte[] binary = [.. Payload(10), .. Payload(50), .. Payload(90)];
        var whole = Glb(RustMetal, binary);
        var path = _files.Write("cut.glb", whole[..^20]);

        var scene = Read(path);

        Assert.Contains(scene.Issues, issue => issue.Contains("truncated"));
        Source<EmbeddedSource>(scene.Images[0]);
        Source<BrokenSource>(scene.Images[2]);
    }

    [Fact]
    public void AGlbWithoutABinaryChunkHasNoDataForBufferZero()
    {
        var scene = Read(_files.Write("nobin.glb", Glb(RustMetal)));

        Assert.All(scene.Images, image => Assert.Contains("no data", Source<BrokenSource>(image).Reason));
    }

    [Fact]
    public void AGltfWithAByteOrderMarkStillReads()
    {
        var path = _files.Write("bom.gltf", [0xEF, 0xBB, 0xBF, .. """{ "asset": { "version": "2.0" } }"""u8]);

        Assert.Empty(Read(path).Images);
    }

    [Fact]
    public void FilesThatAreNotGltf20AreRefused()
    {
        Assert.Throws<InvalidDataException>(() => Read(_files.Write("bad.gltf", "{ not json")));
        Assert.Throws<InvalidDataException>(() => Read(_files.Write("array.gltf", "[]")));
        Assert.Throws<NotSupportedException>(() => Read(_files.Write("old.gltf", """{ "asset": { "version": "1.0" } }""")));
        Assert.Throws<NotSupportedException>(() => Read(_files.Write("old.glb", Glb("{}", version: 1))));
        Assert.Throws<InvalidDataException>(() => Read(_files.Write("short.glb", "glTF"u8.ToArray())));
    }

    [Fact]
    public void EveryTruncationOfAGlbEitherReadsOrIsRefusedCleanly()
    {
        var whole = Glb(RustMetal, new byte[48]);

        for (var length = 0; length < whole.Length; length++)
        {
            var path = _files.Write($"cut{length}.glb", whole[..length]);

            try
            {
                Read(path);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { }
        }
    }
}