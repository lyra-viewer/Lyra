using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal sealed unsafe class HeifStreamReader : IDisposable
{
    private const int GrowSizeReached = 0;
    private const int GrowSizeBeyondEof = 2;
    private const int GrowError = 3;

    private const int ReadChunk = 1024 * 1024;

    [StructLayout(LayoutKind.Sequential)]
    private struct Callbacks
    {
        public int ApiVersion;
        public delegate* unmanaged[Cdecl]<IntPtr, long> GetPositionFn;
        public delegate* unmanaged[Cdecl]<IntPtr, nuint, IntPtr, int> ReadFn;
        public delegate* unmanaged[Cdecl]<long, IntPtr, int> SeekFn;
        public delegate* unmanaged[Cdecl]<long, IntPtr, int> WaitForFileSizeFn;
    }

    private readonly Stream _stream;
    private GCHandle _self;
    private Callbacks* _callbacks;
    private long _position;
    private CancellationToken _cancellation;
    private long _length = -1;

    public HeifStreamReader(Stream stream)
    {
        _stream = stream;
        _self = GCHandle.Alloc(this);

        _callbacks = (Callbacks*)NativeMemory.AllocZeroed((nuint)sizeof(Callbacks));
        _callbacks->ApiVersion = 1;
        _callbacks->GetPositionFn = &GetPosition;
        _callbacks->ReadFn = &Read;
        _callbacks->SeekFn = &Seek;
        _callbacks->WaitForFileSizeFn = &WaitForFileSize;
    }

    public IntPtr Native => (IntPtr)_callbacks;

    public IntPtr UserData => GCHandle.ToIntPtr(_self);

    public CancellationToken Cancellation
    {
        get => _cancellation;
        set
        {
            _cancellation = value;
            Cancelled = false;
        }
    }

    public bool Cancelled { get; private set; }

    private static HeifStreamReader From(IntPtr userdata) => (HeifStreamReader)GCHandle.FromIntPtr(userdata).Target!;
    
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long GetPosition(IntPtr userdata)
    {
        try
        {
            return From(userdata)._position;
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Read(IntPtr data, nuint size, IntPtr userdata)
    {
        try
        {
            return From(userdata).ReadAt((byte*)data, size) ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Seek(long position, IntPtr userdata)
    {
        if (position < 0)
            return 1;

        try
        {
            From(userdata)._position = position;
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WaitForFileSize(long size, IntPtr userdata)
    {
        try
        {
            return size <= From(userdata).Length() ? GrowSizeReached : GrowSizeBeyondEof;
        }
        catch
        {
            return GrowError;
        }
    }

    private long Length()
    {
        if (_length < 0)
            _length = _stream.Length;

        return _length;
    }

    private bool ReadAt(byte* data, nuint size)
    {
        _stream.Position = _position;

        while (size > 0)
        {
            if (_cancellation.IsCancellationRequested)
            {
                Cancelled = true;
                return false;
            }

            var chunk = (int)Math.Min(size, ReadChunk);
            _stream.ReadExactly(new Span<byte>(data, chunk));

            data += chunk;
            size -= (nuint)chunk;
            _position += chunk;
        }

        return true;
    }

    public void Dispose()
    {
        if (_callbacks is not null)
        {
            NativeMemory.Free(_callbacks);
            _callbacks = null;
        }

        if (_self.IsAllocated)
            _self.Free();
    }
}