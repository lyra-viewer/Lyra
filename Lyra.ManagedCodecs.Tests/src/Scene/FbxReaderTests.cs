using Lyra.ManagedCodecs.Scene;
using Lyra.ManagedCodecs.Scene.Fbx;
using Xunit;
using static Lyra.ManagedCodecs.Tests.Scene.FbxFixture;
using static Lyra.ManagedCodecs.Tests.Scene.GltfFixture;

namespace Lyra.ManagedCodecs.Tests.Scene;

/// <summary>
/// FBX, binary and ASCII: the texture graph assembled from connections, embedded media located
/// rather than copied, paths tried relative, absolute, then by name, and meshes never touched.
/// </summary>
public sealed class FbxReaderTests : IDisposable
{
    private readonly GltfFixture _files = new();

    public void Dispose() => _files.Dispose();

    private static SceneTextures Read(string path)
    {
        using var stream = File.OpenRead(path);
        return FbxReader.Read(stream, TestContext.Current.CancellationToken);
    }

    private static T Source<T>(SceneImage image) where T : ImageSource => Assert.IsType<T>(image.Source);

    private static readonly byte[] Jeep = Payload(0x40, 32);
    private static readonly byte[] Thumb = Payload(0x90, 24);

    /// <summary>
    /// Body: an embedded diffuse, a normal map at its relative path, a shininess map found by name in
    /// the .fbm folder. Glass: a two-layer diffuse, a 3ds Max transparency map that is missing, and
    /// the body's normal map again. An empty material, a mesh, and a video no texture uses.
    /// </summary>
    private static FbxNode[] Jeep7()
    {
        var garbage = Payload(0x01, 64); // a "deflated" vertex array that would not inflate

        return
        [
            new FbxNode("Creator", "Lyra Test Exporter"),
            new FbxNode("Objects").With(
                new FbxNode("Geometry", 100L, Named("Body", "Geometry"), "Mesh").With(
                    new FbxNode("Vertices", new FbxArray('d', 1_000_000, 1, garbage)),
                    new FbxNode("PolygonVertexIndex", new FbxArray('i', 1_000_000, 1, garbage))),
                new FbxNode("Material", 1L, Named("Body_Mat", "Material"), ""),
                new FbxNode("Material", 2L, Named("Glass_Mat", "Material"), ""),
                new FbxNode("Material", 3L, Named("Empty", "Material"), ""),
                new FbxNode("Texture", 10L, Named("Diffuse", "Texture"), "").With(
                    new FbxNode("FileName", @"D:\projects\jeep\jeep1.jpg"),
                    new FbxNode("RelativeFilename", "jeep1.jpg")),
                new FbxNode("Video", 20L, Named("jeep1", "Video"), "Clip").With(
                    new FbxNode("Type", "Clip"),
                    new FbxNode("FileName", @"D:\projects\jeep\jeep1.jpg"),
                    new FbxNode("RelativeFilename", "jeep1.jpg"),
                    new FbxNode("Content", Jeep)),
                new FbxNode("Texture", 11L, Named("Normal", "Texture"), "").With(
                    new FbxNode("FileName", @"D:\projects\jeep\maps\jeep_n.png"),
                    new FbxNode("RelativeFilename", @"maps\jeep_n.png")),
                new FbxNode("Texture", 12L, Named("Rough", "Texture"), "").With(
                    new FbxNode("FileName", @"C:\Users\artist\rough.tga"),
                    new FbxNode("RelativeFilename", @"..\..\rough.tga")),
                new FbxNode("Texture", 13L, Named("GlassAlpha", "Texture"), "").With(
                    new FbxNode("FileName", "glass_a.png")),
                new FbxNode("LayeredTexture", 30L, Named("Layers", "LayeredTexture"), ""),
                new FbxNode("Texture", 14L, Named("Dirt", "Texture"), "").With(new FbxNode("RelativeFilename", "dirt.png")),
                new FbxNode("Texture", 15L, Named("Ao", "Texture"), "").With(new FbxNode("FileName", @"Z:\shared\ao.png")),
                new FbxNode("Texture", 17L, Named("GlassNormal", "Texture"), "").With(
                    new FbxNode("FileName", @"D:\projects\jeep\maps\jeep_n.png")),
                new FbxNode("Video", 21L, Named("thumb", "Video"), "Clip").With(new FbxNode("Content", Thumb))),
            new FbxNode("Connections").With(
                new FbxNode("C", "OO", 100L, 999L),
                new FbxNode("C", "OO", 1L, 999L),
                new FbxNode("C", "OP", 10L, 1L, "DiffuseColor"),
                new FbxNode("C", "OO", 20L, 10L),
                new FbxNode("C", "OP", 11L, 1L, "NormalMap"),
                new FbxNode("C", "OP", 12L, 1L, "ShininessExponent"),
                new FbxNode("C", "OP", 13L, 2L, "3dsMax|main|transparency_map"),
                new FbxNode("C", "OP", 30L, 2L, "DiffuseColor"),
                new FbxNode("C", "OO", 14L, 30L),
                new FbxNode("C", "OO", 15L, 30L),
                new FbxNode("C", "OP", 17L, 2L, "NormalMap"))
        ];
    }

