namespace Lyra.Imaging.Content.Status;

public sealed record LoadWarning(string Message, string Detail)
{
    public static LoadWarning PartiallyDecoded(string detail) => new("Partially decoded", detail);

    public static LoadWarning Misnamed(string extension, string actual) => extension.Length == 0
        ? new LoadWarning("No extension", $"Read as {actual} from its contents")
        : new LoadWarning("Wrong extension", $"Named {extension}; read as {actual} from its contents");
}