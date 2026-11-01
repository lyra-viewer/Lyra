using System.Text.Json;

namespace Lyra.ManagedCodecs.Scene.Gltf;

/// <summary>
/// Reads the materials and images of a glTF 2.0 model, as <c>.gltf</c> JSON or <c>.glb</c>. Only the
/// texture graph is read; meshes, nodes and animation are not. A broken reference is recorded on the
/// image or in <see cref="SceneTextures.Issues"/>, never thrown, so one bad image does not hide the rest.
/// </summary>
public static class GltfReader
{
    internal const long MaxJsonBytes = 1L << 30;

    /// <param name="stream">The model, seekable: a GLB's binary chunk is located, not read.</param>
    public static SceneTextures Read(Stream stream, CancellationToken ct = default)
    {
        stream.Position = 0;

        Span<byte> head = stackalloc byte[4];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = 0;

        if (GlbContainer.Recognizes(head[..read]))
        {
            var glb = GlbContainer.Read(stream, MaxJsonBytes);
            return new Parser(SceneFormat.Glb, glb.Binary, glb.Issues, ct).Parse(glb.Json);
        }

        if (stream.Length > MaxJsonBytes)
            throw new InvalidDataException($"The glTF file is {stream.Length} bytes, more than the {MaxJsonBytes} that are read.");

        var json = new byte[stream.Length];
        stream.ReadExactly(json);

        return new Parser(SceneFormat.Gltf, binary: null, [], ct).Parse(json);
    }

    private readonly record struct TextureImages(SceneImage Image, IReadOnlyList<SceneImage> Alternates);

    private abstract record BufferData;
    private sealed record ChunkBuffer(ByteRange Range) : BufferData;
    private sealed record InlineBuffer(byte[] Bytes) : BufferData;
    private sealed record FileBuffer(FileReference File) : BufferData;
    private sealed record BrokenBuffer(string Reason) : BufferData;

    private sealed class Parser(SceneFormat format, ByteRange? binary, IReadOnlyList<string> containerIssues, CancellationToken ct)
    {
        private const int MaxIssues = 100;
        private const int MaxSlotDepth = 8;

        /// <summary>The core slots, in the order a material is usually read. Others follow in file order.</summary>
        private static readonly string[] CoreSlots =
        [
            "pbrMetallicRoughness.baseColorTexture",
            "pbrMetallicRoughness.metallicRoughnessTexture",
            "normalTexture",
            "occlusionTexture",
            "emissiveTexture"
        ];

        private static readonly JsonDocumentOptions Options = new()
        {
            MaxDepth = 512,
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        private readonly List<string> _issues = [.. containerIssues];
        private int _suppressedIssues;

        private readonly Dictionary<int, BufferData> _buffers = [];
        private JsonElement[] _bufferElements = [];
        private JsonElement[] _views = [];
        private JsonElement[] _textures = [];
        private SceneImage[] _images = [];

        public SceneTextures Parse(byte[] json)
        {
            var memory = json.AsMemory();
            if (memory.Span.StartsWith("﻿"u8))
                memory = memory[3..];

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(memory, Options);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"The glTF JSON is malformed: {ex.Message}", ex);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The glTF JSON is not an object.");

                var generator = ReadAsset(root);

                _bufferElements = Array(root, "buffers");
                _views = Array(root, "bufferViews");
                _textures = Array(root, "textures");
                _images = ReadImages(Array(root, "images"));

                var materials = ReadMaterials(Array(root, "materials"));

                if (_suppressedIssues > 0)
                    _issues.Add($"…and {_suppressedIssues} more.");

                return new SceneTextures
                {
                    Format = format,
                    Generator = generator,
                    Materials = materials,
                    Images = _images,
                    Issues = _issues
                };
            }
        }

