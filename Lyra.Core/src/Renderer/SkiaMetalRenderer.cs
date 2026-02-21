using System.Runtime.InteropServices;
using Lyra.DropStatusProvider;
using Lyra.SdlCore;
using SkiaSharp;
using static SDL3.SDL;

namespace Lyra.Renderer;

public sealed class SkiaMetalRenderer : SkiaRendererBase
{
    // SDL3 metal glue
    private readonly IntPtr _metalView;   // SDL_MetalView (typedef void*)
    private readonly IntPtr _metalLayer;  // CAMetalLayer*

    // Metal objects
    private readonly IntPtr _device;      // id<MTLDevice>
    private readonly IntPtr _queue;       // id<MTLCommandQueue>

    private readonly GRMtlBackendContext _mtlBackend;
    private readonly GRContext _grContext;

    // Per-frame
    private IntPtr _currentDrawable; // id<CAMetalDrawable>

    private GRBackendRenderTarget? _currentRenderTarget;
    private IntPtr _autoreleasePool; // NSAutoreleasePool*

    // MTLPixelFormatBGRA8Unorm (stable value)
    private const ulong MTLPixelFormatBGRA8Unorm = 80;

    public SkiaMetalRenderer(IntPtr window, PixelSize drawableSize, IDropProgressProvider dropProgressProvider)
        : base(drawableSize, dropProgressProvider)
    {
        _metalView = MetalCreateView(window);
        if (_metalView == IntPtr.Zero)
            throw new InvalidOperationException("SDL_Metal_CreateView failed.");

        _metalLayer = MetalGetLayer(_metalView);
        if (_metalLayer == IntPtr.Zero)
            throw new InvalidOperationException("SDL_Metal_GetLayer returned null CAMetalLayer.");

        _device = MetalNative.MTLCreateSystemDefaultDevice();
        if (_device == IntPtr.Zero)
            throw new InvalidOperationException("MTLCreateSystemDefaultDevice returned null.");

        // layer.device = device
        ObjC.SendVoid_IntPtr(_metalLayer, ObjC.Sel("setDevice:"), _device);
        // layer.pixelFormat = BGRA8
        ObjC.SendVoid_UInt64(_metalLayer, ObjC.Sel("setPixelFormat:"), MTLPixelFormatBGRA8Unorm);
        // layer.framebufferOnly = NO
        ObjC.SendVoid_Bool(_metalLayer, ObjC.Sel("setFramebufferOnly:"), false);

        // queue = [device newCommandQueue]
        _queue = ObjC.Send_IntPtr(_device, ObjC.Sel("newCommandQueue"));
        if (_queue == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create MTLCommandQueue (newCommandQueue)." );

        _mtlBackend = new GRMtlBackendContext
        {
            DeviceHandle = _device,
            QueueHandle = _queue,
        };

        _grContext = GRContext.CreateMetal(_mtlBackend)
            ?? throw new InvalidOperationException("GRContext.CreateMetal returned null. Is SkiaSharp built with Metal support?" );
        
        _grContext.SetResourceCacheLimit(512 * 1024 * 1024); // 512 MB
    }

    protected override void BeforeRender()
    {
        // Drain autoreleased Objective-C objects every frame (important in a tight SDL render loop).
        // Equivalent to: pool = [[NSAutoreleasePool alloc] init];
        _autoreleasePool = ObjC.Send_IntPtr(ObjC.GetClass("NSAutoreleasePool"), ObjC.Sel("new"));
    }

    protected override SKSurface CreateSurface()
    {
        _currentDrawable = ObjC.Send_IntPtr(_metalLayer, ObjC.Sel("nextDrawable"));
        if (_currentDrawable == IntPtr.Zero)
            throw new InvalidOperationException("CAMetalLayer.nextDrawable returned null.");

        ObjC.SendVoid(_currentDrawable, ObjC.Sel("retain"));

        var texture = ObjC.Send_IntPtr(_currentDrawable, ObjC.Sel("texture"));
        if (texture == IntPtr.Zero)
            throw new InvalidOperationException("Drawable.texture returned null.");

        var mtlInfo = new GRMtlTextureInfo(texture);

        _currentRenderTarget?.Dispose();
        _currentRenderTarget = new GRBackendRenderTarget(WindowWidth, WindowHeight, mtlInfo);

        // Metal surfaces are TopLeft in most integrations.
        // Color type should match the CAMetalLayer pixel format (BGRA8Unorm).
        return SKSurface.Create(_grContext, _currentRenderTarget, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888)
            ?? throw new InvalidOperationException("SKSurface.Create returned null for Metal render target.");
    }

    protected override void AfterRender(SKSurface surface)
    {
        _grContext.Flush();
        _grContext.Submit();

        // Present
        if (_currentDrawable != IntPtr.Zero)
        {
            var commandBuffer = ObjC.Send_IntPtr(_queue, ObjC.Sel("commandBuffer"));
            if (commandBuffer != IntPtr.Zero)
            {
                // commandBuffer is autoreleased; retain to ensure it survives until after commit.
                ObjC.SendVoid(commandBuffer, ObjC.Sel("retain"));

                ObjC.SendVoid_IntPtr(commandBuffer, ObjC.Sel("presentDrawable:"), _currentDrawable);
                ObjC.SendVoid(commandBuffer, ObjC.Sel("commit"));
                ObjC.SendVoid(commandBuffer, ObjC.Sel("release"));
            }

            ObjC.SendVoid(_currentDrawable, ObjC.Sel("release"));
            _currentDrawable = IntPtr.Zero;
        }

        _currentRenderTarget?.Dispose();
        _currentRenderTarget = null;

        if (_autoreleasePool != IntPtr.Zero)
        {
            ObjC.SendVoid(_autoreleasePool, ObjC.Sel("drain"));
            _autoreleasePool = IntPtr.Zero;
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        _grContext.Dispose();
        _mtlBackend.Dispose();

        if (_metalView != IntPtr.Zero)
            MetalDestroyView(_metalView);

        if (_queue != IntPtr.Zero)
            ObjC.SendVoid(_queue, ObjC.Sel("release"));
    }

    private static class MetalNative
    {
        [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
        public static extern IntPtr MTLCreateSystemDefaultDevice();
    }

    private static class ObjC
    {
        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_UInt64(IntPtr receiver, IntPtr selector, ulong arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_Bool(IntPtr receiver, IntPtr selector, bool arg1);

        public static IntPtr Sel(string name) 
            => sel_registerName(name);

        public static IntPtr GetClass(string name)
            => objc_getClass(name);

        public static IntPtr Send_IntPtr(IntPtr receiver, IntPtr selector)
            => objc_msgSend_IntPtr(receiver, selector);

        public static IntPtr Send_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1)
            => objc_msgSend_IntPtr_IntPtr(receiver, selector, arg1);

        public static void SendVoid(IntPtr receiver, IntPtr selector)
            => objc_msgSend_Void(receiver, selector);

        public static void SendVoid_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1)
            => objc_msgSend_Void_IntPtr(receiver, selector, arg1);

        public static void SendVoid_UInt64(IntPtr receiver, IntPtr selector, ulong arg1)
            => objc_msgSend_Void_UInt64(receiver, selector, arg1);

        public static void SendVoid_Bool(IntPtr receiver, IntPtr selector, bool arg1)
            => objc_msgSend_Void_Bool(receiver, selector, arg1);
    }
}