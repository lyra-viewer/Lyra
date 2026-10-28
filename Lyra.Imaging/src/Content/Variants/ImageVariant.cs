namespace Lyra.Imaging.Content.Variants;

public sealed record ImageVariant(string Label, int Width, int Height, string Detail, long? ByteSize)
{
    public override string ToString() => Label;
}