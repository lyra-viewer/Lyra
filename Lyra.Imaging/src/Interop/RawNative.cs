using System.Runtime.InteropServices;
using System.Text;

namespace Lyra.Imaging.Interop;

internal static class RawNative
{
    private const string Library = "libraw_native";

    public const int SensorBayer = 0;
    public const int SensorXTrans = 1;
    public const int SensorLinear = 2;
    public const int SensorMonochrome = 3;

    public const int DamageNone = 0;
    public const int DamageTruncated = 1;
    public const int DamageCorrupt = 2;

    public const int PreviewNone = 0;
    public const int PreviewJpeg = 1;
    public const int PreviewRgb8 = 2;

    public const int DemosaicAhd = 3;

    public const int StageIdle = 0;
    public const int StageEarly = 1;
    public const int StageFinishing = 2;

    /// <summary>LibRaw's own limit on how many previews it lists.</summary>
    private const int MaxPreviews = 8;

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RawInfo
    {
        public int Width;
        public int Height;
        public int RawWidth;
        public int RawHeight;
        public int Flip;
        public int Sensor;
        public fixed byte CfaPattern[8];
        public int RawBits;
        public fixed float AsShotMultipliers[4];
        public float BaselineExposure;
        public uint DngVersion;
        public int PreviewCount;
        public int PreviewWidth;
        public int PreviewHeight;
        public int PreviewFormat;
        public fixed byte Make[64];
        public fixed byte Model[64];
        public int PreviewColorSpace;
        public float IsoSpeed;
        public float Shutter;
        public float Aperture;
        public float FocalLength;
        public long Timestamp;
        public fixed byte Lens[64];

        public string CfaPatternText
        {
            get
            {
                fixed (byte* p = CfaPattern)
                    return Text(p, 8);
            }
        }

        public string MakeText
        {
            get
            {
                fixed (byte* p = Make)
                    return Text(p, 64);
            }
        }

        public string ModelText
        {
            get
            {
                fixed (byte* p = Model)
                    return Text(p, 64);
            }
        }

        public string LensText
        {
            get
            {
                fixed (byte* p = Lens)
                    return Text(p, 64);
            }
        }

        public float Multiplier(int index) => AsShotMultipliers[index];

        private static string Text(byte* chars, int capacity)
        {
            var length = new ReadOnlySpan<byte>(chars, capacity).IndexOf((byte)0);
            return Encoding.UTF8.GetString(chars, length < 0 ? capacity : length).Trim();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DevelopOptions
    {
        public int HalfSize;
        public int Demosaic;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Pixels
    {
        public int Width;
        public int Height;
        public IntPtr Data;
        public IntPtr Owner;
        public int BlackLevelMin;
        public int BlackLevelMax;
        public int WhiteLevel;
        public int Damage;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Preview
    {
        public int Format;
        public int Width;
        public int Height;
        public IntPtr Data;
        public nuint Size;
        public IntPtr Owner;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PreviewEntry
    {
        public int Index;
        public int Format;
        public int Width;
        public int Height;
        public long Size;
    }

    internal sealed class ImageHandle() : SafeHandle(IntPtr.Zero, ownsHandle: true)
    {
        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <summary>Opens the same file afresh, on the same terms; a cancelled develop leaves this handle spent.</summary>
        public Func<ImageHandle?> Reopen { get; private set; } = static () => null;

        internal ImageHandle WithReopen(Func<ImageHandle?> reopen)
        {
            Reopen = reopen;
            return this;
        }

        protected override bool ReleaseHandle()
        {
            raw_close(handle);
            return true;
        }
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_raw_error();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr raw_libraw_version();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern ImageHandle raw_open_memory(IntPtr data, nuint size, out RawInfo info);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern ImageHandle raw_open_file([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out RawInfo info);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool raw_extract_preview(ImageHandle image, int minLongSide, out Preview preview);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int raw_list_previews(ImageHandle image, [Out] PreviewEntry[] entries, int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool raw_extract_preview_at(ImageHandle image, int index, out Preview preview);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_raw_preview(IntPtr owner);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool raw_develop(ImageHandle image, in DevelopOptions options, out Pixels pixels);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_raw_pixels(IntPtr owner);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void raw_cancel(ImageHandle image);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int raw_develop_stage(ImageHandle image);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void raw_close(IntPtr image);

    public static string LibRawVersion => NativeErrors.GetUtf8ZOrAnsiZ(raw_libraw_version());

    public static string LibRawShortVersion => ShortVersion(LibRawVersion);

    internal static string ShortVersion(string version) => version.Split('-', 2)[0].Trim();

    /// <summary>Opens a file held in memory, which must outlive the handle; null on failure.</summary>
    public static ImageHandle? OpenMemory(IntPtr data, ulong size, out RawInfo info) =>
        Valid(raw_open_memory(data, (nuint)size, out info))?.WithReopen(() => OpenMemory(data, size, out _));

    /// <summary>Opens a file by path, reading only what later calls need; null on failure.</summary>
    public static ImageHandle? OpenFile(string path, out RawInfo info) =>
        Valid(raw_open_file(path, out info))?.WithReopen(() => OpenFile(path, out _));

    public static PreviewEntry[] ListPreviews(ImageHandle image)
    {
        var entries = new PreviewEntry[MaxPreviews];
        var count = Math.Clamp(raw_list_previews(image, entries, entries.Length), 0, entries.Length);
        return entries[..count];
    }

    private static ImageHandle? Valid(ImageHandle handle)
    {
        if (!handle.IsInvalid)
            return handle;

        handle.Dispose();
        return null;
    }
}