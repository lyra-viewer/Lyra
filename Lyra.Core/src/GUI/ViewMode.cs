namespace Lyra.GUI;

/// <summary>
/// What the navigation keys act on. Derived from the navigation state, never stored,
/// so it cannot drift from it. Overlays sit on top of any mode (<see cref="OverlayKind"/>).
/// </summary>
public enum ViewMode
{
    /// <summary>The whole collection, one file at a time.</summary>
    Default,

    /// <summary>The collection narrowed to duplicate groups.</summary>
    Duplicates,

    /// <summary>One model's textures and materials. Not built yet.</summary>
    Container
}