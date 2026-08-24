using Lyra.Common;
using Lyra.Common.Settings;
using Lyra.DropStatusProvider;
using Lyra.DuplicateStatusProvider;
using Lyra.SdlCore;
using Lyra.SystemUtils.MacInterop;
using SkiaSharp;
using static SDL3.SDL;

using Lyra.Renderer.Display;
using Lyra.Renderer.Drawing;

namespace Lyra.Renderer.Backends;

public sealed class SkiaMetalRenderer : SkiaRendererBase
{
    // SDL3 metal glue
    private readonly IntPtr _metalView; // SDL_MetalView (typedef void*)
    private readonly IntPtr _metalLayer; // CAMetalLayer*

    // Metal objects
    private readonly IntPtr _device; // id<MTLDevice>
    private readonly IntPtr _queue; // id<MTLCommandQueue>

    private readonly GRMtlBackendContext _mtlBackend;
    private readonly GRContext _grContext;

    // Per-frame
    private IntPtr _currentDrawable; // id<CAMetalDrawable>

    private GRBackendRenderTarget? _currentRenderTarget;
    private IntPtr _autoreleasePool; // NSAutoreleasePool*

    // Stable MTLPixelFormat values.
    private const ulong MTLPixelFormatBGRA8Unorm = 80;
    private const ulong MTLPixelFormatRGBA16Float = 115;

    /// <summary>Whether the layer was successfully configured for extended range.</summary>
    private readonly bool _extendedRange;
    
    private static readonly IntPtr SelNextDrawable = ObjC.Sel("nextDrawable");
    private static readonly IntPtr SelTexture = ObjC.Sel("texture");
    private static readonly IntPtr SelRetain = ObjC.Sel("retain");
    private static readonly IntPtr SelRelease = ObjC.Sel("release");
    private static readonly IntPtr SelCommandBuffer = ObjC.Sel("commandBuffer");
    private static readonly IntPtr SelPresentDrawable = ObjC.Sel("presentDrawable:");
    private static readonly IntPtr SelCommit = ObjC.Sel("commit");
    private static readonly IntPtr SelNew = ObjC.Sel("new");
    private static readonly IntPtr SelDrain = ObjC.Sel("drain");
    private static readonly IntPtr ClassAutoreleasePool = ObjC.Class("NSAutoreleasePool");

    private static readonly SKColorSpace DisplayP3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

    /// <summary>
    /// The extended-range surface's space: the same primaries, no transfer function. The tone-map
    /// shader reads the transfer off this space and finds the identity, so one code path serves
    /// both surface kinds.
    /// </summary>
    private static readonly SKColorSpace ExtendedLinearDisplayP3 =
        SKColorSpace.CreateRgb(new SKColorSpaceTransferFn(1f, 1f, 0f, 0f, 0f, 0f, 0f), SKColorSpaceXyz.DisplayP3);

