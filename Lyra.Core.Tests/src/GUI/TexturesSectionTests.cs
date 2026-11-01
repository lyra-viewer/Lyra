using Lyra.GUI.Sections;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Content.Variants;
using Lyra.UI.Components.Controls.TreeView;
using Xunit;

namespace Lyra.Core.Tests.GUI;

public class TexturesSectionTests
{
    private sealed class FakeContent : ICompositeContent
    {
        public bool IsResolutionIndependent => false;
        public float? DecodedWidth => 4;
        public float? DecodedHeight => 4;
        public void Dispose() { }
    }

    private static readonly TextureSource Embedded = new(TextureLocation.Embedded, null, null);

    private static ImageVariant Variant(string label) => new(label, 4, 4, "4 x 4 PNG", 64);

    /// <summary>Body (albedo, normal, rough missing, albedo again as emissive), Glass (one slot), an empty material, one stray image.</summary>
    private static TextureLayout Layout() => new(
        [
            new TextureMaterial("Body", [
                new TextureSlot("Base Color", 0, [4]),
                new TextureSlot("Normal", 1, []),
                new TextureSlot("Roughness", 2, []),
                new TextureSlot("Emissive", 0, [])
            ]),
            new TextureMaterial("Glass", [new TextureSlot("Opacity", 3, [])]),
            new TextureMaterial("Empty", [])
        ],
        [5],
        [
            Embedded,
            new TextureSource(TextureLocation.Linked, "maps/normal.png", "/models/maps/normal.png"),
            new TextureSource(TextureLocation.Missing, @"D:\art\body\rough.png", null),
            new TextureSource(TextureLocation.FoundByName, @"C:\old\glass.png", "/models/textures/glass.png"),
            Embedded,
            Embedded
        ]);

    private static VariantContainer Set(TextureLayout? layout = null, int active = 0)
    {
        string[] names = ["albedo.ktx2", "normal.png", "rough.png", "glass.png", "albedo.png", "thumb"];
        layout ??= Layout();

        return new VariantContainer(
            [.. names.Select(Variant)],
            [.. names.Select(_ => (ICompositeContent)new FakeContent())],
            active)
        {
            Kind = VariantKind.Textures,
            Textures = layout,
            StepOrder = layout.ShowableInListOrder()
        };
    }

    private static TreeNode<TextureRow> Leaf(IEnumerable<TreeNode<TextureRow>> roots, string title) => roots.SelectMany(root => root.Children.Prepend(root)).First(node => node.Data.Title == title);

    [Fact]
    public void EmptyMaterialsAreDroppedAndSingleSlotOnesFolded()
    {
        var set = Set();
        var roots = TextureTreeBuilder.Build(set.Textures!, set.Variants);

        Assert.Equal(["Body", "Glass – Opacity", "Unreferenced – thumb"], roots.Select(root => root.Data.Title));

        var body = roots[0];
        // Five leaves, four textures: the emissive slot reuses the albedo.
        Assert.Equal(4, body.Data.Count);
        Assert.Null(body.Data.Texture);
        Assert.Equal(["Base Color", "Base Color · alternate", "Normal", "Roughness", "Emissive"], body.Children.Select(child => child.Data.Title));
        Assert.Equal([0, 4, 1, 2, 0], body.Children.Select(child => child.Data.Texture ?? -1));

        Assert.Equal(3, roots[1].Data.Texture);
        Assert.True(roots[1].IsLeaf);
    }

    [Fact]
    public void GroupsStartOpenOnlyWhileTheTreeIsSmall()
    {
        var set = Set();
        Assert.True(TextureTreeBuilder.Build(set.Textures!, set.Variants)[0].IsExpanded);

        var manySlots = Enumerable.Range(0, TextureTreeBuilder.ExpandAllUpTo + 1).Select(i => new TextureSlot($"Slot {i}", 0, [])).ToList();
        var large = new TextureLayout([new TextureMaterial("Big", manySlots)], [], [Embedded]);

        Assert.False(TextureTreeBuilder.Build(large, [Variant("one")])[0].IsExpanded);
    }

    [Fact]
    public void TheTexturesSectionTakesOverFromTheVariantList()
    {
        using var textures = new TexturesSection();
        using var variants = new VariantsSection();
        var set = Set();

        textures.Refresh(set);
        variants.Refresh(set);

        Assert.True(textures.Collapsible.Present);
        Assert.False(variants.Collapsible.Present);

        var pages = new VariantContainer([Variant("page")], [new FakeContent()], 0);
        textures.Refresh(pages);

        Assert.False(textures.Collapsible.Present);
    }

    [Fact]
    public void MissingTexturesAndGroupsCannotBePicked()
    {
        using var section = new TexturesSection();
        section.Refresh(Set());

        var picked = new List<int>();
        section.VariantSelected += picked.Add;

        Assert.False(section.CanPick(Leaf(section.Roots, "Roughness")));
        Assert.False(section.CanPick(Leaf(section.Roots, "Body")));
        Assert.True(section.CanPick(Leaf(section.Roots, "Glass – Opacity")));

        section.Pick(Leaf(section.Roots, "Roughness"));
        Assert.Empty(picked);

        section.Pick(Leaf(section.Roots, "Normal"));
        Assert.Equal([1], picked);
    }

