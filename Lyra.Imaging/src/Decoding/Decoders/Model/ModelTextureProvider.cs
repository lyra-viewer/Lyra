using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Content.Variants;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Scene;

namespace Lyra.Imaging.Decoding.Decoders.Model;

internal sealed record DecodedTexture(ICompositeContent Content, ImageVariant Variant, LoadWarning? Warning);

/// <summary>
/// Supplies a model's textures and contact sheets as they are asked for, and redescribes each
/// texture once decoded, since its size is not known before. Holds paths only; every call reads afresh.
/// </summary>
internal sealed class ModelTextureProvider(string modelPath, IReadOnlyList<ModelTexture> textures, IReadOnlyList<ModelSheet> sheets) : IVariantProvider
{
    private volatile VariantContainer? _owner;
    private OpeningFindings? _opening = new(MakeStandIn, DecodePolicy.StandInBudgetBytes);

    public static ICompositeContent? MakeStandIn(ICompositeContent content) => ContentThumbnail.Make(content, DecodePolicy.StandInEdge);

    /// <summary>
    /// Gives the container what decoding the opening sheet found out. Call once, before anything
    /// can ask for a texture.
    /// </summary>
    public void Attach(VariantContainer owner)
    {
        _opening?.HandTo(owner);
        _opening = null;
        _owner = owner;
    }

    public void Discard()
    {
        _opening?.Dispose();
        _opening = null;
    }

    public ICompositeContent Decode(int index, CancellationToken ct)
    {
        if (index >= textures.Count)
            return DecodeSheet(index, null, ct);

        var decoded = DecodeTexture(index, ct);
        Learn(index, decoded);

        return decoded.Content;
    }

    /// <summary>Decodes one texture with the decoder its format gets, keeping only what the container needs.</summary>
    public DecodedTexture DecodeTexture(int index, CancellationToken ct)
    {
        var texture = textures[index];
        var bytes = ModelImageBytes.Read(texture, modelPath, ct);

        using var decoded = bytes is null
            ? StandaloneDecode.FromFile(texture.Source.ResolvedPath!, ct)
            : StandaloneDecode.FromBytes(bytes, $"{modelPath}#{texture.Image.Index}", texture.Image.MimeType, (texture.Image.Source as ExternalSource)?.File.StoredPath ?? texture.Image.Name, ct);

        var variant = ModelTextureList.Described(texture, (int)decoded.LogicalWidth, (int)decoded.LogicalHeight, decoded.ImageFormatType);
        var content = decoded.Content!;

        // The content now belongs to the container, not to the composite that decoded it.
        decoded.Content = null;

        return new DecodedTexture(content, variant, decoded.Warning);
    }

    public ContactSheetContent DecodeSheet(int index, Dictionary<int, LoadFailure>? failures, CancellationToken ct)
    {
        var sheet = sheets[index - textures.Count];
        var cells = new List<SheetCell>(sheet.Items.Count);

        try
        {
            foreach (var item in sheet.Items)
            {
                ct.ThrowIfCancellationRequested();

                cells.Add(KnownFailure(item.Texture, failures) is { } failure
                    ? new SheetCell(item.Caption, null, failure.Message)
                    : DecodeCell(item, failures, ct));
            }

            return new ContactSheetContent(cells);
        }
        catch
        {
            foreach (var cell in cells)
                cell.Content?.Dispose();

            throw;
        }
    }

    private LoadFailure? KnownFailure(int texture, Dictionary<int, LoadFailure>? failures) =>
        failures?.GetValueOrDefault(texture) ?? (_owner?.FailureOf(texture) is { IsTransient: false } lasting ? lasting : null);

    private SheetCell DecodeCell(SheetItem item, Dictionary<int, LoadFailure>? failures, CancellationToken ct)
    {
        using (var lease = _owner?.Borrow(item.Texture, orStandIn: true))
        {
            if (lease is not null)
                return Cell(item, lease.Content);
        }

        try
        {
            var decoded = DecodeTexture(item.Texture, ct);

            using (decoded.Content)
            {
                Learn(item.Texture, decoded);
                LeaveStandIn(item.Texture, decoded.Content);
                return Cell(item, decoded.Content);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            var failure = LoadFailure.From(ex);
            failures?.TryAdd(item.Texture, failure);

            if (!failure.IsExpected)
                Logger.Error(typeof(ModelTextureProvider), $"{item.Caption} failed to decode: {ex}");

            return new SheetCell(item.Caption, null, failure.Message);
        }
    }

    private void Learn(int index, DecodedTexture decoded)
    {
        if (_owner is not { } owner)
        {
            _opening?.Learn(index, decoded.Variant, decoded.Warning);
            return;
        }

        owner.Redescribe(index, decoded.Variant);

        if (decoded.Warning is { } warning)
            owner.RecordWarning(index, warning);
    }

    private void LeaveStandIn(int index, ICompositeContent full)
    {
        if (_owner is { } owner)
            owner.OfferStandIn(index, full);
        else
            _opening?.KeepStandIn(index, full);
    }

    private static SheetCell Cell(SheetItem item, ICompositeContent texture) =>
        ContentThumbnail.Make(texture, ContactSheetContent.CellSize) is { } thumbnail
            ? new SheetCell(item.Caption, thumbnail, null)
            : new SheetCell(item.Caption, null, "Cannot be shown in a sheet");
}