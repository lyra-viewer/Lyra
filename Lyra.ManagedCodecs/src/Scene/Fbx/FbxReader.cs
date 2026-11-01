namespace Lyra.ManagedCodecs.Scene.Fbx;

/// <summary>
/// Reads the materials and images of an FBX model, binary or ASCII, version 6 or 7. The texture
/// graph is assembled from the connection list: video to texture, texture (or layered texture) to
/// a material's property. Only that graph is read; a broken reference is kept on the image it
/// affects, never thrown. Files the model refers to are named, not looked for.
/// </summary>
public static class FbxReader
{
    internal const long MaxAsciiBytes = 1L << 30;

    /// <param name="stream">The model, seekable: a binary FBX is walked record by record, not read whole.</param>
    public static SceneTextures Read(Stream stream, CancellationToken ct = default)
    {
        stream.Position = 0;

        Span<byte> head = stackalloc byte[64];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        head = head[..read];

        FbxDocument document;
        if (FbxBinaryReader.Recognizes(head))
        {
            document = FbxBinaryReader.Read(stream, ct);
        }
        else if (FbxAsciiReader.Recognizes(head))
        {
            if (stream.Length > MaxAsciiBytes)
                throw new InvalidDataException($"The ASCII FBX is {stream.Length} bytes, more than the {MaxAsciiBytes} that are read.");

            var text = new byte[stream.Length];
            stream.Position = 0;
            stream.ReadExactly(text);
            document = FbxAsciiReader.Read(text, ct);
        }
        else
        {
            throw new InvalidDataException("Not an FBX file.");
        }

        return new GraphBuilder(document).Build();
    }

