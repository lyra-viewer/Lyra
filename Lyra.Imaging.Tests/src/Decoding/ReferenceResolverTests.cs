using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Decoding.Decoders.Model;
using Lyra.ManagedCodecs.Scene;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public sealed class ReferenceResolverTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lyra-resolve-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException) { }
    }

    private string Write(string relativePath)
    {
        var path = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private ReferenceResolver Resolver => new(Path.Combine(_folder, "jeep.fbx"));

    [Fact]
    public void AStoredPathIsFoundRelativeToTheModel()
    {
        var texture = Write("maps/old wood.png");

        Assert.Equal(new TextureSource(TextureLocation.Linked, "maps/old wood.png", texture), Resolver.Resolve(new FileReference("maps/old wood.png")));
    }

    [Fact]
    public void EveryStoredPathIsTriedWhereItPointsBeforeAnyIsLookedForByName()
    {
        Write("body.png");
        var exact = Write("maps/body.png");

        var source = Resolver.Resolve(new FileReference(@"D:\art\body.png", [@"missing\body.png", Path.Combine(_folder, "maps", "body.png")]));

        Assert.Equal(new TextureSource(TextureLocation.Linked, @"D:\art\body.png", exact), source);
    }

    [Theory]
    [InlineData("elsewhere/Beside.png", "beside.png")]
    [InlineData(@"D:\projects\jeep\body.png", "jeep.fbm/body.png")]
    [InlineData("/Users/somebody/glass.png", "textures/glass.png")]
    public void AFileNotWhereTheModelSaysIsFoundByNameNearby(string stored, string actual)
    {
        var found = Write(actual);

        Assert.Equal(new TextureSource(TextureLocation.FoundByName, stored, found), Resolver.Resolve(new FileReference(stored)));
    }

    [Theory]
    [InlineData("https://example.com/remote.png")]
    [InlineData("gone.png")]
    [InlineData("")]
    public void WhatCannotBeFoundIsMissing(string stored)
    {
        Write("other.png");

        Assert.Equal(new TextureSource(TextureLocation.Missing, stored, null), Resolver.Resolve(new FileReference(stored)));
    }
}