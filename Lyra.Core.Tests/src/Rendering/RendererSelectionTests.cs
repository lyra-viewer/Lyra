using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.Renderer.Backends;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// Which backends startup tries, and in what order.
///
/// The ordering carries a real constraint rather than a preference: Metal is the only macOS
/// backend that can carry an extended-range surface, so "auto" on macOS must reach for it
/// first or EDR is silently unreachable. Equally, no configuration may resolve to an empty
/// list - that would leave the app with nothing to start.
/// </summary>
public class RendererSelectionTests
{
    [Fact]
    public void Auto_PrefersMetal_OnMacOS()
    {
        var candidates = RendererSelection.ResolveCandidates(Backend.Auto, isMacOs: true);

        Assert.Equal([Backend.Metal, Backend.OpenGL], candidates);
    }

    [Fact]
    public void Auto_UsesOpenGl_Elsewhere()
    {
        var candidates = RendererSelection.ResolveCandidates(Backend.Auto, isMacOs: false);

        Assert.Equal([Backend.OpenGL], candidates);
    }

    [Fact]
    public void ExplicitMetal_IsHonoured_OnMacOS()
    {
        var candidates = RendererSelection.ResolveCandidates(Backend.Metal, isMacOs: true);

        Assert.Equal(Backend.Metal, candidates[0]);
    }

    [Fact]
    public void ExplicitMetal_StillKeepsOpenGlAsLastResort()
    {
        var candidates = RendererSelection.ResolveCandidates(Backend.Metal, isMacOs: true);

        Assert.Equal(Backend.OpenGL, candidates[^1]);
    }

    [Fact]
    public void ExplicitMetal_IsDropped_WhenNotOnMacOS()
    {
        var candidates = RendererSelection.ResolveCandidates(Backend.Metal, isMacOs: false);

        Assert.DoesNotContain(Backend.Metal, candidates);
        Assert.Equal([Backend.OpenGL], candidates);
    }

    [Fact]
    public void ExplicitOpenGl_TriesOnlyOpenGl()
    {
        Assert.Equal([Backend.OpenGL], RendererSelection.ResolveCandidates(Backend.OpenGL, isMacOs: true));
        Assert.Equal([Backend.OpenGL], RendererSelection.ResolveCandidates(Backend.OpenGL, isMacOs: false));
    }
    
    public static TheoryData<Backend, bool> EveryConfiguredBackend()
    {
        var data = new TheoryData<Backend, bool>();

        foreach (var backend in Enum.GetValues<Backend>())
        foreach (var isMacOs in new[] { true, false })
            data.Add(backend, isMacOs);

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryConfiguredBackend))]
    public void AlwaysOffersSomethingToTry(Backend configured, bool isMacOs)
    {
        var candidates = RendererSelection.ResolveCandidates(configured, isMacOs);

        Assert.NotEmpty(candidates);
        Assert.Equal(Backend.OpenGL, candidates[^1]);
        Assert.DoesNotContain(candidates, candidate => candidate.HasAttribute<DisabledBackendAttribute>());
    }
    
    [Fact]
    public void TheLastResortBackendIsNotDisabled()
    {
        Assert.False(Backend.OpenGL.HasAttribute<DisabledBackendAttribute>());
    }
    
    [Theory]
    [InlineData(Backend.Vulkan)]
    [InlineData(Backend.DirectX)]
    public void ABackendSettingsNamesButLyraCannotBuildYet_StartsOpenGlAndReportsItself(Backend declared)
    {
        foreach (var isMacOs in new[] { true, false })
        {
            var candidates = RendererSelection.ResolveCandidates(declared, isMacOs);

            Assert.Equal([Backend.OpenGL], candidates);
            Assert.True(RendererSelection.IsUnavailable(declared, candidates));
        }
    }
    
    [Fact]
    public void ExplainsWhyABackendWillNotBeUsed_PreferringWhatTheAttributeSays()
    {
        var onLinux = RendererSelection.ResolveCandidates(Backend.Metal, isMacOs: false);
        Assert.Equal("The metal backend is not available on this platform.", RendererSelection.DescribeUnavailable(Backend.Metal, onLinux));

        var vulkan = RendererSelection.ResolveCandidates(Backend.Vulkan, isMacOs: true);
        Assert.Equal("The vulkan backend is not implemented yet.", RendererSelection.DescribeUnavailable(Backend.Vulkan, vulkan));
    }

    [Fact]
    public void ExplainsNothing_WhenTheChoiceIsHonouredOrNoChoiceWasMade()
    {
        var onMac = RendererSelection.ResolveCandidates(Backend.Metal, isMacOs: true);
        Assert.Null(RendererSelection.DescribeUnavailable(Backend.Metal, onMac));

        var auto = RendererSelection.ResolveCandidates(Backend.Auto, isMacOs: false);
        Assert.Null(RendererSelection.DescribeUnavailable(Backend.Auto, auto));
    }

    [Fact]
    public void ReportsUnavailable_OnlyForAnExplicitChoiceThatCannotBeTried()
    {
        var onLinux = RendererSelection.ResolveCandidates(Backend.Metal, isMacOs: false);
        Assert.True(RendererSelection.IsUnavailable(Backend.Metal, onLinux));

        var onMac = RendererSelection.ResolveCandidates(Backend.Metal, isMacOs: true);
        Assert.False(RendererSelection.IsUnavailable(Backend.Metal, onMac));

        var auto = RendererSelection.ResolveCandidates(Backend.Auto, isMacOs: false);
        Assert.False(RendererSelection.IsUnavailable(Backend.Auto, auto));
    }
}
