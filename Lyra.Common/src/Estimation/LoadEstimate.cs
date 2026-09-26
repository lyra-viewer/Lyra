namespace Lyra.Common.Estimation;

public readonly record struct LoadEstimate(double Ms)
{
    public static readonly LoadEstimate None = new(0);
    public bool IsKnown => Ms > 0;
}