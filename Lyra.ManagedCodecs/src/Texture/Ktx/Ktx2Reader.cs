using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Texture.Ktx;

/// <summary>
/// Reads the structure of a KTX 2.0 (.ktx2) file into a <see cref="TextureData"/>: it owns the
/// Vulkan <c>VkFormat</c> → <see cref="TextureFormat"/> mapping and uses the file's level index to
/// slice each mip into subresource byte ranges, validating every range against the file length first.
/// KTX2 is little-endian by definition and stores images top-left.
///
/// Uncompressed and Zstd / ZLIB supercompressed payloads are supported; a supercompressed level is
/// inflated into its own buffer (so its subresources are not zero-copy views into the source). Basis
/// supercompression is rejected with a precise message until its transcoder lands. Input is treated as
/// hostile throughout.
/// </summary>
public static class Ktx2Reader
{
    private const int FixedHeaderSize = 80;     // through the dfd/kvd/sgd index, before the level index
    private const int LevelIndexEntrySize = 24; // byteOffset + byteLength + uncompressedByteLength (3x u64)

    public static TextureData Read(ReadOnlyMemory<byte> file)
    {
        var span = file.Span;
        if (span.Length < FixedHeaderSize)
        {
            throw new InvalidDataException("KTX2: file is too small to contain a header.");
        }

        if (!KtxShared.IsKtx2(span))
        {
            throw new InvalidDataException("KTX2: missing or unrecognized KTX 2.0 identifier.");
        }

        var vkFormat = Read32(span, 12);
        var width = (int)Read32(span, 20);
        var heightField = (int)Read32(span, 24);
        var depthField = (int)Read32(span, 28);
        var layerField = (int)Read32(span, 32);
        var faceCount = (int)Read32(span, 36);
        var levelField = (int)Read32(span, 40);
        var supercompression = Read32(span, 44);
        var kvdByteOffset = (int)Read32(span, 56);
        var kvdByteLength = (int)Read32(span, 60);

        if (!KtxSupercompression.IsSupported(supercompression))
        {
            throw new NotSupportedException($"KTX2: {DescribeSupercompression(supercompression)} is not yet supported.");
        }

        var format = KtxFormatMap.FromVk(vkFormat);
        if (format == TextureFormat.Unknown)
        {
            throw new NotSupportedException($"KTX2: unsupported {KtxFormatMap.DescribeUnsupportedVk(vkFormat)}.");
        }

        if (width is <= 0 or > TextureLayout.MaxDimension)
        {
            throw new InvalidDataException($"KTX2: implausible width {width}.");
        }

        var height = heightField == 0 ? 1 : heightField; // 0 marks a 1D texture
        if (height is <= 0 or > TextureLayout.MaxDimension)
        {
            throw new InvalidDataException($"KTX2: implausible height {heightField}.");
        }

        var isVolume = depthField > 0;
        var depth = isVolume ? depthField : 1;
        if (depth > TextureLayout.MaxDimension)
        {
            throw new InvalidDataException($"KTX2: implausible depth {depthField}.");
        }

        if (faceCount is not (1 or 6))
        {
            throw new InvalidDataException($"KTX2: implausible face count {faceCount}.");
        }

        var arrayCount = layerField == 0 ? 1 : layerField;
        if (arrayCount is < 1 or > 0xFFFF)
        {
            throw new InvalidDataException($"KTX2: implausible layer count {layerField}.");
        }

        // levelCount 0 requests runtime mip generation (Basis), which has no level index to read.
        if (levelField < 1)
        {
            throw new NotSupportedException("KTX2: files with no stored levels (level count 0) are not supported.");
        }

        var maxMips = TextureLayout.MaxMipLevels(width, height, depth);
        if (levelField > maxMips)
        {
            throw new InvalidDataException($"KTX2: level count {levelField} exceeds the maximum {maxMips} for {width}x{height}x{depth}.");
        }

        var levelIndexEnd = FixedHeaderSize + (long)levelField * LevelIndexEntrySize;
        if (levelIndexEnd > span.Length)
        {
            throw new InvalidDataException("KTX2: file is truncated before the end of the level index.");
        }

        var origin = ReadOrigin(span, kvdByteOffset, kvdByteLength);

        var isCubemap = faceCount == 6;
        var kind = TextureLayout.ResolveKind(isVolume, isCubemap, arrayCount);

        var subresources = EnumerateSubresources(
            file, format, width, height, depth, levelField, arrayCount, faceCount, isVolume, supercompression);

        return new TextureData
        {
            Format = format,
            FormatName = KtxFormatMap.VkName(vkFormat),
            Kind = kind,
            Width = width,
            Height = height,
            Depth = depth,
            MipLevels = levelField,
            ArrayLayers = arrayCount,
            Origin = origin,
            Subresources = subresources,
        };
    }

