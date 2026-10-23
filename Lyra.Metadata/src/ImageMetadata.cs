using Lyra.Metadata.Containers;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Icc;
using Lyra.Metadata.Iptc;
using Lyra.Metadata.Photoshop;
using Lyra.Metadata.Support;
using Lyra.Metadata.Tiff;
using Lyra.Metadata.Xmp;

namespace Lyra.Metadata;

public sealed class ImageMetadata
{
    public ExifMetadata? Exif { get; private init; }

    public XmpPacket? Xmp { get; private init; }

    public IptcRecords? Iptc { get; private init; }

    public IccProfile? Icc { get; private init; }

    public IReadOnlyList<IFormatFacts> Facts { get; private init; } = [];

    public bool Unreadable { get; private init; }

    public bool IsEmpty { get; private init; } = true;

    private ImageMetadata() { }

    public static ImageMetadata Empty { get; } = new();

    private static readonly ImageMetadata UnreadableFile = new() { Unreadable = true };
    
    public static ImageMetadata? Read(Stream stream)
    {
        if (TiffDirectoryReader.IsTiff(stream))
        {
            using var _ = stream.KeepPosition();
            if (ExifMetadata.Read(stream) is not { } exif)
                return UnreadableFile;

            if (Rw2Container.CameraExif(stream) is { } cameraTiff)
                using (var tiff = new MemoryStream(cameraTiff, writable: false))
                    if (ExifMetadata.Read(tiff) is { } camera)
                        exif = exif.WithCameraValues(camera);

            var resolver = new Resolver();
            resolver.OfferExif(exif);
            return resolver.Resolve([]);
        }

        return ContainerReader.Read(stream) is { } container ? From(container) : null;
    }

    /// <summary>The metadata of blocks a container holds, located here or by the caller (JPEG XL, JPEG 2000, PSD).</summary>
    public static ImageMetadata From(ContainerContents container)
    {
        if (container.Failed)
            return UnreadableFile;

        var resolver = new Resolver();
        foreach (var block in container.Blocks)
        {
            switch (block.Kind)
            {
                case MetadataBlockKind.Exif:
                    using (var tiff = new MemoryStream(block.Data, writable: false))
                        if (ExifMetadata.Read(tiff, block.FirstDirectory) is { } exif)
                            resolver.OfferExif(exif);
                    break;

                case MetadataBlockKind.Xmp:
                    resolver.Xmp.Offer(block.Data);
                    break;

                case MetadataBlockKind.Iptc:
                    resolver.Iptc.Offer(block.Data);
                    break;

                case MetadataBlockKind.Icc:
                    resolver.Icc.Offer(block.Data);
                    break;
            }
        }

        return resolver.Resolve(container.Facts);
    }

    private sealed class Resolver
    {
        private readonly List<ExifMetadata> _exif = [];

        public FirstOffered<XmpPacket> Xmp { get; } = new();
        public FirstOffered<IptcRecords> Iptc { get; } = new();
        public FirstOffered<IccProfile> Icc { get; } = new();

        /// <summary>
        /// An EXIF block, and the blocks its IFD0 carries in the order its tags are read: XMP
        /// (0x02BC), IPTC (0x83BB), Photoshop's copies (0x8649), then ICC (0x8773) - which is why
        /// Photoshop's ICC comes first.
        /// </summary>
        public void OfferExif(ExifMetadata exif)
        {
            _exif.Add(exif);
            var photoshop = exif.Photoshop is { } block ? PhotoshopResources.Parse(block) : null;

            Xmp.Offer(exif.Xmp);
            Xmp.Offer(photoshop?.Find(PhotoshopResources.Xmp));

            Iptc.Offer(exif.Iptc);
            Iptc.Offer(photoshop?.Find(PhotoshopResources.Iptc));

            Icc.Offer(photoshop?.Find(PhotoshopResources.IccProfile));
            Icc.Offer(exif.Icc);
        }

        public ImageMetadata Resolve(IReadOnlyList<IFormatFacts> facts) => new()
        {
            Exif = ExifMetadata.Combine(_exif),
            Xmp = Xmp.Value,
            Iptc = Iptc.Value,
            Icc = Icc.Value,
            Facts = facts,
            IsEmpty = _exif.Count == 0 && facts.Count == 0 && !Xmp.Seen && !Iptc.Seen && !Icc.Seen
        };
    }

    /// <summary>The first block of a kind offered; once one is, later ones are not even read.</summary>
    private sealed class FirstOffered<T> where T : class, IMetadataBlock<T>
    {
        public bool Seen { get; private set; }

        public T? Value { get; private set; }

        public void Offer(byte[]? bytes)
        {
            if (bytes is null || Seen)
                return;

            Seen = true;
            Value = T.Parse(bytes);
        }
    }
}