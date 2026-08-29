namespace Lunapack.Cli.Application.CommandExecution;

internal sealed record ManifestOperationResult<T>(
    T? Value,
    string? Error,
    ManifestOperationErrorKind ErrorKind
)
{
    public bool IsSuccess => Error is null;

    public static ManifestOperationResult<T> Failure(
        string error,
        ManifestOperationErrorKind errorKind = ManifestOperationErrorKind.General
    ) => new(default, error, errorKind);

    public static ManifestOperationResult<T> Success(T value) =>
        new(value, null, ManifestOperationErrorKind.General);
}