    /// <summary><c>DiffuseColor</c> -> <c>Diffuse Color</c>; <c>3dsMax|main|base_color_map</c> -> <c>Base Color</c>.</summary>
    internal static string SlotLabel(string? property)
    {
        if (string.IsNullOrWhiteSpace(property))
            return "Texture";

        var name = property[(property.LastIndexOf('|') + 1)..].Trim();

        if (name.StartsWith("TEX_", StringComparison.OrdinalIgnoreCase))
            name = name[4..];

        if (name.EndsWith("_map", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
            name = name[..^4];

        return SceneText.Words(name) ?? property;
    }

    private sealed class GraphBuilder(FbxDocument document)
    {
        private readonly Dictionary<string, FbxObject> _objects = Index(document.Objects);
        private readonly ILookup<string, FbxConnection> _byParent = document.Connections.ToLookup(connection => connection.Parent, StringComparer.Ordinal);
        private readonly ILookup<string, FbxConnection> _byChild = document.Connections.ToLookup(connection => connection.Child, StringComparer.Ordinal);
        private readonly Dictionary<string, SceneImage> _imagesByKey = new(StringComparer.Ordinal);
        private readonly List<SceneImage> _images = [];

        public SceneTextures Build()
        {
            // Every image in file order first, used by a material or not; a texture naming nothing only once a material uses it.
            foreach (var o in document.Objects)
            {
                if (o.Is("Video"))
                    ImageOfVideo(o, TextureUsing(o));
                else if (o.Is("Texture") && (VideoOf(o) is not null || PathKey(o) is not null))
                    ImageOfTexture(o);
            }

            var materials = new List<SceneMaterial>();

            foreach (var material in document.Objects.Where(o => o.Is("Material")))
                materials.Add(new SceneMaterial(materials.Count, NonEmpty(material.Name), SlotsOf(material)));

            return new SceneTextures
            {
                Format = SceneFormat.Fbx,
                Generator = NonEmpty(document.Creator),
                Materials = materials,
                Images = _images,
                Issues = document.Issues
            };
        }

        private static Dictionary<string, FbxObject> Index(IReadOnlyList<FbxObject> objects)
        {
            var index = new Dictionary<string, FbxObject>(StringComparer.Ordinal);
            foreach (var o in objects)
                index.TryAdd(o.Key, o);

            return index;
        }

        private List<MaterialSlot> SlotsOf(FbxObject material)
        {
            var slots = new List<MaterialSlot>();

            foreach (var connection in _byParent[material.Key])
            {
                if (!_objects.TryGetValue(connection.Child, out var child))
                    continue;

                var label = SlotLabel(connection.Property);
                var key = connection.Property ?? "";

                if (child.Is("Texture"))
                {
                    slots.Add(new MaterialSlot(key, label, ImageOfTexture(child), []));
                }
                else if (child.Is("LayeredTexture"))
                {
                    var layers = _byParent[child.Key]
                        .Select(layer => _objects.GetValueOrDefault(layer.Child))
                        .OfType<FbxObject>()
                        .Where(layer => layer.Is("Texture"))
                        .ToList();

                    for (var i = 0; i < layers.Count; i++)
                        slots.Add(new MaterialSlot(key, layers.Count == 1 ? label : $"{label} (layer {i + 1})", ImageOfTexture(layers[i]), []));
                }
            }

            // The colour map first, as a material is usually read; the rest in connection order.
            return [.. slots.OrderBy(slot => IsColor(slot.Label) ? 0 : 1)];
        }

        private static bool IsColor(string label) => label.Split(" (layer ")[0] is "Diffuse Color" or "Base Color" or "Color" or "Albedo";

        private SceneImage ImageOfTexture(FbxObject texture)
        {
            if (VideoOf(texture) is { } video)
                return ImageOfVideo(video, texture);

            if (PathKey(texture) is not { } pathKey)
            {
                var key = $"texture:{texture.Key}";
                return Remember(key) ?? Add(key, NonEmpty(texture.Name), new BrokenSource($"Texture {texture.Name} names no image."));
            }

            return Remember(pathKey) ?? Add(pathKey, FileNameOf(texture) ?? NonEmpty(texture.Name), External(texture, null));
        }

        private FbxObject? VideoOf(FbxObject texture) =>
            _byParent[texture.Key]
                .Select(connection => _objects.GetValueOrDefault(connection.Child))
                .FirstOrDefault(child => child?.Is("Video") == true);

        private FbxObject? TextureUsing(FbxObject video) =>
            _byChild[video.Key]
                .Select(connection => _objects.GetValueOrDefault(connection.Parent))
                .FirstOrDefault(parent => parent?.Is("Texture") == true);

        /// <summary>
        /// The image a video carries or names. A video without content takes its paths from the
        /// texture using it when it names none itself.
        /// </summary>
        private SceneImage ImageOfVideo(FbxObject video, FbxObject? texture)
        {
            var key = $"video:{video.Key}";
            if (Remember(key) is { } known)
                return known;

            var name = FileNameOf(video) ?? (texture is null ? null : FileNameOf(texture)) ?? NonEmpty(video.Name);

            if (video.Content is null && (PathKey(video) ?? (texture is null ? null : PathKey(texture))) is { } pathKey)
            {
                if (Remember(pathKey) is not { } shared)
                    return Add(pathKey, name, External(video, texture), also: key);

                _imagesByKey[key] = shared;
                return shared;
            }

            ImageSource source = video.Content switch
            {
                FbxContentRange range => new EmbeddedSource(range.Range),
                FbxContentBytes bytes => new InlineSource(bytes.Bytes),
                _ => new BrokenSource($"Video {video.Name} holds no media and names no file.")
            };

            return Add(key, name, source);
        }

        /// <summary>
        /// The relative paths first, as the model travels with its textures more often than it stays
        /// where it was made; the absolute path is the one reported, as the author's tool wrote it.
        /// </summary>
        private static ExternalSource External(FbxObject first, FbxObject? second)
        {
            string[] candidates = [.. new[] { first.RelativeFilename, second?.RelativeFilename, first.FileName, second?.FileName }.OfType<string>().Where(path => !string.IsNullOrWhiteSpace(path))];
            var stored = NonEmpty(first.FileName) ?? NonEmpty(second?.FileName) ?? candidates[0];

            return new ExternalSource(new FileReference(stored, candidates), null);
        }

        private SceneImage? Remember(string key) => _imagesByKey.GetValueOrDefault(key);

        private SceneImage Add(string key, string? name, ImageSource source, string? also = null)
        {
            var image = new SceneImage(_images.Count, name, null, source);
            _images.Add(image);
            _imagesByKey[key] = image;

            if (also is not null)
                _imagesByKey[also] = image;

            return image;
        }

        /// <summary>Textures and videos naming the same file share one image, so it is decoded once.</summary>
        private static string? PathKey(FbxObject o) => (NonEmpty(o.FileName) ?? NonEmpty(o.RelativeFilename)) is { } path
                ? $"path:{path.Replace('\\', '/').ToLowerInvariant()}"
                : null;

        private static string? FileNameOf(FbxObject o)
        {
            var path = NonEmpty(o.RelativeFilename) ?? NonEmpty(o.FileName);
            return path is null ? null : NonEmpty(path[(path.LastIndexOfAny(['/', '\\']) + 1)..]);
        }

        private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
    }
}