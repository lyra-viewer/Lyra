using Lyra.SystemUtils;
using Viewer = Lyra.SdlCore.SdlCore;
using Xunit;

namespace Lyra.Core.Tests.Startup;

/// <summary>
/// What a failure that stops the application tells the person in front of it. A release build logs
/// to file and nothing else, so before this the whole report was a file nobody had been told about
/// and the application simply appeared not to start - these cover the text that now reaches them
/// instead, on stderr and in the dialog.
/// </summary>
public class FatalErrorTests
{
    [Fact]
    public void Describe_NamesTheExceptionAndWhatItSaid()
    {
        var described = FatalError.Describe(new InvalidOperationException("The renderer refused."));

        Assert.Equal("InvalidOperationException: The renderer refused.", described);
    }

    [Fact]
    public void Describe_AddsTheInnermostCause_WhichIsUsuallyTheOneThatNamesTheProblem()
    {
        var cause = new TypeInitializationException("Lyra.NativeLibraryLoader", new DllNotFoundException("Unable to load shared library 'libSkiaSharp'."));
        
        var described = FatalError.Describe(cause);

        Assert.Contains("TypeInitializationException", described);
        Assert.Contains("DllNotFoundException: Unable to load shared library 'libSkiaSharp'.", described);
    }

    [Fact]
    public void Describe_DoesNotRepeatItselfWhenTheCauseIsAlsoTheInnermost()
    {
        var described = FatalError.Describe(new DllNotFoundException("libSDL3 is missing."));

        Assert.Equal("DllNotFoundException: libSDL3 is missing.", described);
        Assert.DoesNotContain("\n", described);
    }

    [Fact]
    public void RendererFailure_QuotesWhatEachBackendSaid()
    {
        var message = Viewer.DescribeRendererFailure(
        [
            "Metal: SDL_CreateWindow failed: Metal support is not available",
            "OpenGL: Could not assemble an OpenGL function interface for the current GL context."
        ]);
        
        Assert.Contains("Metal: SDL_CreateWindow failed: Metal support is not available", message);
        Assert.Contains("OpenGL: Could not assemble an OpenGL function interface", message);
        Assert.Contains("OpenGL 3.2", message);
    }

    [Fact]
    public void RendererFailure_StillSaysSomethingWhenNoBackendWasEvenTried()
    {
        var message = Viewer.DescribeRendererFailure([]);

        Assert.Contains("No backend was available", message);
        Assert.Contains("OpenGL 3.2", message);
    }
}
