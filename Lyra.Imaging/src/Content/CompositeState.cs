namespace Lyra.Imaging.Content;

public enum CompositeState
{
    Pending,
    Loading,
    Ready,      // preview / full usable (tiles may still stream)
    Complete,   // everything finished (e.g., tiles fully decoded)
    Failed,
    Cancelled,
    Disposed
}
