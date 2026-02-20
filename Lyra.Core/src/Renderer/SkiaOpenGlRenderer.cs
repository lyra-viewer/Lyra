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

    public SkiaOpenGlRenderer(IntPtr window, PixelSize drawableSize, IDropStatusProvider dropStatusProvider)
        : base(drawableSize, dropStatusProvider)
    {
        _window = window;

        GLSetAttribute(GLAttr.ContextMajorVersion, 3);
        GLSetAttribute(GLAttr.ContextMinorVersion, 2);
        GLSetAttribute(GLAttr.ContextProfileMask, (int)GLProfile.Core);

        _glContext = GLCreateContext(window);
        GLMakeCurrent(window, _glContext);
        GLSetSwapInterval(1);

        var glInterface = GRGlInterface.Create();
        _grContext = GRContext.CreateGl(glInterface);
    }

    protected override void BeforeRender()
    {
        // Defensive: ensure GL context is current on the calling thread
        GLMakeCurrent(_window, _glContext);
    }

    protected override SKSurface CreateSurface()
    {
        // If MSAA ever introduced, this will need samples/stencil updates
        var fbInfo = new GRGlFramebufferInfo(0, 0x8058); // GL_RGBA8
        var renderTarget = new GRBackendRenderTarget(WindowWidth, WindowHeight, 0, 8, fbInfo);

        return SKSurface.Create(_grContext, renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
    }

    public override void Dispose()
    {
        base.Dispose();

        _grContext.Dispose();

        if (_glContext != IntPtr.Zero)
            GLDestroyContext(_glContext);
    }
}