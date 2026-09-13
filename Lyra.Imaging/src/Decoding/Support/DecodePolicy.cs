namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// What this project considers a large image, and what it will spend on one.
///
/// The one budget that is <em>not</em> here is the decoded-image cache, which is sized from the
/// machine rather than fixed - see <see cref="Loading.ImageLoader.CacheBudgetBytes"/>.
/// </summary>
internal static class DecodePolicy
{
    /// <summary>
    /// Above this, decoded pixels are published as a preview plus tiles rather than one texture,
    /// so the GPU only ever holds what is on screen. Every format that can produce a raster this
    /// large asks the same question here.
    /// </summary>
    public const long SingleTextureCeilingBytes = 256L * 1024 * 1024;

    /// <summary>
    /// The same ceiling, asked of a form that does not exist yet: when a TIFF's gray directory
    /// <em>would</em> cross it as RGBA8, it is read at its own depth instead and never pays the
    /// four bytes a pixel.
    /// </summary>
    public const long RgbaFormCeilingBytes = SingleTextureCeilingBytes;

    /// <summary>
    /// Tile edge in pixels, for every grid cut here. 2048 costs 16 MiB per RGBA8 tile, so the
    /// handful covering a screen stays far inside any sane cache, while keeping the tile count
    /// low enough that walking them per frame is free (a 16K image is 8x4).
    /// </summary>
    public const int TileEdge = 2048;

    /// <summary>
    /// What one image may hold resident as decoded tiles at once. Distinct from
    /// <see cref="MaxTileDecodeBytes"/>: this caps the whole set, that one caps a single tile.
    /// </summary>
    public const long ResidentTileBudgetBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The most one tile may cost to decode, which is what a tiler sizes its grid against when it
    /// picks the edge itself rather than being told one.
    /// </summary>
    public const long MaxTileDecodeBytes = 64L * 1024 * 1024;

    /// <summary>
    /// What a multi-rendition container - the pages of a document - may hold resident as decoded
    /// pages before the least recently shown are dropped.
    /// </summary>
    public const long ResidentPageBudgetBytes = 256L * 1024 * 1024;

    /// <summary>
    /// What one band of a streaming preview pass may cost. Larger than a tile budget because the
    /// band is transient: it is read, averaged into the preview, and released before the next.
    /// </summary>
    public const long PreviewBandBudgetBytes = 192L * 1024 * 1024;

    /// <summary>
    /// How much larger than the display a preview is built, so it stays sharp at fit-to-window
    /// with headroom for a little zoom before tiles take over.
    /// </summary>
    public const float PreviewSizeMultiplier = 2.0f;

    /// <summary>
    /// Stands in for the display when no bounds have been published yet - decode can finish before
    /// the first <c>DisplayBoundsChangedEvent</c>, and a zero-sized preview would be worse than a
    /// guess.
    /// </summary>
    public const int FallbackDisplayEdge = 2560;
}
