using System.Runtime.InteropServices;
using Lyra.Common;
using Lyra.Common.Settings;
using Lyra.DropStatusProvider;
using Lyra.DuplicateStatusProvider;
using Lyra.SdlCore;
using SkiaSharp;
using static SDL3.SDL;

using Lyra.Renderer.Drawing;

namespace Lyra.Renderer.Backends;

public sealed class SkiaOpenGlRenderer : SkiaRendererBase
{
    private readonly IntPtr _window;
    private readonly IntPtr _glContext;
    private readonly GRContext _grContext;

    private SKSurface? _surface;
    private GRBackendRenderTarget? _renderTarget;
    private bool _surfaceDirty = true;

    private readonly SKColorSpace _surfaceColorSpace;

    public SkiaOpenGlRenderer(IntPtr window, PixelSize drawableSize, IDropProgressProvider dropProgressProvider, ViewState viewState, IScanProgressProvider scanProgressProvider)
        : base(drawableSize, dropProgressProvider, viewState, "OpenGL", scanProgressProvider)
    {
        _window = window;
        _surfaceColorSpace = DetermineSurfaceColorSpace(window);

        GLSetAttribute(GLAttr.ContextMajorVersion, 3);
        GLSetAttribute(GLAttr.ContextMinorVersion, 2);
        GLSetAttribute(GLAttr.ContextProfileMask, (int)GLProfile.Core);

        _glContext = GLCreateContext(window);
        GLMakeCurrent(window, _glContext);

        if (!GLSetSwapInterval(-1))
            GLSetSwapInterval(1);

        GLGetSwapInterval(out var swapInterval);
        Logger.Debug($"[SkiaOpenGlRenderer] GL swap interval = {swapInterval}");

        var glInterface = GRGlInterface.Create();
        _grContext = GRContext.CreateGl(glInterface);

        ConfigureResourceCache(_grContext, "SkiaOpenGlRenderer");
    }

    protected override bool DisposeSurfaceAfterRender => false;

    protected override void BeforeRender()
    {
        // Defensive: ensure GL context is current on the calling thread
        GLMakeCurrent(_window, _glContext);
    }

    protected override void OnDrawableSizeChangedInternal(int width, int height, float scale)
    {
        _surfaceDirty = true;
    }

    /// <summary>
    /// No extended range, on any platform. macOS OpenGL has no path to it and is deprecated;
    /// WGL on Windows reaches neither an HDR color space nor 10-bit; Linux would need the
    /// compositor's color management, which is Vulkan's route rather than GL's.
    /// </summary>
    protected override bool BackendSupportsExtendedRange => false;

    /// 8-bit, and no extended-range path exists in GL on any platform Lyra runs on.
    protected override SurfaceProfile Surface => SurfaceProfile.DisplayReferred(_surfaceColorSpace);

    protected override SKSurface CreateSurface()
    {
        if (_surfaceDirty || _surface == null || _renderTarget == null)
        {
            RecreateSurface();
        }

        return _surface!;
    }

    private void RecreateSurface()
    {
        _surfaceDirty = false;

        _surface?.Dispose();
        _surface = null;

        _renderTarget?.Dispose();
        _renderTarget = null;

        var fbInfo = new GRGlFramebufferInfo(0, 0x8058); // GL_RGBA8

        // If MSAA ever introduced, this will need samples/stencil updates
        _renderTarget = new GRBackendRenderTarget(WindowWidth, WindowHeight, 0, 8, fbInfo);

        _surface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888, _surfaceColorSpace)
                   ?? throw new InvalidOperationException("SKSurface.Create returned null for OpenGL surface.");
    }
    
    private static SKColorSpace DetermineSurfaceColorSpace(IntPtr window)
    {
        if (OperatingSystem.IsMacOS())
            return SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

        var displayColorSpace = TryGetDisplayIccColorSpace(window);
        if (displayColorSpace is not null)
        {
            Logger.Debug("[SkiaOpenGlRenderer] Render surface tagged with the display's ICC profile (wide-gamut content preserved).");
            return displayColorSpace;
        }

        Logger.Debug("[SkiaOpenGlRenderer] No display ICC profile published; render surface falls back to sRGB (wide-gamut content folds to sRGB).");
        return SKColorSpace.CreateSrgb();
    }

    // Reads the raw ICC profile for the window's display via SDL and builds an SKColorSpace
    // from it. Returns null when SDL publishes no profile or the bytes don't parse.
    private static SKColorSpace? TryGetDisplayIccColorSpace(IntPtr window)
    {
        var profilePtr = GetWindowICCProfile(window, out var sizeBytes);
        if (profilePtr == IntPtr.Zero)
            return null;

        try
        {
            var size = (int)sizeBytes;
            if (size <= 0)
                return null;

            var icc = new byte[size];
            Marshal.Copy(profilePtr, icc, 0, size);

            return SKColorSpace.CreateIcc(icc);
        }
        catch (Exception ex)
        {
            Logger.Warning($"[SkiaOpenGlRenderer] Could not build a color space from the display ICC profile: {ex.Message}");
            return null;
        }
        finally
        {
            Free(profilePtr);
        }
    }

    protected override void AfterRender(SKSurface surface)
    {
        surface.Flush();
        _grContext.Submit();
    }

    public override void Dispose()
    {
        base.Dispose();

        _surface?.Dispose();
        _renderTarget?.Dispose();

        _grContext.Dispose();

        if (_glContext != IntPtr.Zero)
            GLDestroyContext(_glContext);
    }
}