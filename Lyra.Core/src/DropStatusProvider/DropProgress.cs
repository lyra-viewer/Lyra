namespace Lyra.DropStatusProvider;

public readonly record struct DropProgress(
    bool Active,
    bool Aborted,
    long PathsEnqueued,
    long FilesEnumerated,
    long FilesSupported
);