namespace Lyra.Imaging.Content;

public enum LayerSection
{
    Layer,

    // The bottom of a group's contents, in file order; the group's header comes after them;
    GroupDivider,

    OpenGroup,
    ClosedGroup
}

public sealed record LayerEntry(string? Name, bool Visible, LayerSection Section)
{
    public bool IsGroup => Section is LayerSection.OpenGroup or LayerSection.ClosedGroup;
}