    public SkiaMetalRenderer(IntPtr window, PixelSize drawableSize, IDropProgressProvider dropProgressProvider, ViewState viewState, IScanProgressProvider scanProgressProvider)
        : base(drawableSize, dropProgressProvider, viewState, "Metal", scanProgressProvider)
    {
        _metalView = MetalCreateView(window);
        if (_metalView == IntPtr.Zero)
            throw new InvalidOperationException("SDL_Metal_CreateView failed.");

        _metalLayer = MetalGetLayer(_metalView);
        if (_metalLayer == IntPtr.Zero)
            throw new InvalidOperationException("SDL_Metal_GetLayer returned null CAMetalLayer.");

        _device = Native.CreateMetalDevice(out var deviceSource);
        if (_device == IntPtr.Zero)
            throw new InvalidOperationException("No Metal device available (MTLCreateSystemDefaultDevice and MTLCopyAllDevices both came up empty).");

        Logger.Debug($"[SkiaMetalRenderer] Metal device obtained via {deviceSource}.");

        // layer.device = device
        ObjC.SendVoid(_metalLayer, "setDevice:", _device);
        // layer.framebufferOnly = NO
        ObjC.SendVoid(_metalLayer, "setFramebufferOnly:", false);
        // enable vsync
        ObjC.SendVoid(_metalLayer, "setDisplaySyncEnabled:", true);

        _extendedRange = TryConfigureExtendedRange();

        // queue = [device newCommandQueue]
        _queue = ObjC.Send(_device, "newCommandQueue");
        if (_queue == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create MTLCommandQueue (newCommandQueue).");

        _mtlBackend = new GRMtlBackendContext
        {
            DeviceHandle = _device,
            QueueHandle = _queue,
        };

        _grContext = GRContext.CreateMetal(_mtlBackend)
                     ?? throw new InvalidOperationException("GRContext.CreateMetal returned null. Is SkiaSharp built with Metal support?");

        ConfigureResourceCache(_grContext, "SkiaMetalRenderer");
    }

    /// <summary>
    /// Metal is the one macOS backend that can carry extended range: a CAMetalLayer takes an
    /// RGBA16Float pixel format and an extended color space, proven end to end by `LyraEdrProbe`.
    /// </summary>
    protected override bool BackendSupportsExtendedRange => true;

    /// <summary>
    /// An RGBA16Float layer in extended linear Display-P3, carrying the headroom the panel grants.
    /// </summary>
    protected override SurfaceProfile Surface => _extendedRange
        ? SurfaceProfile.Extended(ExtendedLinearDisplayP3, HeadroomPolicy.Spendable(Displays.Current))
        : SurfaceProfile.DisplayReferred(DisplayP3);

    /// <summary>
    /// Puts the layer into extended range: half-float pixels, an extended linear color space, and
    /// the flag telling the compositor to honor values above SDR white.
    /// </summary>
    private bool TryConfigureExtendedRange()
    {
        var extendedSpace = Native.CreateColorSpace("kCGColorSpaceExtendedLinearDisplayP3");
        if (extendedSpace != IntPtr.Zero && ObjC.Responds(_metalLayer, "setWantsExtendedDynamicRangeContent:"))
        {
            ObjC.SendVoid(_metalLayer, "setPixelFormat:", MTLPixelFormatRGBA16Float);
            ObjC.SendVoid(_metalLayer, "setWantsExtendedDynamicRangeContent:", true);
            ObjC.SendVoid(_metalLayer, "setColorspace:", extendedSpace);
            Native.CGColorSpaceRelease(extendedSpace); // the layer retains it

            Logger.Info("[SkiaMetalRenderer] Layer configured for extended range (RGBA16Float, extended linear Display-P3).");
            return true;
        }

        if (extendedSpace != IntPtr.Zero)
            Native.CGColorSpaceRelease(extendedSpace);

        ObjC.SendVoid(_metalLayer, "setPixelFormat:", MTLPixelFormatBGRA8Unorm);

        var p3ColorSpace = Native.CreateColorSpace("kCGColorSpaceDisplayP3");
        if (p3ColorSpace != IntPtr.Zero)
        {
            ObjC.SendVoid(_metalLayer, "setColorspace:", p3ColorSpace);
            Native.CGColorSpaceRelease(p3ColorSpace);
        }

        Logger.Warning("[SkiaMetalRenderer] Extended range unavailable on this system; the layer stays BGRA8 in Display-P3.");
        return false;
    }

    protected override void BeforeRender()
    {
        // Drain autoreleased Objective-C objects every frame (important in a tight SDL render loop).
        _autoreleasePool = ObjC.Send(ClassAutoreleasePool, SelNew);
    }

    protected override SKSurface CreateSurface()
    {
        _currentDrawable = ObjC.Send(_metalLayer, SelNextDrawable);
        if (_currentDrawable == IntPtr.Zero)
            throw new InvalidOperationException("CAMetalLayer.nextDrawable returned null.");

        ObjC.SendVoid(_currentDrawable, SelRetain);

        var texture = ObjC.Send(_currentDrawable, SelTexture);
        if (texture == IntPtr.Zero)
            throw new InvalidOperationException("Drawable.texture returned null.");

        var mtlInfo = new GRMtlTextureInfo(texture);

        _currentRenderTarget?.Dispose();
        _currentRenderTarget = new GRBackendRenderTarget(WindowWidth, WindowHeight, mtlInfo);
        
        var colorType = _extendedRange ? SKColorType.RgbaF16 : SKColorType.Bgra8888;
        var colorSpace = _extendedRange ? ExtendedLinearDisplayP3 : DisplayP3;

        return SKSurface.Create(_grContext, _currentRenderTarget, GRSurfaceOrigin.TopLeft, colorType, colorSpace)
               ?? throw new InvalidOperationException("SKSurface.Create returned null for Metal render target.");
    }

    protected override void AfterRender(SKSurface surface)
    {
        surface.Flush();
        _grContext.Submit();

        // Present
        if (_currentDrawable != IntPtr.Zero)
        {
            var commandBuffer = ObjC.Send(_queue, SelCommandBuffer);
            if (commandBuffer != IntPtr.Zero)
            {
                // commandBuffer is autoreleased; retain to ensure it survives until after commit.
                ObjC.SendVoid(commandBuffer, SelRetain);

                ObjC.SendVoid(commandBuffer, SelPresentDrawable, _currentDrawable);
                ObjC.SendVoid(commandBuffer, SelCommit);
                ObjC.SendVoid(commandBuffer, SelRelease);
            }

            ObjC.SendVoid(_currentDrawable, SelRelease);
            _currentDrawable = IntPtr.Zero;
        }

        _currentRenderTarget?.Dispose();
        _currentRenderTarget = null;

        if (_autoreleasePool != IntPtr.Zero)
        {
            ObjC.SendVoid(_autoreleasePool, SelDrain);
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
            ObjC.SendVoid(_queue, SelRelease);
    }
}