    [Theory]
    [InlineData(7400u)]
    [InlineData(7500u)]
    public void ABinaryFbxGivesItsTextureGraph(uint version)
    {
        var file = Binary(version, Jeep7());
        var path = _files.Write("jeep.fbx", file);

        var scene = Read(path);

        Assert.Equal(SceneFormat.Fbx, scene.Format);
        Assert.Equal("Lyra Test Exporter", scene.Generator);
        Assert.Empty(scene.Issues);
        Assert.Equal(["Body_Mat", "Glass_Mat", "Empty"], scene.Materials.Select(material => material.Name));

        var body = scene.Materials[0].Slots;
        Assert.Equal(["Diffuse Color", "Normal Map", "Shininess Exponent"], body.Select(slot => slot.Label));

        var diffuse = Source<EmbeddedSource>(body[0].Image);
        Assert.Equal(new ByteRange(OffsetOf(file, Jeep), Jeep.Length), diffuse.Range);
        Assert.Equal("jeep1.jpg", body[0].Image.Name);

        // The relative path is tried first; the absolute one is what the author's tool wrote.
        var normal = Source<ExternalSource>(body[1].Image).File;
        Assert.Equal(@"D:\projects\jeep\maps\jeep_n.png", normal.StoredPath);
        Assert.Equal([@"maps\jeep_n.png", @"D:\projects\jeep\maps\jeep_n.png"], normal.Candidates);

        Assert.Equal([@"..\..\rough.tga", @"C:\Users\artist\rough.tga"], Source<ExternalSource>(body[2].Image).File.Candidates);

        var glass = scene.Materials[1].Slots;
        Assert.Equal(["Diffuse Color (layer 1)", "Diffuse Color (layer 2)", "Transparency", "Normal Map"], glass.Select(slot => slot.Label));
        Assert.Equal("dirt.png", Source<ExternalSource>(glass[0].Image).File.StoredPath);
        Assert.Equal(@"Z:\shared\ao.png", Source<ExternalSource>(glass[1].Image).File.StoredPath);
        Assert.Equal("glass_a.png", Source<ExternalSource>(glass[2].Image).File.StoredPath);
        Assert.Same(body[1].Image, glass[3].Image);

        Assert.Empty(scene.Materials[2].Slots);

        var thumb = Assert.Single(scene.Unreferenced);
        Assert.Equal(new ByteRange(OffsetOf(file, Thumb), Thumb.Length), Source<EmbeddedSource>(thumb).Range);
    }

    [Fact]
    public void EveryTruncationOfABinaryFbxEitherReadsOrIsRefusedCleanly()
    {
        var whole = Binary(7500, Jeep7());

        for (var length = 0; length < whole.Length; length += 3)
        {
            var path = _files.Write($"cut{length}.fbx", whole[..length]);

            try
            {
                Read(path);
            }
            catch (InvalidDataException) { }
        }
    }

    private const string Ascii7 = """
        ; FBX 7.4.0 project file
        ; ----------------------------------------------------
        FBXHeaderExtension:  {
            FBXHeaderVersion: 1003
            Creator: "ASCII Exporter"
            SceneInfo: "SceneInfo::GlobalInfo", "UserData" {
                Properties70:  {
                    P: "Original|ApplicationName", "KString", "", "", "Tool {with braces}"
                }
            }
        }
        Objects:  {
            Geometry: 100, "Geometry::Body", "Mesh" {
                Vertices: *6 {
                    a: 0,0,0,1,1,1
                }
                PolygonVertexIndex: *3 {
                    a: 0,1,-3
                }
            }
            Material: 1, "Material::Body_Mat", "" {
                Version: 102
                ShadingModel: "phong"
                Properties70:  {
                    P: "DiffuseColor", "Color", "", "A",0.8,0.8,0.8
                }
            }
            Video: 20, "Video::jeep1", "Clip" {
                Type: "Clip"
                FileName: "D:\projects\jeep\jeep1.png"
                RelativeFilename: "jeep1.png"
                Content: , "QUJD",
                "REVG"
            }
            Texture: 10, "Texture::Diffuse", "" {
                FileName: "D:\projects\jeep\jeep1.png"
                RelativeFilename: "jeep1.png"
            }
            Texture: 11, "Texture::Bump", "" {
                RelativeFilename: "maps/bump.png"
            }
        }
        Connections:  {
            ;Texture::Diffuse, Material::Body_Mat
            C: "OP",10,1, "DiffuseColor"
            C: "OO",20,10
            C: "OP",11,1, "Maya|TEX_bump_map"
        }
        """;

