namespace Lunapack.Cli;

internal sealed record ManifestOperationResult<T>(T? Value, string? Error)
{
    public bool IsSuccess => Error is null;

    public static ManifestOperationResult<T> Failure(string error) => new(default, error);

    public static ManifestOperationResult<T> Success(T value) => new(value, null);
}
