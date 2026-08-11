using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.TreeView;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Layout;

/// <summary>
/// Resolve is the top-down pass that hands surplus space to Flexible children. It only works if
/// every container forwards it, and a container that forgets fails silently: children keep their
/// measured size and nothing reports an error.
///
/// VScrollContainer, Grid, ListView and TreeView all used to drop it. The visible symptom was a
/// Flexible child inside a scroller staying at its content width instead of filling the row.
/// </summary>
public class ResolvePropagationTests
{
    private sealed class Probe : ComponentBase
    {
        public int ResolveCalls { get; private set; }

        protected override SKSize MeasureContent(SKSize availableSize) => new(10, 10);
        protected override void ArrangeContent(SKRect contentBounds) { }
        protected override void RenderContent(SKCanvas canvas, SKRect contentBounds) { }
        protected override void ResolveContent() => ResolveCalls++;
    }

    // --------------------------------------------------------
    //  Every container forwards Resolve
    // --------------------------------------------------------

    public static TheoryData<string> ContainerNames() =>
    [
        "VStack", "HStack", "VScrollContainer", "Grid", "ListView", "TreeView", "Collapsible"
    ];

    [Theory]
    [MemberData(nameof(ContainerNames))]
    public void ContainerForwardsResolveToItsChild(string containerName)
    {
        var probe = new Probe();
        var container = BuildContainer(containerName, probe);

        container.Measure(new SKSize(200, 200));
        container.Resolve();

        Assert.Equal(1, probe.ResolveCalls);
    }

    private static ComponentBase BuildContainer(string name, IComponent child)
    {
        switch (name)
        {
            case "VStack":
            {
                var c = new VStack();
                c.AddComponent(child);
                return c;
            }
            case "HStack":
            {
                var c = new HStack();
                c.AddComponent(child);
                return c;
            }
            case "VScrollContainer":
            {
                var c = new VScrollContainer();
                c.AddComponent(child);
                return c;
            }
            case "Grid":
            {
                var c = new Grid();
                c.AddComponent(child);
                return c;
            }
            case "ListView":
                // Rows come from the factory, so the probe is delivered as the single row.
                return new ListView<int>([0], (_, _) => child);
            case "TreeView":
                return new TreeView<int>([new TreeNode<int>(0)], (_, _) => child);
            case "Collapsible":
            {
                var c = new Collapsible("T") { IsExpanded = true };
                c.AddComponent(child);
                return c;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    // --------------------------------------------------------
    //  The cascade reaches arbitrary depth
    // --------------------------------------------------------
    //  One forwarding container is not enough: a container that
    //  forwards to its own children but is itself never resolved
    //  leaves everything below it unresolved too. These pin the
    //  two nestings the app actually builds.
    // --------------------------------------------------------

    [Fact]
    public void ResolveReachesNestedContainersThroughAScroller()
    {
        // StructureSection's shape: scroller -> collapsible panel -> content.
        var probe = new Probe();

        var panel = new VStack();
        panel.AddComponent(probe);

        var column = new VStack();
        column.AddComponent(panel);

        var scroller = new VScrollContainer();
        scroller.AddComponent(column);

        scroller.Measure(new SKSize(300, 300));
        scroller.Resolve();

        Assert.Equal(1, probe.ResolveCalls);
    }

    [Fact]
    public void ResolveReachesTheChildrenOfAListRow()
    {
        // Every metadata panel builds rows as an HStack of labels; the row is a container
        // in its own right and its Resolve has to run.
        var probe = new Probe();

        var row = new HStack { HorizontalSize = SizeMode.Expand };
        row.AddComponent(probe);

        var list = new ListView<int>([0], (_, _) => row) { HorizontalSize = SizeMode.Expand };

        list.Measure(new SKSize(200, 200));
        list.Resolve();

        Assert.Equal(1, probe.ResolveCalls);
    }

    [Fact]
    public void NonPresentChildrenAreSkipped()
    {
        var probe = new Probe { Present = false };
        var scroller = new VScrollContainer();
        scroller.AddComponent(probe);

        scroller.Measure(new SKSize(200, 200));
        scroller.Resolve();

        Assert.Equal(0, probe.ResolveCalls);
    }
}