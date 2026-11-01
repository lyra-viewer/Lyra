using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Content.Variants;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Loading;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// glTF models open as their textures: a flat list, opening on its contact sheet, or on the first
/// texture that decodes when it has none, the rest on demand, and a missing or broken texture
/// listed with its reason instead of failing the model.
/// </summary>
public sealed class ModelDecoderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lyra-model-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException) { }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Write(string name, string text) => Write(name, System.Text.Encoding.UTF8.GetBytes(text));

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Teal);

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string DataUri(byte[] png) => $"data:image/png;base64,{Convert.ToBase64String(png)}";

    private static Composite Open(string path)
    {
        var composite = new Composite(new FileInfo(path));
        new ModelDecoder().DecodeAsync(composite, Ct).GetAwaiter().GetResult();
        return composite;
    }

    /// <summary>Selects a texture and waits for it to arrive or fail.</summary>
    private static void SelectAndSettle(VariantContainer set, int index)
    {
        using var settled = new ManualResetEventSlim();
        void OnSettled(VariantContainer _) => settled.Set();

        set.VariantReady += OnSettled;
        set.VariantFailed += OnSettled;

        try
        {
            Assert.True(set.Select(index));
            Assert.True(settled.Wait(TimeSpan.FromSeconds(10), Ct), "the texture never settled");
        }
        finally
        {
            set.VariantReady -= OnSettled;
            set.VariantFailed -= OnSettled;
        }
    }

    [Fact]
    public void ModelsAreRegisteredAndKeptOutOfTheDuplicateFinder()
    {
        Assert.Equal(ImageFormatType.Gltf, ImageFormat.GetImageFormat(".glb"));
        Assert.Equal(ImageFormatType.Gltf, ImageFormat.GetImageFormat(".GLTF"));
        Assert.Equal(ImageFormatType.Fbx, ImageFormat.GetImageFormat(".fbx"));
        Assert.IsType<ModelDecoder>(DecoderManager.GetDecoder(ImageFormatType.Fbx));
        Assert.True(ImageFormat.IsExcludedFromDuplicateScan(".fbx"));
        Assert.IsType<ModelDecoder>(DecoderManager.GetDecoder(ImageFormatType.Gltf));

        Assert.True(ImageFormat.IsExcludedFromDuplicateScan(".glb"));
        Assert.False(ImageFormat.IsPerceptualHashSupported(".gltf"));
        Assert.False(ImageFormat.IsExcludedFromDuplicateScan(".png"));
        Assert.False(ImageFormat.IsExcludedFromDuplicateScan(".bin"));
    }

    [Fact]
    public void EachImageIsOneTextureAndTheLayoutPlacesItInEverySlotThatUsesIt()
    {
        Write("normal.png", Png(4, 4));
        var model = Write("crate.gltf", $$"""
            {
              "asset": { "version": "2.0", "generator": "Test Exporter" },
              "images": [
                { "name": "albedo", "uri": "{{DataUri(Png(8, 4))}}" },
                { "uri": "normal.png" },
                { "uri": "C:/art/rough.png" },
                { "name": "thumbnail", "uri": "{{DataUri(Png(2, 2))}}" }
              ],
              "textures": [ { "source": 0 }, { "source": 1 }, { "source": 2 }, { "source": 3 } ],
              "materials": [
                {
                  "name": "Crate",
                  "normalTexture": { "index": 1 },
                  "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 }, "metallicRoughnessTexture": { "index": 2 } }
                },
                { "name": "Decal", "emissiveTexture": { "index": 0 } }
              ]
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);

        Assert.Equal(VariantKind.Textures, set.Kind);
        Assert.Equal(["albedo", "normal.png", "rough.png", "thumbnail", "Crate", "All materials"], set.Variants.Select(variant => variant.Label));

        var layout = Assert.IsType<TextureLayout>(set.Textures);
        Assert.Equal(["Crate", "Decal"], layout.Materials.Select(material => material.Name));
        Assert.Equal([("Base Color", 0), ("Metallic Roughness", 2), ("Normal", 1)], layout.Materials[0].Slots.Select(slot => (slot.Label, slot.Texture)));
        Assert.Equal(0, Assert.Single(layout.Materials[1].Slots).Texture);
        Assert.Equal([3], layout.Unreferenced);

        Assert.Equal(TextureLocation.Embedded, layout.Sources[0].Location);
        Assert.Equal(TextureLocation.Linked, layout.Sources[1].Location);
        Assert.Equal(new TextureSource(TextureLocation.Missing, "C:/art/rough.png", null), layout.Sources[2]);

        Assert.Equal(layout.Overview, set.ActiveIndex);
        Assert.Equal((8, 4, "8 x 4 PNG"), (set.Variants[0].Width, set.Variants[0].Height, set.Variants[0].Detail));
        Assert.Equal(((float)set.Variants[5].Width, (float)set.Variants[5].Height), (composite.LogicalWidth, composite.LogicalHeight));

        Assert.Equal(LoadFailureKind.NotFound, set.FailureOf(2)?.Kind);
        Assert.Equal("missing", set.Variants[2].Detail);
        Assert.Null(set.LastFailure);

        Assert.Equal("4 x 4 PNG · linked", set.Variants[1].Detail);

        var facts = composite.FormatSpecificSnapshot().ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal("Test Exporter", facts["Generator"]);
        Assert.Equal("2", facts["Materials"]);
        Assert.Equal("1", facts["Missing"]);
        Assert.Equal("1", facts["Unreferenced"]);
    }

    [Fact]
    public void ALinkedTextureIsListedFromItsHeader_AndDecodedWhenPicked()
    {
        Write("normal.png", Png(4, 6));
        var model = Write("crate.gltf", $$"""
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "{{DataUri(Png(8, 4))}}" }, { "uri": "normal.png" } ],
              "textures": [ { "source": 0 }, { "source": 1 } ],
              "materials": [ { "normalTexture": { "index": 1 }, "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } } ]
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);

        Assert.Equal((4, 6, "4 x 6 PNG · linked"), (set.Variants[1].Width, set.Variants[1].Height, set.Variants[1].Detail));

        SelectAndSettle(set, 1);

        Assert.Equal(1, set.ShownIndex);
        Assert.Equal((4, 6, "4 x 6 PNG · linked"), (set.Variants[1].Width, set.Variants[1].Height, set.Variants[1].Detail));
        Assert.Equal((4f, 6f), (set.DecodedWidth, set.DecodedHeight));
        Assert.Equal("normal.png", set.Variants[1].Label);
        Assert.Equal("Material 0", set.Textures!.Materials[0].Name);
    }

    [Fact]
    public void ATextureNamedWithoutAnImageExtensionIsRecognisedByItsBytes()
    {
        Write("normal.data", Png(3, 3));
        var model = Write("odd.gltf", """
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "normal.data" } ],
              "textures": [ { "source": 0 } ],
              "materials": [ { "normalTexture": { "index": 0 } } ]
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);

        Assert.Equal("3 x 3 PNG · linked", set.Variants[0].Detail);
    }

    [Fact]
    public void AModelWithoutASheetOpensOnTheFirstTextureItsMaterialsListNotTheFirstInTheFile()
    {
        var model = Write("order.gltf", $$"""
            {
              "asset": { "version": "2.0" },
              "images": [ { "name": "thumb", "uri": "{{DataUri(Png(2, 2))}}" }, { "name": "albedo", "uri": "{{DataUri(Png(6, 3))}}" } ],
              "textures": [ { "source": 1 } ],
              "materials": [ { "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } } ]
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);

        Assert.Equal(1, set.ActiveIndex);
        Assert.Equal([0], set.Textures!.Unreferenced);
    }

    [Fact]
    public void WhenTheFirstTextureFails_AModelWithoutASheetOpensOnTheNext()
    {
        var model = Write("half.gltf", $$"""
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "data:image/png;base64,iVBORw0KGgo=" }, { "uri": "{{DataUri(Png(5, 5))}}" } ],
              "textures": [ { "source": 0 } ],
              "materials": [ { "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } } ]
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);

        Assert.Equal(1, set.ActiveIndex);
        Assert.Equal(LoadFailureKind.DecodeFailed, set.FailureOf(0)?.Kind);
        Assert.Equal(5, set.Variants[1].Width);
    }

    [Fact]
    public void AModelOpensOnItsSheet_KeepingWhatTheSheetLearnedOfEachTexture()
    {
        var model = Write("crate.gltf", $$"""
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "{{DataUri(Png(8, 4))}}" }, { "uri": "data:image/png;base64,iVBORw0KGgo=" }, { "uri": "{{DataUri(Png(6, 6))}}" } ],
              "textures": [ { "source": 0 }, { "source": 1 }, { "source": 2 } ],
              "materials": [
                {
                  "normalTexture": { "index": 1 },
                  "emissiveTexture": { "index": 2 },
                  "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } }
                }
              ]
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);

        Assert.Equal(set.Textures!.Materials[0].Sheet, set.ActiveIndex);
        var sheet = Assert.IsType<ContactSheetContent>(set.Active);
        Assert.Equal([true, false, true], sheet.Cells.Select(cell => cell.Content is not null));

        Assert.True(set.HasStandIn(0));
        Assert.True(set.HasStandIn(2));
        Assert.Equal(LoadFailureKind.DecodeFailed, set.FailureOf(1)?.Kind);
        Assert.Null(composite.Warning);

        SelectAndSettle(set, 2);
        Assert.Equal((6f, 6f), (set.DecodedWidth, set.DecodedHeight));
    }

    [Fact]
    public void AModelWhoseTexturesAllFail_OpensOnASheetOfTheReasons()
    {
        var model = Write("lost.gltf", """
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "gone.png" }, { "uri": "also-gone.png" } ],
              "textures": [ { "source": 0 }, { "source": 1 } ],
              "materials": [ { "normalTexture": { "index": 1 }, "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } } ]
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);

        Assert.Equal(set.Textures!.Materials[0].Sheet, set.ActiveIndex);
        var sheet = Assert.IsType<ContactSheetContent>(set.Active);
        Assert.Equal(["Base Color", "Normal"], sheet.Cells.Select(cell => cell.Caption));
        Assert.All(sheet.Cells, cell => Assert.Equal((null, "File not found"), (cell.Content, cell.Failure)));

        Assert.Equal(LoadFailureKind.NotFound, set.FailureOf(0)?.Kind);
        Assert.Empty(set.StepOrder);
        Assert.Equal("No texture could be shown", composite.Warning?.Message);
    }

    [Fact]
    public void AModelOfOneTextureThatFails_DoesNotOpen()
    {
        var model = Write("lost.gltf", """
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "gone.png" } ],
              "textures": [ { "source": 0 } ],
              "materials": [ { "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } } ]
            }
            """);

        var failure = Assert.Throws<LoadFailureException>(() => Open(model).Dispose());

        Assert.Contains("gone.png", failure.Message);
    }

    [Fact]
    public void AnUnnamedImageIsNamedForTheSlotItFills()
    {
        var model = Write("anon.gltf", $$"""
            {
              "asset": { "version": "2.0" },
              "images": [ { "uri": "{{DataUri(Png(2, 2))}}" }, { "uri": "{{DataUri(Png(2, 2))}}" }, { "uri": "{{DataUri(Png(2, 2))}}" } ],
              "textures": [ { "source": 0 }, { "source": 1 } ],
              "materials": [ { "normalTexture": { "index": 1 }, "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } } ]
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);

        Assert.Equal(["Base Color", "Normal", "Image 2"], set.Variants.Take(3).Select(variant => variant.Label));
        Assert.Equal("2 x 2 PNG", set.Variants[1].Detail);
    }

    [Fact]
    public void AnFbxOpensOnItsMaterialSheetAndListsTheMissingTexture()
    {
        var model = Write("jeep.fbx", $$"""
            ; FBX 7.4.0 project file
            Objects:  {
                Material: 1, "Material::Body", "" {
                }
                Video: 20, "Video::jeep1", "Clip" {
                    RelativeFilename: "jeep1.png"
                    Content: , "{{Convert.ToBase64String(Png(7, 5))}}"
                }
                Texture: 10, "Texture::Diffuse", "" {
                }
                Texture: 11, "Texture::Normal", "" {
                    FileName: "D:\art\jeep_n.png"
                }
            }
            Connections:  {
                C: "OP",11,1, "NormalMap"
                C: "OP",10,1, "DiffuseColor"
                C: "OO",20,10
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);
        var layout = set.Textures!;

        Assert.Equal(ImageFormatType.Fbx, composite.ImageFormatType);
        Assert.Equal(["Diffuse Color", "Normal Map"], layout.Materials[0].Slots.Select(slot => slot.Label));

        Assert.Equal(layout.Materials[0].Sheet, set.ActiveIndex);

        var diffuse = layout.Materials[0].Slots[0].Texture;
        Assert.Equal((7, 5, "7 x 5 PNG"), (set.Variants[diffuse].Width, set.Variants[diffuse].Height, set.Variants[diffuse].Detail));
        Assert.Equal("jeep1", set.Variants[diffuse].Label);

        var normal = layout.Materials[0].Slots[1].Texture;
        Assert.Equal(new TextureSource(TextureLocation.Missing, @"D:\art\jeep_n.png", null), layout.Sources[normal]);
        Assert.Equal(LoadFailureKind.NotFound, set.FailureOf(normal)?.Kind);
    }

    [Fact]
    public void MaterialsAndTheModelHaveContactSheetsOfReducedTextures()
    {
        var model = Write("sheets.gltf", $$"""
            {
              "asset": { "version": "2.0" },
              "images": [
                { "name": "albedo", "uri": "{{DataUri(Png(2048, 1024))}}" },
                { "name": "normal", "uri": "{{DataUri(Png(16, 16))}}" },
                { "uri": "gone.png" },
                { "name": "glass", "uri": "{{DataUri(Png(8, 8))}}" }
              ],
              "textures": [ { "source": 0 }, { "source": 1 }, { "source": 2 }, { "source": 3 } ],
              "materials": [
                {
                  "name": "Body",
                  "normalTexture": { "index": 1 },
                  "emissiveTexture": { "index": 0 },
                  "occlusionTexture": { "index": 2 },
                  "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } }
                },
                { "name": "Glass", "pbrMetallicRoughness": { "baseColorTexture": { "index": 3 } } }
              ]
            }
            """);

        using var composite = Open(model);
        var set = Assert.IsType<VariantContainer>(composite.Content);
        var layout = set.Textures!;

        Assert.Equal(4, layout.Materials[0].Sheet);
        Assert.Null(layout.Materials[1].Sheet);
        Assert.Equal(5, layout.Overview);
        Assert.Equal(5, set.ActiveIndex);
        var overview = Assert.IsType<ContactSheetContent>(set.Active);
        Assert.Equal(["Body", "Glass"], overview.Cells.Select(cell => cell.Caption));
        Assert.Equal(["Body", "All materials"], set.Variants.Skip(4).Select(variant => variant.Label));
        Assert.Null(layout.SourceOf(4));

        var expected = ContactSheetContent.SizeFor(3);
        Assert.Equal((expected.Width, expected.Height), (set.Variants[4].Width, set.Variants[4].Height));

        SelectAndSettle(set, 4);
        var sheet = Assert.IsType<ContactSheetContent>(set.Active);

        Assert.Equal(["Base Color, Emissive", "Normal", "Occlusion"], sheet.Cells.Select(cell => cell.Caption));
        Assert.Equal((1024f, 512f), (sheet.Cells[0].Content!.DecodedWidth!.Value, sheet.Cells[0].Content!.DecodedHeight!.Value));
        Assert.Equal(16f, sheet.Cells[1].Content!.DecodedWidth);
        Assert.Null(sheet.Cells[2].Content);
        Assert.Equal("File not found", sheet.Cells[2].Failure);
        Assert.Equal((float)expected.Width, composite.LogicalWidth);

        // Decoding the sheet described the textures it used.
        Assert.Equal("16 x 16 PNG", set.Variants[1].Detail);
    }

    [Fact]
    public void AModelWithoutTexturesDoesNotOpen()
    {
        var model = Write("plain.gltf", """{ "asset": { "version": "2.0" }, "materials": [ { "name": "Grey" } ] }""");

        var failure = Assert.Throws<LoadFailureException>(() => Open(model).Dispose());

        Assert.Equal((LoadFailureKind.NothingToShow, "The model holds no textures"), (failure.Kind, failure.Message));
    }

    [Fact]
    public void ANestedModelOrCameraRawIsNotATexture()
    {
        var nested = Write("inner.gltf", """{ "asset": { "version": "2.0" } }""");

        var failure = Assert.Throws<LoadFailureException>(() => Lyra.Imaging.Decoding.Support.StandaloneDecode.FromFile(nested, Ct).Dispose());

        Assert.Contains("cannot be shown as part of another file", failure.Message);
    }
}