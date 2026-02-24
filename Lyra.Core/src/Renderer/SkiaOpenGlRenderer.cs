using Lyra.Common;
using Lyra.DropStatusProvider;
using Lyra.SdlCore;
using SkiaSharp;
using static SDL3.SDL;

namespace Lyra.Renderer;

public sealed class SkiaOpenGlRenderer : SkiaRendererBase
{
    private readonly IntPtr _window;
    private readonly IntPtr _glContext;
    private readonly GRContext _grContext;

    private SKSurface? _surface;
    private GRBackendRenderTarget? _renderTarget;
    private bool _surfaceDirty = true;

    public SkiaOpenGlRenderer(IntPtr window, PixelSize drawableSize, IDropProgressProvider dropProgressProvider)
        : base(drawableSize, dropProgressProvider, "OpenGL")
    {
        _window = window;

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

        _surface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
                   ?? throw new InvalidOperationException("SKSurface.Create returned null for OpenGL surface.");
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