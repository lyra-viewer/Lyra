namespace Lyra.UI.SupportingTypes;

public readonly record struct Padding(float Left, float Top, float Right, float Bottom)
{
    public Padding(float all) : this(all, all, all, all)
    {
    }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}