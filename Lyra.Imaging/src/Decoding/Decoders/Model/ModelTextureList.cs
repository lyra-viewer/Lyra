using Lyra.Common;
using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Content.Variants;
using Lyra.ManagedCodecs.Scene;

namespace Lyra.Imaging.Decoding.Decoders.Model;

internal sealed record ModelTexture(SceneImage Image, TextureSource Source, ImageVariant Variant);

internal sealed record SheetItem(string Caption, int Texture);

internal sealed record ModelSheet(ImageVariant Variant, IReadOnlyList<SheetItem> Items);

internal sealed record ModelTextureSet(IReadOnlyList<ModelTexture> Textures, IReadOnlyList<ModelSheet> Sheets, TextureLayout Layout);

/// <summary>
/// A model's images as one texture each, however many slots use them, so each decodes once; the
/// layout that places them in materials and slots; and a contact sheet for each material with
/// several textures, and one of every material.
/// </summary>
internal static class ModelTextureList
{
    public static ModelTextureSet Build(SceneTextures scene, ReferenceResolver resolver)
    {
        var position = new Dictionary<SceneImage, int>(ReferenceEqualityComparer.Instance);
        var textures = new List<ModelTexture>(scene.Images.Count);
        var usedAs = UsedAs(scene);

        foreach (var image in scene.Images)
        {
            position[image] = textures.Count;

            var source = image.Source switch
            {
                ExternalSource external => resolver.Resolve(external.File),
                BrokenSource => new TextureSource(TextureLocation.Broken, null, null),
                _ => new TextureSource(TextureLocation.Embedded, null, null)
            };

            textures.Add(new ModelTexture(image, source, new ImageVariant(DisplayName(image, usedAs.GetValueOrDefault(image)), 0, 0, Where(source), ByteSize(image.Source, source))));
        }

        var materials = scene.Materials
            .Select(material => new TextureMaterial(
                material.Name ?? $"Material {material.Index}",
                [.. material.Slots.Select(slot => new TextureSlot(slot.Label, position[slot.Image], [.. slot.Alternates.Select(alternate => position[alternate])]))]))
            .ToList();

        var sheets = new List<ModelSheet>();

        for (var i = 0; i < materials.Count; i++)
        {
            var items = Merge(materials[i].Slots.Select(slot => new SheetItem(slot.Label, slot.Texture)));
            if (items.Count < 2)
                continue;

            materials[i] = materials[i] with { Sheet = textures.Count + sheets.Count };
            sheets.Add(Sheet(materials[i].Name, $"{items.Count} textures", items));
        }

        int? overview = null;

        if (materials.Count(material => material.Slots.Count > 0) >= 2)
        {
            var items = Merge(materials.Where(material => material.Slots.Count > 0).Select(material => new SheetItem(material.Name, material.Slots[0].Texture)));

            overview = textures.Count + sheets.Count;
            sheets.Add(Sheet("All materials", $"{items.Count} materials", items));
        }

        var layout = new TextureLayout(
            materials,
            [.. scene.Unreferenced.Select(image => position[image])],
            [.. textures.Select(texture => texture.Source)])
        {
            Overview = overview
        };

        return new ModelTextureSet(textures, sheets, layout);
    }

    public static ImageVariant Described(ModelTexture texture, int width, int height, ImageFormatType format)
    {
        var decoded = width > 0 && height > 0 ? $"{width} x {height} {ImageFormat.FormatName(format)}" : ImageFormat.FormatName(format);

        return texture.Variant with
        {
            Width = width,
            Height = height,
            Detail = texture.Source.Location == TextureLocation.Embedded ? decoded : $"{decoded} · {Where(texture.Source)}"
        };
    }

    private static ModelSheet Sheet(string label, string detail, List<SheetItem> items)
    {
        var size = ContactSheetContent.SizeFor(items.Count);
        return new ModelSheet(new ImageVariant(label, size.Width, size.Height, $"Contact sheet · {detail}", null), items);
    }

    /// <summary>One cell per texture, captioned with everything that uses it.</summary>
    private static List<SheetItem> Merge(IEnumerable<SheetItem> items) =>
        [.. items
            .GroupBy(item => item.Texture)
            .Select(group => new SheetItem(string.Join(", ", group.Select(item => item.Caption).Distinct()), group.Key))];

    private static string Where(TextureSource source) => source.Location switch
    {
        TextureLocation.Linked => "linked",
        TextureLocation.FoundByName => "found by name",
        TextureLocation.Missing => "missing",
        TextureLocation.Broken => "broken",
        _ => "embedded"
    };
    
    private static string DisplayName(SceneImage image, string? usedAs)
    {
        if (image.Name is { Length: > 0 } name)
            return image.Source is ExternalSource { Range: null } ? name : WithoutImageExtension(name);

        if (image.Source is ExternalSource { Range: null } external)
        {
            var stored = external.File.StoredPath;
            if (stored[(stored.LastIndexOfAny(['/', '\\']) + 1)..] is { Length: > 0 } file)
                return file;
        }

        return usedAs ?? $"Image {image.Index}";
    }

    private static string WithoutImageExtension(string name) =>
        ImageFormat.IsSupported(Path.GetExtension(name)) && Path.GetFileNameWithoutExtension(name) is { Length: > 0 } bare ? bare : name;

    private static Dictionary<SceneImage, string> UsedAs(SceneTextures scene)
    {
        var usedAs = new Dictionary<SceneImage, string>(ReferenceEqualityComparer.Instance);

        foreach (var material in scene.Materials)
        {
            foreach (var slot in material.Slots)
            {
                usedAs.TryAdd(slot.Image, scene.Materials.Count > 1 && material.Name is { Length: > 0 } owner ? $"{owner} {slot.Label}" : slot.Label);
            }
        }

        return usedAs;
    }

    private static long? ByteSize(ImageSource image, TextureSource source)
    {
        switch (image)
        {
            case EmbeddedSource embedded:
                return embedded.Range.Length;

            case InlineSource inline:
                return inline.Bytes.Length;

            case ExternalSource { Range: { } range }:
                return range.Length;

            case ExternalSource when source.ResolvedPath is { } path:
                try
                {
                    return new FileInfo(path).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return null;
                }

            default:
                return null;
        }
    }
}