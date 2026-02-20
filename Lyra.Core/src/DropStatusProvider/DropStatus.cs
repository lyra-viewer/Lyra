namespace Lyra.DropStatusProvider;

public readonly record struct DropStatus(
    bool Active,
    bool Aborted,
    long PathsEnqueued,
    long FilesEnumerated,
    long FilesSupported
);