    [Fact]
    public void AnAsciiFbxGivesTheSameGraphWithItsMediaDecoded()
    {
        var scene = Read(_files.Write("ascii.fbx", Ascii7));

        Assert.Equal("ASCII Exporter", scene.Generator);
        Assert.Empty(scene.Issues);

        var slots = Assert.Single(scene.Materials).Slots;
        Assert.Equal(["Diffuse Color", "Bump"], slots.Select(slot => slot.Label));
        Assert.Equal("ABCDEF"u8.ToArray(), Source<InlineSource>(slots[0].Image).Bytes.ToArray());
        Assert.Equal(["maps/bump.png"], Source<ExternalSource>(slots[1].Image).File.Candidates);
    }

    [Fact]
    public void AnAsciiFbxStartingWithAByteOrderMarkIsRecognised()
    {
        var scene = Read(_files.Write("bom.fbx", [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(Ascii7)]));

        Assert.Equal("ASCII Exporter", scene.Generator);
        Assert.Equal(2, Assert.Single(scene.Materials).Slots.Count);
    }

    [Fact]
    public void AnFbx6FileConnectsObjectsByName()
    {
        var scene = Read(_files.Write("old.fbx", """
            ; FBX 6.1.0 project file
            FBXHeaderExtension:  {
                FBXHeaderVersion: 1003
            }
            Creator: "FBX SDK/FBX Plugins version 2006"
            Objects:  {
                Model: "Model::Crate", "Mesh" {
                    Vertices: 0,0,0,1,1,1
                }
                Material: "Material::Wood", "" {
                    Version: 102
                }
                Texture: "Texture::WoodMap", "TextureVideoClip" {
                    FileName: "C:\old\wood.png"
                    RelativeFilename: "wood.png"
                }
            }
            Connections:  {
                Connect: "OO", "Material::Wood", "Model::Crate"
                Connect: "OP", "Texture::WoodMap", "Material::Wood", "DiffuseColor"
            }
            """));

        Assert.Equal("FBX SDK/FBX Plugins version 2006", scene.Generator);

        var material = Assert.Single(scene.Materials);
        Assert.Equal("Wood", material.Name);

        var slot = Assert.Single(material.Slots);
        Assert.Equal("Diffuse Color", slot.Label);
        Assert.Equal(["wood.png", @"C:\old\wood.png"], Source<ExternalSource>(slot.Image).File.Candidates);
        Assert.Equal(@"C:\old\wood.png", Source<ExternalSource>(slot.Image).File.StoredPath);
        Assert.Equal("wood.png", slot.Image.Name);
    }

    [Fact]
    public void EveryTruncationOfAnAsciiFbxReads()
    {
        for (var length = 40; length < Ascii7.Length; length += 7)
            Read(_files.Write($"cut{length}.fbx", Ascii7[..length]));
    }

    [Fact]
    public void AVideoWithNeitherMediaNorAFileIsBroken()
    {
        var scene = Read(_files.Write("empty.fbx", Binary(7400,
            new FbxNode("Objects").With(
                new FbxNode("Material", 1L, Named("M", "Material"), ""),
                new FbxNode("Texture", 10L, Named("T", "Texture"), ""),
                new FbxNode("Texture", 11L, Named("U", "Texture"), ""),
                new FbxNode("Video", 20L, Named("V", "Video"), "Clip")),
            new FbxNode("Connections").With(
                new FbxNode("C", "OP", 10L, 1L, "DiffuseColor"),
                new FbxNode("C", "OO", 20L, 10L),
                new FbxNode("C", "OP", 11L, 1L, "NormalMap")))));

        var slots = Assert.Single(scene.Materials).Slots;
        Assert.Contains("holds no media", Source<BrokenSource>(slots[0].Image).Reason);
        Assert.Contains("names no image", Source<BrokenSource>(slots[1].Image).Reason);
    }

    [Theory]
    [InlineData("DiffuseColor", "Diffuse Color")]
    [InlineData("NormalMap", "Normal Map")]
    [InlineData("3dsMax|main|base_color_map", "Base Color")]
    [InlineData("Maya|TEX_normal_map", "Normal")]
    [InlineData("Maya|baseColor", "Base Color")]
    [InlineData("", "Texture")]
    [InlineData(null, "Texture")]
    public void SlotLabelsAreReadable(string? property, string label) =>
        Assert.Equal(label, FbxReader.SlotLabel(property));

    [Fact]
    public void FilesThatAreNotFbxAreRefused()
    {
        Assert.Throws<InvalidDataException>(() => Read(_files.Write("notfbx.fbx", "{ \"asset\": {} }")));
        Assert.Throws<InvalidDataException>(() => Read(_files.Write("short.fbx", "Kaydara FBX Binary  \0"u8.ToArray())));
    }
}