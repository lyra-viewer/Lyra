namespace Lyra.Imaging.Content;

public sealed record LoadWarning(string Message, string Detail)
{
    public static LoadWarning PartiallyDecoded(string detail) => new("Partially decoded", detail);
}