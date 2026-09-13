namespace Lyra.Common.Estimation;

public readonly record struct LoadEstimate(double Ms, bool IncludesTransfer)
{
    public static readonly LoadEstimate None = new(0, false);
    public bool IsKnown => Ms > 0;
}