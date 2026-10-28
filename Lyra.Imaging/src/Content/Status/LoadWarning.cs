namespace Lyra.Imaging.Content.Status;

public sealed record LoadWarning(string Message, string Detail)
{
    public static LoadWarning PartiallyDecoded(string detail) => new("Partially decoded", detail);
}