    [Fact]
    public void ALinkedTextureShowsOneLine_OneFoundByNameAlsoItsStoredPath()
    {
        using var section = new TexturesSection();
        section.Refresh(Set(), "/models");

        Assert.Equal("", section.PathText);

        section.Pick(Leaf(section.Roots, "Normal"));

        Assert.Equal($"Linked   {Path.Combine("maps", "normal.png")}", section.PathText);
        Assert.Equal("", section.FoundPathText);

        section.Pick(Leaf(section.Roots, "Glass – Opacity"));

        Assert.Equal(@"Stored   C:\old\glass.png", section.PathText);
        Assert.Equal($"Found    {Path.Combine("textures", "glass.png")}", section.FoundPathText);
    }

    [Theory]
    [InlineData("textures/albedo.png", "textures/albedo.png")]
    [InlineData("../textures/albedo.png", "../textures/albedo.png")]
    [InlineData("../../shared/albedo.png", null)]
    public void ALinkedFileIsRelativeToTheModelUpToOneLevelUp(string fromModel, string? shown)
    {
        var models = Path.Combine(Path.GetTempPath(), "lyra", "assets", "models");
        var file = Path.GetFullPath(Path.Combine(models, fromModel));

        var expected = shown?.Replace('/', Path.DirectorySeparatorChar) ?? file;
        Assert.Equal(expected, TexturesSection.Shown(file, models));
    }

    [Fact]
    public void WithoutTheModelsFolder_TheFileIsShownInFull() => Assert.Equal("/models/maps/normal.png", TexturesSection.Shown("/models/maps/normal.png", null));

    [Fact]
    public void ThePickFollowsTheTextureOnScreenButStaysOnTheLeafTheUserChose()
    {
        using var section = new TexturesSection();
        var set = Set();

        section.Refresh(set);
        Assert.Equal("Base Color", section.PickedNode?.Data.Title);

        Assert.True(set.Select(1));
        section.Refresh(set);
        Assert.Equal("Normal", section.PickedNode?.Data.Title);

        // Emissive shows the same texture as Base Color: picking it must not snap back to Base Color.
        section.Pick(Leaf(section.Roots, "Emissive"));
        Assert.True(set.Select(0));
        section.Refresh(set);

        Assert.Equal("Emissive", section.PickedNode?.Data.Title);
    }

    [Fact]
    public void TheInfoBarSaysWhereTheKeysAreAmongTheFilesVariants()
    {
        var textures = Set(active: 3);
        Assert.Equal("[Texture 4/5]", InfoSection.VariantNav(textures));

        var pages = new VariantContainer([Variant("1"), Variant("2"), Variant("3")], [new FakeContent(), new FakeContent(), new FakeContent()], 1)
        {
            Kind = VariantKind.Pages
        };
        Assert.Equal("[Page 2/3]", InfoSection.VariantNav(pages));

        Assert.Null(InfoSection.VariantNav(new VariantContainer([Variant("only")], [new FakeContent()], 0)));
        Assert.Null(InfoSection.VariantNav(null));
    }

    [Fact]
    public void MaterialsWithASheetAndTheOverviewCanBePicked()
    {
        var layout = Layout() with { Overview = 7 };
        layout = layout with { Materials = [layout.Materials[0] with { Sheet = 6 }, .. layout.Materials.Skip(1)] };

        string[] names = ["albedo.ktx2", "normal.png", "rough.png", "glass.png", "albedo.png", "thumb", "Body", "All materials"];
        List<ICompositeContent> contents = [.. names.Take(6).Select(_ => (ICompositeContent)new FakeContent()), Sheet(4), Sheet(2)];
        var set = new VariantContainer([.. names.Select(Variant)], contents, 0)
        {
            Kind = VariantKind.Textures,
            Textures = layout,
            StepOrder = layout.ShowableInListOrder()
        };

        using var section = new TexturesSection();
        section.Refresh(set);

        var picked = new List<int>();
        section.VariantSelected += picked.Add;

        Assert.Equal(TextureTreeBuilder.OverviewTitle, section.Roots[0].Data.Title);
        Assert.True(section.CanPick(section.Roots[0]));
        Assert.True(section.CanPick(Leaf(section.Roots, "Body")));

        var body = Leaf(section.Roots, "Body");
        body.IsExpanded = false;

        section.Pick(body);
        Assert.True(body.IsExpanded);

        section.Pick(section.Roots[0]);
        Assert.Equal([6, 7], picked);
        Assert.Equal("", section.PathText);

        Assert.True(set.Select(6));
        Assert.Equal("[Contact sheet 4 textures]", InfoSection.VariantNav(set));

        Assert.True(set.Select(7));
        Assert.Equal("[Contact sheet 2 materials]", InfoSection.VariantNav(set));
    }

    private static ContactSheetContent Sheet(int cells) => new([.. Enumerable.Range(0, cells).Select(i => new SheetCell($"{i}", null, "missing"))]);
}
