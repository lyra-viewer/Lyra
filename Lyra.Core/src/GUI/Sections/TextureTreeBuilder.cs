using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Content.Variants;
using Lyra.UI.Components.Controls.TreeView;

namespace Lyra.GUI.Sections;

public sealed record TextureRow(string Title, int? Texture, int Count = 0);

public static class TextureTreeBuilder
{
    /// <summary>Up to this many leaves every group starts open; past it they start closed.</summary>
    public const int ExpandAllUpTo = 24;

    public const string OverviewTitle = "All materials";

    private sealed record Group(string Name, List<TextureRow> Leaves, int? Sheet);

    public static List<TreeNode<TextureRow>> Build(TextureLayout layout, IReadOnlyList<ImageVariant> variants)
    {
        var groups = layout.Materials
            .Select(material => new Group(material.Name, [.. material.Slots.SelectMany(Leaves)], material.Sheet))
            .Append(new Group("Unreferenced", [.. layout.Unreferenced.Select(texture => new TextureRow(variants[texture].Label, texture))], null))
            .Where(group => group.Leaves.Count > 0)
            .ToList();

        var expand = groups.Sum(group => group.Leaves.Count) <= ExpandAllUpTo;
        var roots = new List<TreeNode<TextureRow>>(groups.Count + 1);

        if (layout.Overview is { } overview)
            roots.Add(new TreeNode<TextureRow>(new TextureRow(OverviewTitle, overview)));

        foreach (var group in groups)
        {
            if (group.Leaves.Count == 1)
            {
                var only = group.Leaves[0];
                roots.Add(new TreeNode<TextureRow>(only with { Title = $"{group.Name} – {only.Title}" }));
                continue;
            }

            var textures = group.Leaves.Select(leaf => leaf.Texture).Distinct().Count();
            var node = new TreeNode<TextureRow>(new TextureRow(group.Name, group.Sheet, textures)) { IsExpanded = expand };
            node.AddChildren(group.Leaves);
            roots.Add(node);
        }

        return roots;
    }

    private static IEnumerable<TextureRow> Leaves(TextureSlot slot) =>
        slot.Alternates
            .Select(alternate => new TextureRow($"{slot.Label} · alternate", alternate))
            .Prepend(new TextureRow(slot.Label, slot.Texture));
}