        private string? ReadAsset(JsonElement root)
        {
            if (!root.TryGetProperty("asset", out var asset) || asset.ValueKind != JsonValueKind.Object)
            {
                Issue("The file has no asset description.");
                return null;
            }

            var version = String(asset, "version");
            if (version is not null && (version == "1" || version.StartsWith("1.", StringComparison.Ordinal)))
                throw new NotSupportedException($"glTF {version} is not supported, only glTF 2.0.");

            return String(asset, "generator");
        }

        private SceneImage[] ReadImages(JsonElement[] elements)
        {
            var images = new SceneImage[elements.Length];

            for (var i = 0; i < elements.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var element = elements[i];
                var mimeType = String(element, "mimeType");

                ImageSource source;
                if (String(element, "uri") is { } uri)
                {
                    if (IsDataUri(uri))
                    {
                        source = DecodeDataUri(uri, out var dataMimeType) is { } bytes
                            ? new InlineSource(bytes)
                            : new BrokenSource($"Image {i} has a data URI that cannot be decoded.");

                        mimeType ??= dataMimeType;
                    }
                    else
                    {
                        source = new ExternalSource(new FileReference(Uri.UnescapeDataString(uri)), null);
                    }
                }
                else if (Int(element, "bufferView") is { } view)
                {
                    source = ViewSource(view, i);
                }
                else
                {
                    source = new BrokenSource($"Image {i} has neither a URI nor a buffer view.");
                }

                images[i] = new SceneImage(i, String(element, "name"), mimeType, source);
            }

            return images;
        }

        private ImageSource ViewSource(int viewIndex, int imageIndex)
        {
            if (!TryGet(_views, viewIndex, out var view))
                return new BrokenSource($"Image {imageIndex} points at buffer view {viewIndex}, which does not exist.");

            var offset = Long(view, "byteOffset") ?? 0;
            if (Int(view, "buffer") is not { } bufferIndex || Long(view, "byteLength") is not { } length || offset < 0 || length < 0)
                return new BrokenSource($"Buffer view {viewIndex} is malformed.");

            return Buffer(bufferIndex) switch
            {
                ChunkBuffer chunk => Fits(offset, length, chunk.Range.Length) 
                    ? new EmbeddedSource(new ByteRange(chunk.Range.Offset + offset, length)) 
                    : new BrokenSource($"Buffer view {viewIndex} reaches past the end of the binary chunk."),
                InlineBuffer inline => Fits(offset, length, inline.Bytes.Length) 
                    ? new InlineSource(inline.Bytes.AsMemory((int)offset, (int)length)) 
                    : new BrokenSource($"Buffer view {viewIndex} reaches past the end of buffer {bufferIndex}."),
                FileBuffer file => new ExternalSource(file.File, new ByteRange(offset, length)),
                BrokenBuffer broken => new BrokenSource(broken.Reason),
                _ => throw new InvalidOperationException()
            };
        }

        private BufferData Buffer(int index)
        {
            if (_buffers.TryGetValue(index, out var known))
                return known;

            var buffer = ReadBuffer(index);
            _buffers[index] = buffer;
            return buffer;
        }

        private BufferData ReadBuffer(int index)
        {
            if (!TryGet(_bufferElements, index, out var element))
                return new BrokenBuffer($"Buffer {index} does not exist.");

            if (String(element, "uri") is not { } uri)
            {
                return format == SceneFormat.Glb && index == 0 && binary is { } chunk
                    ? new ChunkBuffer(chunk)
                    : new BrokenBuffer($"Buffer {index} has no data.");
            }

            if (IsDataUri(uri))
            {
                return DecodeDataUri(uri, out _) is { } bytes
                    ? new InlineBuffer(bytes)
                    : new BrokenBuffer($"Buffer {index} has a data URI that cannot be decoded.");
            }

            return new FileBuffer(new FileReference(Uri.UnescapeDataString(uri)));
        }

        private SceneMaterial[] ReadMaterials(JsonElement[] elements)
        {
            var materials = new SceneMaterial[elements.Length];

            for (var i = 0; i < elements.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var slots = new List<MaterialSlot>();
                if (elements[i].ValueKind == JsonValueKind.Object)
                    CollectSlots(elements[i], "", 0, i, slots);

                materials[i] = new SceneMaterial(i, String(elements[i], "name"), [.. slots.OrderBy(slot => CoreRank(slot.Key))]);
            }

            return materials;
        }

