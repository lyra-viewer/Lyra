using Lyra.Common;
using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class TiffNative
{
    /// <summary>
    /// Mirrors <c>TiffDirectoryInfo</c>. One directory of the file, read from its tags alone.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DirectoryInfo
    {
        public uint Width;
        public uint Height;

        /// <summary>SUBFILETYPE bits as stored. Zero when the tag is absent, which is common.</summary>
        public uint SubfileType;

        public uint TileWidth;
        public uint TileHeight;
        public uint RowsPerStrip;

        public ushort PageNumber;
        public ushort PageTotal;
        public ushort BitsPerSample;
        public ushort SamplesPerPixel;
        public ushort SampleFormat;
        public ushort PlanarConfig;
        public ushort Photometric;
        public ushort Compression;
        public byte IsTiled;

        /// <summary>
        /// Non-zero when <see cref="LoadGrayRegion"/> can read this directory: one sample, 1/8/16
        /// bits, unsigned, photometrically gray. The rule lives on the native side so the two
        /// cannot drift.
        /// </summary>
        public byte GrayCapable;

        /// <summary>Non-zero when libtiff's RGBA interface will read this directory.</summary>
        public byte RgbaCapable;

        /// <summary>Non-zero when <see cref="LoadNative"/> will read it.</summary>
        public byte NativeCapable;

        /// <summary>Non-zero when this directory can be read by rectangle.</summary>
        public byte RegionCapable;

        /// <summary>Bytes per pixel a region comes back as: 1 for gray, 4 for color.</summary>
        public byte RegionSamples;

        /// <summary>
        /// Non-zero when a color region's alpha is associated - the file said EXTRASAMPLES 1, and
        /// the color channels come back already multiplied by it.
        /// </summary>
        public byte RegionPremultiplied;
    }

    /// <summary>What <see cref="LoadNative"/> should produce. Mirrors <c>TiffOutputKind</c>.</summary>
    internal enum OutputKind
    {
        Gray8 = 0,
        Rgba8 = 1,
        RgbaFloat = 2
    }

    /// <summary>Size the managed and native structs must agree on.</summary>
    private const int ExpectedDirectoryInfoSize = 48;

    private static bool _directoryEntryPointsMissing;
    private static bool _memoryEntryPointMissing;
    private static int _staleWarningIssued;

    /// <summary>
    /// Whether this build of the native library can enumerate directories and decode a named one.
    /// False against an older one, where a multipage TIFF falls back to showing its first page.
    /// </summary>
    public static bool DirectoryAccessAvailable => !Volatile.Read(ref _directoryEntryPointsMissing);

    public static bool MemoryLoadAvailable => !Volatile.Read(ref _memoryEntryPointMissing);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] // native returns a 1-byte C++ bool
    public static extern bool load_tiff_rgba(string path, out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool load_tiff_rgba_mem(IntPtr data, ulong size, out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool describe_tiff_directories(string path, out IntPtr dirs, out int count);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool describe_tiff_directories_mem(IntPtr data, ulong size, out IntPtr dirs, out int count);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool load_tiff_rgba_at(string path, int directory, out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool load_tiff_rgba_mem_at(IntPtr data, ulong size, int directory, out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool load_tiff_gray_region(string path, int directory, uint x, uint y, uint width, uint height, out IntPtr pixels, out uint stride);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool load_tiff_rgba_region(string path, int directory, uint x, uint y, uint width, uint height, out IntPtr pixels, out uint stride);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool load_tiff_native(string path, int directory, int outputKind, out IntPtr pixels, out int width, out int height, out uint stride);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void free_tiff_directories(IntPtr ptr);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_tiff_pixels(IntPtr ptr);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_tiff_error();

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong get_last_tiff_io_microseconds();

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong get_last_tiff_io_bytes();

    private static bool _ioEntryPointsMissing;
    
    public static (long Bytes, double Ms)? LastIo()
    {
        if (Volatile.Read(ref _ioEntryPointsMissing))
            return null;

        try
        {
            return ((long)get_last_tiff_io_bytes(), get_last_tiff_io_microseconds() / 1000.0);
        }
        catch (EntryPointNotFoundException)
        {
            Volatile.Write(ref _ioEntryPointsMissing, true);
            Logger.Info($"[TiffNative] {nameof(get_last_tiff_io_microseconds)} is missing; this build cannot separate fetch time from decode time for TIFFs it reads by path.");
            return null;
        }
    }

    /// <summary>
    /// Reads what directories the file holds, without decoding a pixel. Returns an empty list when
    /// the entry point is missing - <see cref="DirectoryAccessAvailable"/> then reports that for
    /// the rest of the process - or when the file could not be read.
    /// </summary>
    /// <param name="data">A buffer holding the whole file, or <see cref="IntPtr.Zero"/> to read from the path.</param>
    public static IReadOnlyList<DirectoryInfo> DescribeDirectories(string path, IntPtr data, ulong size)
    {
        if (!DirectoryAccessAvailable)
            return [];

        var managedSize = Marshal.SizeOf<DirectoryInfo>();
        if (managedSize != ExpectedDirectoryInfoSize)
            throw new InvalidOperationException($"TiffDirectoryInfo is {managedSize} bytes managed, {ExpectedDirectoryInfoSize} native.");

        var dirs = IntPtr.Zero;

        try
        {
            var fromBuffer = data != IntPtr.Zero && size > 0;
            var ok = fromBuffer
                ? describe_tiff_directories_mem(data, size, out dirs, out var count)
                : describe_tiff_directories(path, out dirs, out count);

            return ok ? Take(dirs, count) : [];
        }
        catch (EntryPointNotFoundException)
        {
            MissingEntryPoint(nameof(describe_tiff_directories));
            return [];
        }
        finally
        {
            if (dirs != IntPtr.Zero)
                free_tiff_directories(dirs);
        }
    }

    /// <summary>
    /// Decodes one directory. Falls back to the whole-file entry points for directory 0, so an
    /// older native library still serves the single-page case.
    /// </summary>
    public static bool LoadDirectory(string path, IntPtr data, ulong size, int directory,
        out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize)
    {
        pixels = IntPtr.Zero;
        width = height = 0;
        icc = IntPtr.Zero;
        iccSize = 0;

        try
        {
            return data != IntPtr.Zero && size > 0
                ? load_tiff_rgba_mem_at(data, size, directory, out pixels, out width, out height, out icc, out iccSize)
                : load_tiff_rgba_at(path, directory, out pixels, out width, out height, out icc, out iccSize);
        }
        catch (EntryPointNotFoundException)
        {
            return MissingEntryPoint(nameof(load_tiff_rgba_at));
        }
    }

    public static bool LoadFromMemory(IntPtr data, ulong size, out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize)
    {
        pixels = IntPtr.Zero;
        width = height = 0;
        icc = IntPtr.Zero;
        iccSize = 0;

        try
        {
            return load_tiff_rgba_mem(data, size, out pixels, out width, out height, out icc, out iccSize);
        }
        catch (EntryPointNotFoundException)
        {
            Volatile.Write(ref _memoryEntryPointMissing, true);
            return false;
        }
    }

    /// <summary>
    /// Reads a rectangle as gray or color, according to what the directory holds.
    /// </summary>
    public static bool LoadRegion(string path, int directory, bool colour, uint x, uint y, uint width, uint height, out IntPtr pixels, out uint stride) =>
        colour
            ? LoadRgbaRegion(path, directory, x, y, width, height, out pixels, out stride)
            : LoadGrayRegion(path, directory, x, y, width, height, out pixels, out stride);

    /// <summary>
    /// Reads a rectangle of an eight-bit color directory as tightly packed RGBA.
    /// </summary>
    public static bool LoadRgbaRegion(string path, int directory, uint x, uint y, uint width, uint height, out IntPtr pixels, out uint stride)
    {
        pixels = IntPtr.Zero;
        stride = 0;

        try
        {
            return load_tiff_rgba_region(path, directory, x, y, width, height, out pixels, out stride);
        }
        catch (EntryPointNotFoundException)
        {
            return MissingEntryPoint(nameof(load_tiff_rgba_region));
        }
    }

    /// <summary>
    /// Decodes a rectangle at the file's own bit depth into 8-bit grey, one byte per pixel.
    /// </summary>
    public static bool LoadGrayRegion(string path, int directory, uint x, uint y, uint width, uint height,
        out IntPtr pixels, out uint stride)
    {
        pixels = IntPtr.Zero;
        stride = 0;

        try
        {
            return load_tiff_gray_region(path, directory, x, y, width, height, out pixels, out stride);
        }
        catch (EntryPointNotFoundException)
        {
            return MissingEntryPoint(nameof(load_tiff_gray_region));
        }
    }

    /// <summary>
    /// Decodes a whole directory at its own sample layout, for the layouts the RGBA interface
    /// refuses: 10, 12 and 14-bit samples, 32 and 64-bit, IEEE float, and one-bit color.
    /// </summary>
    public static bool LoadNative(string path, int directory, OutputKind kind, out IntPtr pixels, out int width, out int height, out uint stride)
    {
        pixels = IntPtr.Zero;
        width = height = 0;
        stride = 0;

        try
        {
            return load_tiff_native(path, directory, (int)kind, out pixels, out width, out height, out stride);
        }
        catch (EntryPointNotFoundException)
        {
            return MissingEntryPoint(nameof(load_tiff_native));
        }
    }

    private static List<DirectoryInfo> Take(IntPtr dirs, int count)
    {
        var result = new List<DirectoryInfo>(count);
        var stride = Marshal.SizeOf<DirectoryInfo>();

        for (var i = 0; i < count; i++)
            result.Add(Marshal.PtrToStructure<DirectoryInfo>(dirs + i * stride));

        return result;
    }

    /// <summary>
    /// Records that the native library is older than this build expects, says so once, and reports
    /// the failure the caller passes straight back.
    /// </summary>
    private static bool MissingEntryPoint(string entryPoint)
    {
        Volatile.Write(ref _directoryEntryPointsMissing, true);

        if (Interlocked.Exchange(ref _staleWarningIssued, 1) == 0)
            Logger.Warning($"[TiffNative] The native TIFF library has no '{entryPoint}'. It is older than this " +
                           "build expects, so multi-page documents, region reads and unusual sample layouts are " +
                           "all unavailable, and large images will fail rather than stream. Rebuild " +
                           "native/TIFFWrapper and check for a stale copy beside the assembly.");

        return false;
    }
}
