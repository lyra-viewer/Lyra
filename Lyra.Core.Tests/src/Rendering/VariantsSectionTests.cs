using Lyra.FileLoader.Navigation;
using Lyra.Imaging.Content;
using Lyra.Renderer.GUI;
using Lyra.Renderer.GUI.Sections;
using Lyra.UI.Components;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

public class VariantsSectionTests
{
    [Fact]
    public void Hidden_WhenCompositeHasNoVariants()
    {
        using var section = new VariantsSection();

        section.Refresh(StateFor(null));

        Assert.False(section.Collapsible.Present);
    }

    [Fact]
    public void Shown_WithPlainTitle_WhenVariantsExist()
    {
        using var section = new VariantsSection();

        section.Refresh(StateFor(CompositeWith(("512 x 512", 512), ("256 x 256", 256))));

        Assert.True(section.Collapsible.Present);
        Assert.Equal("SIZES", section.Collapsible.Title);
    }

    [Fact]
    public void HidesAgain_WhenNavigatingToAFileWithoutVariants()
    {
        using var section = new VariantsSection();

        section.Refresh(StateFor(CompositeWith(("512 x 512", 512))));
        Assert.True(section.Collapsible.Present);

        section.Refresh(StateFor(null));
        Assert.False(section.Collapsible.Present);
    }

    [Fact]
    public void ReportsIndexOfSameSizedVariants_Distinctly()
    {
        using var section = new VariantsSection();

        // Two entries that decode to identical dimensions - the ic11 / icp5 case.
        var composite = CompositeWith(("32 x 32 @2x", 32), ("32 x 32", 32));
        section.Refresh(StateFor(composite));

        var reported = new List<int>();
        section.VariantSelected += reported.Add;

        section.Select(Set(composite).Variants[1]);
        section.Select(Set(composite).Variants[0]);

        Assert.Equal([1, 0], reported);
    }

    [Fact]
    public void EveryRowContent_IsTransient_SoTheWholeRowIsClickable()
    {
        var row = VariantsSection.RenderRow(new ImageVariant("512 x 512", 512, 512, "ic09 - PNG", 52_000), isPicked: false);
        var offenders = Descendants(row).Where(c => !c.Transient).ToList();

        Assert.True(offenders.Count == 0, "these row contents would swallow the click: " + string.Join(", ", offenders.Select(c => c.GetType().Name)));
    }

    /// <summary>Every component beneath <paramref name="root"/>, excluding the row itself.</summary>
    private static IEnumerable<IComponent> Descendants(IComponent root)
    {
        if (root is not IContainer container)
            yield break;

        foreach (var child in container.Children)
        {
            yield return child;

            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    // ------------------------------------------------------------------

    private static VariantRasterContent Set(Composite composite) => Assert.IsType<VariantRasterContent>(composite.Content);

    private static Composite CompositeWith(params (string Label, int Size)[] entries)
    {
        var composite = new Composite(new FileInfo("fake.icns"));

        var variants = entries.Select(e => new ImageVariant(e.Label, e.Size, e.Size, "test - PNG", e.Size * 4L)).ToList();
        var contents = entries.Select(ICompositeContent (e) => new StubContent(e.Size)).ToList();

        composite.Content = new VariantRasterContent(variants, contents, active: 0);
        return composite;
    }

    private static UIState StateFor(Composite? composite) =>
        new(composite, composite?.State ?? CompositeState.Disposed, default, default, default(DirectoryNavigator.Navigation), null);

    private sealed class StubContent(int size) : ICompositeContent
    {
        public bool IsResolutionIndependent => false;
        public float? DecodedWidth => size;
        public float? DecodedHeight => size;
        public void Dispose() { }
    }
}