    /// <summary>
    /// Slices each mip using the file's level index (level 0 = largest first). Within a level the data
    /// is laid out layer -> face -> depth-slice; volumes collapse their depth into one subresource to
    /// match DDS/KTX1. A supercompressed level is inflated to its <c>uncompressedByteLength</c> first
    /// and sliced from that owned buffer; an uncompressed level is sliced zero-copy from the source.
    /// Either way the resulting (uncompressed) level size is checked against our own sizing math.
    /// </summary>
    private static List<Subresource> EnumerateSubresources(
        ReadOnlyMemory<byte> file, TextureFormat format,
        int width, int height, int depth, int levelCount, int arrayCount, int faceCount, bool isVolume, uint supercompression)
    {
        var span = file.Span;
        var fileLength = file.Length;
        var subresources = new List<Subresource>(levelCount * arrayCount * faceCount);

        for (var level = 0; level < levelCount; level++)
        {
            var entry = FixedHeaderSize + level * LevelIndexEntrySize;
            var byteOffset = Read64(span, entry);
            var byteLength = Read64(span, entry + 8);
            var uncompressedLength = Read64(span, entry + 16);

            if (byteOffset > (ulong)fileLength || byteLength > (ulong)fileLength - byteOffset)
            {
                throw new InvalidDataException($"KTX2: level {level} data runs past the end of the file.");
            }

            var w = Math.Max(1, width >> level);
            var h = Math.Max(1, height >> level);
            var d = isVolume ? Math.Max(1, depth >> level) : 1;
            var surfaceSize = TextureFormats.SurfaceByteSize(format, w, h);

            var sliceCount = isVolume ? d : 1;
            var expected = checked(surfaceSize * sliceCount * arrayCount * faceCount);

            // The level's *uncompressed* size is what must match our sizing; for scheme 0 the index
            // sets uncompressedByteLength == byteLength, so this covers both paths.
            if ((ulong)expected != uncompressedLength)
            {
                throw new InvalidDataException($"KTX2: level {level} size {uncompressedLength} != expected {expected}.");
            }

            ReadOnlyMemory<byte> levelData;
            if (supercompression == KtxSupercompression.None)
            {
                levelData = file.Slice((int)byteOffset, (int)byteLength);
            }
            else
            {
                var compressed = file.Slice((int)byteOffset, (int)byteLength);
                levelData = KtxSupercompression.Decompress(supercompression, compressed, (int)expected);
            }

            var offset = 0L;
            for (var layer = 0; layer < arrayCount; layer++)
            for (var face = 0; face < faceCount; face++)
            {
                var size = checked(surfaceSize * sliceCount);
                subresources.Add(new Subresource
                {
                    MipLevel = level,
                    ArrayLayer = layer,
                    Face = face,
                    Width = w,
                    Height = h,
                    Depth = isVolume ? d : 1,
                    Data = levelData.Slice((int)offset, (int)size),
                });

                offset += size;
            }
        }

        return subresources;
    }

    private static TextureOrigin ReadOrigin(ReadOnlySpan<byte> span, int kvdByteOffset, int kvdByteLength)
    {
        if (kvdByteLength <= 0 || kvdByteOffset <= 0 || (long)kvdByteOffset + kvdByteLength > span.Length)
            return TextureOrigin.TopLeft; // no (or unreadable) metadata: KTX2 default

        return KtxShared.ReadOrigin(span.Slice(kvdByteOffset, kvdByteLength), isKtx2: true);
    }

    private static string DescribeSupercompression(uint scheme) => scheme switch
    {
        1 => "BasisLZ supercompression",
        2 => "Zstandard supercompression",
        3 => "ZLIB supercompression",
        _ => $"supercompression scheme {scheme}",
    };

    private static uint Read32(ReadOnlySpan<byte> span, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));

    private static ulong Read64(ReadOnlySpan<byte> span, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
}
