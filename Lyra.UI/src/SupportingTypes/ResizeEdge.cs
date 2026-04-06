namespace Lyra.UI.SupportingTypes;

[Flags]
public enum ResizeEdge
{
    None   = 0,
    Left   = 1,
    Right  = 2,
    Top    = 4,
    Bottom = 8,
    All    = Left | Right | Top | Bottom
}