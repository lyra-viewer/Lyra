using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Texture.Ktx;

/// <summary>
/// Reads the structure of a KTX 1.x (.ktx) file into a <see cref="TextureData"/>: it owns the
/// OpenGL <c>glInternalFormat</c> → <see cref="TextureFormat"/> mapping and slices each stored level
/// into subresource byte ranges, validating every range against the file length first. Parsing
/// allocates nothing beyond the subresource list; surface bytes stay as views into the source.
///
/// Treats input as hostile: dimensions, mip/array/face counts, and per-level sizes are all
/// bounds-checked, and surface sizes go through the <c>checked</c> <see cref="TextureFormats.SurfaceByteSize"/>.
/// Big-endian files (rare in practice) are byte-swapped on read: every header field is read in the
/// file's order, and uncompressed pixel data is swapped in <c>glTypeSize</c> chunks so the decoders
/// always see little-endian surfaces.
/// </summary>
public static class KtxReader
{
    private const int HeaderSize = 64;
    private const uint EndiannessRef = 0x04030201; // as written by a little-endian producer

    public static TextureData Read(ReadOnlyMemory<byte> file)
    {
        var span = file.Span;
        if (span.Length < HeaderSize)
        {
            throw new InvalidDataException("KTX: file is too small to contain a header.");
        }

        if (!KtxShared.IsKtx1(span))
        {
            throw new InvalidDataException("KTX: missing or unrecognized KTX 1.1 identifier.");
        }

        // The endianness marker is written in the producer's byte order, so a raw little-endian read
        // yields the reference value for LE files and its byte-reversed form for BE files. Every other
        // field is then read in that same order.
        var endiannessRaw = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4));
        var bigEndian = endiannessRaw == 0x01020304;
        if (!bigEndian && endiannessRaw != EndiannessRef)
        {
            throw new NotSupportedException($"KTX: invalid endianness marker 0x{endiannessRaw:X8}.");
        }

        var glTypeSize = Read32(span, 20, bigEndian);
        var glInternalFormat = Read32(span, 28, bigEndian);
        var width = (int)Read32(span, 36, bigEndian);
        var heightField = (int)Read32(span, 40, bigEndian);
        var depthField = (int)Read32(span, 44, bigEndian);
        var arrayField = (int)Read32(span, 48, bigEndian);
        var faceCount = (int)Read32(span, 52, bigEndian);
        var mipField = (int)Read32(span, 56, bigEndian);
        var kvByteLength = (int)Read32(span, 60, bigEndian);

        // glTypeSize is the byte-swap unit for big-endian pixel data; only 1/2/4 are valid GL type
        // sizes. (For little-endian files it is informational.)
        if (bigEndian && glTypeSize is not (1 or 2 or 4))
        {
            throw new InvalidDataException($"KTX: invalid glTypeSize {glTypeSize} in a big-endian file.");
        }

        var format = KtxFormatMap.FromGl(glInternalFormat);
        if (format == TextureFormat.Unknown)
        {
            throw new NotSupportedException($"KTX: unsupported {KtxFormatMap.DescribeUnsupportedGl(glInternalFormat)}.");
        }

        if (width is <= 0 or > TextureLayout.MaxDimension)
        {
            throw new InvalidDataException($"KTX: implausible width {width}.");
        }

        // height = 0 marks a 1D texture; we display it as a single row.
        var height = heightField == 0 ? 1 : heightField;
        if (height is <= 0 or > TextureLayout.MaxDimension)
        {
            throw new InvalidDataException($"KTX: implausible height {heightField}.");
        }

        var isVolume = depthField > 0;
        var depth = isVolume ? depthField : 1;
        if (depth > TextureLayout.MaxDimension)
        {
            throw new InvalidDataException($"KTX: implausible depth {depthField}.");
        }

        if (faceCount is < 1 or > 6)
        {
            throw new InvalidDataException($"KTX: implausible face count {faceCount}.");
        }

        var arrayCount = arrayField == 0 ? 1 : arrayField;
        if (arrayCount is < 1 or > 0xFFFF)
        {
            throw new InvalidDataException($"KTX: implausible array size {arrayField}.");
        }

        var mipLevels = mipField == 0 ? 1 : mipField; // 0 means "generate"; we keep the one stored level
        var maxMips = TextureLayout.MaxMipLevels(width, height, depth);
        if (mipLevels < 1 || mipLevels > maxMips)
        {
            throw new InvalidDataException($"KTX: mip count {mipLevels} is invalid for {width}x{height}x{depth} (max {maxMips}).");
        }
        
        if (kvByteLength < 0 || HeaderSize + (long)kvByteLength > span.Length)
        {
            throw new InvalidDataException("KTX: key/value data runs past the end of the file.");
        }

        var dataOffset = HeaderSize + kvByteLength;

        var isCubemap = faceCount == 6;
        var origin = KtxShared.ReadOrigin(span.Slice(HeaderSize, kvByteLength), isKtx2: false, bigEndian);

        var kind = TextureLayout.ResolveKind(isVolume, isCubemap, arrayCount);

        // Only uncompressed multibyte components need swapping; a glTypeSize of 1 (and every compressed
        // format) is endian-neutral, so a width of 1 means "no swap".
        var swapWidth = bigEndian ? (int)glTypeSize : 1;
        var subresources = EnumerateSubresources(
            file, format, width, height, depth, mipLevels, arrayCount, faceCount, isVolume, isCubemap, dataOffset, bigEndian, swapWidth);

        return new TextureData
        {
            Format = format,
            FormatName = KtxFormatMap.GlName(glInternalFormat),
            Kind = kind,
            Width = width,
            Height = height,
            Depth = depth,
            MipLevels = mipLevels,
            ArrayLayers = arrayCount,
            Origin = origin,
            Subresources = subresources,
        };
    }

    /// <summary>
    /// Walks the KTX1 image-data layout: one level after another (largest first), each prefixed by a
    /// <c>imageSize</c> field and padded to a 4-byte boundary. For a non-array cubemap, <c>imageSize</c>
    /// is the size of a single face (with per-face <c>cubePadding</c>); for everything else it is the
    /// whole level. Volume levels collapse their depth slices into one subresource, matching DDS.
    /// </summary>
    private static List<Subresource> EnumerateSubresources(
        ReadOnlyMemory<byte> file, TextureFormat format,
        int width, int height, int depth, int mipLevels, int arrayCount, int faceCount,
        bool isVolume, bool isCubemap, int dataOffset, bool bigEndian, int swapWidth)
    {
        var nonArrayCubemap = isCubemap && arrayCount == 1;
        var subresources = new List<Subresource>(arrayCount * faceCount * mipLevels);
        long offset = dataOffset;
        var fileLength = file.Length;
        var span = file.Span;

        for (var mip = 0; mip < mipLevels; mip++)
        {
            if (offset + 4 > fileLength)
                throw new InvalidDataException($"KTX: truncated before mip {mip} image size.");

            var imageSize = Read32(span, (int)offset, bigEndian);
            offset += 4;

            var w = Math.Max(1, width >> mip);
            var h = Math.Max(1, height >> mip);
            var d = isVolume ? Math.Max(1, depth >> mip) : 1;
            var surfaceSize = SurfaceSize(format, w, h); // per 2D image, incl. KTX1 row padding

            if (nonArrayCubemap)
            {
                // imageSize is per-face here; validate it agrees with our own sizing.
                if (imageSize != surfaceSize)
                {
                    throw new InvalidDataException($"KTX: mip {mip} face size {imageSize} != expected {surfaceSize}.");
                }

                for (var face = 0; face < 6; face++)
                {
                    offset = AddSurface(subresources, file, format, mip, layer: 0, face, w, h, depth: 1, surfaceSize, offset, fileLength, bigEndian, swapWidth);
                    offset += Padding(surfaceSize); // cubePadding
                }
            }
            else
            {
                // A 3D ASTC volume can't be sliced per-Z (a block spans Z), so its image is sized as a
                // whole 3D block grid; every other format is a stack of independent 2D slices.
                var sliceCount = isVolume ? d : 1;
                var imageBytes = TextureFormats.Info(format).IsAstc3D
                    ? TextureFormats.SurfaceByteSize3D(format, w, h, d)
                    : checked(surfaceSize * sliceCount);
                var expected = checked(imageBytes * arrayCount * faceCount);
                if (imageSize != expected)
                {
                    throw new InvalidDataException($"KTX: mip {mip} image size {imageSize} != expected {expected}.");
                }

                for (var layer = 0; layer < arrayCount; layer++)
                for (var face = 0; face < faceCount; face++)
                {
                    offset = AddSurface(subresources, file, format, mip, layer, face, w, h, isVolume ? d : 1, imageBytes, offset, fileLength, bigEndian, swapWidth);
                }
            }

            offset += Padding(imageSize); // mipPadding
        }

        return subresources;
    }

    private static long AddSurface(
        List<Subresource> subresources, ReadOnlyMemory<byte> file, TextureFormat format,
        int mip, int layer, int face, int w, int h, int depth, long size, long offset, long fileLength,
        bool bigEndian, int swapWidth)
    {
        if (offset + size > fileLength)
        {
            throw new InvalidDataException($"KTX: subresource (layer {layer}, face {face}, mip {mip}) runs past the end of the file.");
        }

        subresources.Add(new Subresource
        {
            MipLevel = mip,
            ArrayLayer = layer,
            Face = face,
            Width = w,
            Height = h,
            Depth = depth,
            Data = TightSurface(file, (int)offset, format, w, h, depth, bigEndian, swapWidth),
        });

        return offset + size;
    }

    /// <summary>
    /// The stored size of one 2D image, including the KTX1 4-byte row padding (GL_UNPACK_ALIGNMENT)
    /// that uncompressed formats carry. Block-compressed formats are already 4-aligned, so their tight
    /// size is used unchanged.
    /// </summary>
    private static long SurfaceSize(TextureFormat format, int w, int h)
    {
        var info = TextureFormats.Info(format);
        if (info.IsCompressed)
            return TextureFormats.SurfaceByteSize(format, w, h);

        var rowBytes = (long)w * info.BytesPerBlock;
        return checked(AlignUp4(rowBytes) * h);
    }

    /// <summary>
    /// Returns the surface as tightly-packed, little-endian bytes. Compressed surfaces (and uncompressed
    /// ones whose rows are already 4-aligned and need no byte swap) are returned as a zero-copy view; an
    /// uncompressed surface with row padding, or one stored big-endian, is copied row-by-row into an
    /// owned buffer that is then byte-swapped in <paramref name="swapWidth"/> chunks. Compressed block
    /// data is never swapped - its byte layout is endianness-independent.
    /// </summary>
    private static ReadOnlyMemory<byte> TightSurface(
        ReadOnlyMemory<byte> file, int offset, TextureFormat format, int w, int h, int depth, bool bigEndian, int swapWidth)
    {
        var info = TextureFormats.Info(format);
        if (info.IsCompressed)
        {
            var bytes = info.IsAstc3D
                ? TextureFormats.SurfaceByteSize3D(format, w, h, depth)
                : TextureFormats.SurfaceByteSize(format, w, h);
            return file.Slice(offset, (int)bytes);
        }

        var rowBytes = (long)w * info.BytesPerBlock;
        var paddedRow = AlignUp4(rowBytes);
        var swap = bigEndian && swapWidth > 1;
        if (paddedRow == rowBytes && !swap)
            return file.Slice(offset, (int)checked(rowBytes * h * depth));

        var tight = new byte[checked(rowBytes * h * depth)];
        var src = file.Span;
        var dstPos = 0;
        for (var row = 0; row < h * depth; row++)
        {
            src.Slice(offset + (int)(row * paddedRow), (int)rowBytes).CopyTo(tight.AsSpan(dstPos, (int)rowBytes));
            dstPos += (int)rowBytes;
        }

        if (swap) 
            SwapBytes(tight, swapWidth);

        return tight;
    }

    /// <summary>Reverses each <paramref name="width"/>-byte component in place (big-endian -> little-endian).
    /// The tight buffer is always a whole number of components, so chunks never straddle.</summary>
    private static void SwapBytes(Span<byte> data, int width)
    {
        for (var i = 0; i + width <= data.Length; i += width) 
            data.Slice(i, width).Reverse();
    }

    private static long AlignUp4(long value) => (value + 3) & ~3L;

    // KTX pads imageSize / each cube face up to the next 4-byte boundary.
    private static long Padding(long size) => (4 - (size & 3)) & 3;

    private static uint Read32(ReadOnlySpan<byte> span, int offset, bool bigEndian)
        => bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4))
            : BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
}