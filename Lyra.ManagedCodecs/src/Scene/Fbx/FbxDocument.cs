namespace Lyra.ManagedCodecs.Scene.Fbx;

/// <summary>Media bytes an FBX carries itself: a range of a binary file, or decoded base64 from an ASCII one.</summary>
internal abstract record FbxContent;

internal sealed record FbxContentRange(ByteRange Range) : FbxContent;

internal sealed record FbxContentBytes(byte[] Bytes) : FbxContent;

/// <summary>
/// One of the objects the texture graph is made of: a material, texture, layered texture or video.
/// </summary>
/// <param name="Key">What connections call it: the 64-bit id in FBX 7, the full name in FBX 6.</param>
/// <param name="Type">The record name, such as <c>Material</c> or <c>Video</c>.</param>
/// <param name="Name">The object's name, without the class decoration either encoding adds.</param>
internal sealed record FbxObject(string Key, string Type, string Name)
{
    public string? FileName { get; init; }

    public string? RelativeFilename { get; init; }

    public FbxContent? Content { get; init; }

    public bool Is(string type) => Type.Equals(type, StringComparison.OrdinalIgnoreCase);
}

/// <param name="Kind">How the two ends connect: <c>OO</c> object to object, <c>OP</c> object to a property of the parent.</param>
/// <param name="Property">The parent's property for an <c>OP</c> connection, such as <c>DiffuseColor</c>.</param>
internal sealed record FbxConnection(string Kind, string Child, string Parent, string? Property);

/// <summary>The parts of an FBX the texture graph needs; meshes, models and animation are skipped unread.</summary>
internal sealed record FbxDocument(string? Creator, IReadOnlyList<FbxObject> Objects, IReadOnlyList<FbxConnection> Connections, IReadOnlyList<string> Issues)
{
    /// <summary>The object types that make up the texture graph; every other object is skipped unread.</summary>
    public static bool IsGraphType(string type) =>
        type.Equals("Material", StringComparison.OrdinalIgnoreCase)
        || type.Equals("Texture", StringComparison.OrdinalIgnoreCase)
        || type.Equals("LayeredTexture", StringComparison.OrdinalIgnoreCase)
        || type.Equals("Video", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An object's name without its class: <c>Body\0\x01Material</c> in binary FBX 7,
    /// <c>Material::Body</c> in ASCII and in FBX 6.
    /// </summary>
    public static string DisplayName(string raw)
    {
        var binary = raw.IndexOf("\0\u0001", StringComparison.Ordinal);
        if (binary >= 0)
            return raw[..binary];

        var ascii = raw.IndexOf("::", StringComparison.Ordinal);
        return ascii >= 0 ? raw[(ascii + 2)..] : raw;
    }
}