        /// <summary>
        /// Any property named <c>…Texture</c> holding an <c>index</c> is a slot, wherever it sits, so
        /// extensions this reader has never heard of still show their textures.
        /// </summary>
        private void CollectSlots(JsonElement element, string prefix, int depth, int material, List<MaterialSlot> slots)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

                if (property.Name.EndsWith("Texture", StringComparison.Ordinal) && property.Value.TryGetProperty("index", out var index))
                {
                    if (index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out var texture))
                        Issue($"Material {material} has a malformed texture index at {key}.");
                    else if (TextureImagesOf(texture, material, key) is { } images)
                        slots.Add(new MaterialSlot(key, SlotLabel(property.Name), images.Image, images.Alternates));
                }
                else if (depth < MaxSlotDepth)
                {
                    CollectSlots(property.Value, key, depth + 1, material, slots);
                }
            }
        }

        /// <summary>
        /// The image a texture shows, taking an extension's image (KTX2, WebP, AVIF, DDS) over the
        /// plain one, which is then kept as an alternate.
        /// </summary>
        private TextureImages? TextureImagesOf(int textureIndex, int material, string key)
        {
            if (!TryGet(_textures, textureIndex, out var texture))
            {
                Issue($"Material {material} uses texture {textureIndex} at {key}, which does not exist.");
                return null;
            }

            var sources = new List<int>();

            if (texture.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object)
            {
                foreach (var extension in extensions.EnumerateObject())
                {
                    if (Int(extension.Value, "source") is { } source)
                        sources.Add(source);
                }
            }

            if (Int(texture, "source") is { } plain)
                sources.Add(plain);

            var images = new List<SceneImage>();
            foreach (var source in sources)
            {
                if (source >= 0 && source < _images.Length)
                {
                    if (!images.Contains(_images[source]))
                        images.Add(_images[source]);
                }
                else
                {
                    Issue($"Texture {textureIndex} uses image {source}, which does not exist.");
                }
            }

            if (images.Count == 0)
            {
                Issue($"Texture {textureIndex} has no image.");
                return null;
            }

            return new TextureImages(images[0], [.. images.Skip(1)]);
        }

        private static int CoreRank(string key) => System.Array.IndexOf(CoreSlots, key) is var rank and >= 0 ? rank : CoreSlots.Length;

        private void Issue(string text)
        {
            if (_issues.Count < MaxIssues)
                _issues.Add(text);
            else
                _suppressedIssues++;
        }
    }

    internal static string SlotLabel(string property)
    {
        var name = property.EndsWith("Texture", StringComparison.Ordinal) && property.Length > "Texture".Length
            ? property[..^"Texture".Length]
            : property;

        var label = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_')
            {
                label.Append(' ');
                continue;
            }

            if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                label.Append(' ');

            label.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }

        return label.ToString();
    }

    private static bool Fits(long offset, long length, long total) => offset <= total && length <= total - offset;

    private static bool IsDataUri(string uri) => uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    internal static byte[]? DecodeDataUri(string uri, out string? mimeType)
    {
        mimeType = null;

        var comma = uri.IndexOf(',');
        if (comma < 0)
            return null;

        var parameters = uri[5..comma].Split(';');
        if (parameters[0].Contains('/'))
            mimeType = parameters[0];

        if (!parameters.Skip(1).Any(parameter => parameter.Equals("base64", StringComparison.OrdinalIgnoreCase)))
            return null;

        try
        {
            return Convert.FromBase64String(uri[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static JsonElement[] Array(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()] : [];

    private static bool TryGet(JsonElement[] elements, int index, out JsonElement element)
    {
        element = default;
        if (index < 0 || index >= elements.Length || elements[index].ValueKind != JsonValueKind.Object)
            return false;

        element = elements[index];
        return true;
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? Long(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}
