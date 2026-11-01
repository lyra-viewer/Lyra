using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Content.Variants;
using Lyra.Imaging.Decoding.Decoders.Model;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Scene;
using Lyra.ManagedCodecs.Scene.Fbx;
using Lyra.ManagedCodecs.Scene.Gltf;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class ModelDecoder : DecoderBase
{
    public override IReadOnlyList<ImageFormatType> Formats { get; } = [ImageFormatType.Gltf, ImageFormatType.Fbx];

    public override DecoderCapabilities Capabilities => DecoderCapabilities.OpensOnlyDirectly;

    private readonly record struct Opening(int Index, ICompositeContent Content, ImageVariant? Variant, LoadWarning? Warning);

    protected override void Decode(DecodeContext context, CancellationToken ct)
    {
        var path = context.Source.RequireFile();

        SceneTextures scene;
        using (var stream = DecoderIO.OpenRandomAccessRead(path))
            scene = context.ImageFormatType == ImageFormatType.Fbx ? FbxReader.Read(stream, ct) : GltfReader.Read(stream, ct);

        var (listed, sheets, layout) = ModelTextureList.Build(scene, new ReferenceResolver(path));
        IReadOnlyList<ModelTexture> textures = [.. listed.Select(texture => TextureProbe.Probed(texture, path, ct))];

        Describe(context, scene, layout);

        if (textures.Count == 0)
            throw new LoadFailureException(LoadFailureKind.NothingToShow, "The model holds no textures");

        var failures = new Dictionary<int, LoadFailure>();
        for (var i = 0; i < textures.Count; i++)
        {
            if (ModelImageBytes.Unavailable(textures[i]) is { } unavailable)
                failures[i] = LoadFailure.From(unavailable, textures[i].Source.StoredPath);
        }

        var provider = new ModelTextureProvider(path, textures, sheets);
        Opening? opening = null;

        try
        {
            opening = OpenOnSheet(provider, layout, failures, ct)
                      ?? DecodeFirst(path, provider, textures, layout.InListOrder().ToList(), failures, ct)
                      ?? throw Unopenable(textures, layout, failures);
        }
        catch
        {
            provider.Discard();
            throw;
        }

        context.Content = Contain(opening.Value, textures, sheets, layout, provider, failures);

        if (opening.Value.Content is ContactSheetContent sheet && sheet.Cells.All(cell => cell.Content is null))
        {
            context.Warning = new LoadWarning("No texture could be shown", failures.Count == textures.Count
                ? $"All {textures.Count} textures are missing or broken"
                : $"{failures.Count} of {textures.Count} textures are missing or broken");
        }
    }

    /// <summary>The container of every texture and sheet, holding the opening one and what was found out on the way.</summary>
    private static VariantContainer Contain(Opening opening, IReadOnlyList<ModelTexture> textures, IReadOnlyList<ModelSheet> sheets, TextureLayout layout, ModelTextureProvider provider, Dictionary<int, LoadFailure> failures)
    {
        var (active, content, decoded, warning) = opening;

        IReadOnlyList<ImageVariant> variants =
        [
            .. textures.Select((texture, i) => i == active && decoded is not null ? decoded : texture.Variant),
            .. sheets.Select(sheet => sheet.Variant)
        ];

        VariantContainer set;
        try
        {
            set = new VariantContainer(variants, active, content, provider, DecodePolicy.ResidentTextureBudgetBytes)
            {
                Kind = VariantKind.Textures,
                Textures = layout,
                StepOrder = layout.ShowableInListOrder(),
                PreloadsNeighbours = true,
                MakeStandIn = ModelTextureProvider.MakeStandIn,
                StandInBudgetBytes = DecodePolicy.StandInBudgetBytes
            };
        }
        catch
        {
            provider.Discard();
            content.Dispose();
            throw;
        }

        provider.Attach(set);

        foreach (var (index, failure) in failures)
            set.RecordUnavailable(index, failure);

        if (warning is not null)
            set.RecordWarning(active, warning);

        return set;
    }

    private static Opening? OpenOnSheet(ModelTextureProvider provider, TextureLayout layout, Dictionary<int, LoadFailure> failures, CancellationToken ct)
    {
        if ((layout.Overview ?? layout.Materials.Select(material => material.Sheet).FirstOrDefault(sheet => sheet is not null)) is not { } index)
            return null;

        return new Opening(index, provider.DecodeSheet(index, failures, ct), null, null);
    }
    
    private Opening? DecodeFirst(string path, ModelTextureProvider provider, IReadOnlyList<ModelTexture> textures, List<int> order, Dictionary<int, LoadFailure> failures, CancellationToken ct)
    {
        foreach (var i in order)
        {
            if (failures.ContainsKey(i))
                continue;

            try
            {
                var decoded = provider.DecodeTexture(i, ct);
                return new Opening(i, decoded.Content, decoded.Variant, decoded.Warning);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                var failure = LoadFailure.From(ex);
                if (failure.IsExpected)
                    Logger.Warning($"[{Name}] {textures[i].Variant.Label} in {Path.GetFileName(path)}: {failure.Description}");
                else
                    Logger.Error($"[{Name}] {textures[i].Variant.Label} in {Path.GetFileName(path)} failed to decode: {ex}");
                failures[i] = failure;
            }
        }

        return null;
    }

    private static LoadFailureException Unopenable(IReadOnlyList<ModelTexture> textures, TextureLayout layout, Dictionary<int, LoadFailure> failures)
    {
        var reason = layout.InListOrder().Select(failures.GetValueOrDefault).FirstOrDefault(failure => failure is not null)?.Description ?? "Nothing was decoded";

        return new LoadFailureException(LoadFailureKind.DecodeFailed, textures.Count == 1
            ? reason
            : $"None of the model's {textures.Count} textures could be shown. The first: {reason}");
    }

    private static void Describe(DecodeContext context, SceneTextures scene, TextureLayout layout)
    {
        if (scene.Generator is { Length: > 0 } generator)
            context.AddFormatSpecific("Generator", generator);

        context.AddFormatSpecific("Materials", scene.Materials.Count.ToString());
        context.AddFormatSpecific("Images", scene.Images.Count.ToString());

        AddCount(context, "Missing", layout.Sources.Count(source => source.Location == TextureLocation.Missing));
        AddCount(context, "Found by Name", layout.Sources.Count(source => source.Location == TextureLocation.FoundByName));
        AddCount(context, "Broken", layout.Sources.Count(source => source.Location == TextureLocation.Broken));
        AddCount(context, "Unreferenced", layout.Unreferenced.Count);

        if (scene.Issues.Count > 0)
            context.AddFormatSpecific("Problems", string.Join("; ", scene.Issues.Take(5)) + (scene.Issues.Count > 5 ? "; …" : ""));
    }

    private static void AddCount(DecodeContext context, string key, int count)
    {
        if (count > 0)
            context.AddFormatSpecific(key, count.ToString());
    }
}