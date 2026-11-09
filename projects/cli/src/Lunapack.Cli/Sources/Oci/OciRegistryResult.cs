namespace Lunapack.Cli.Sources.Oci;

internal sealed record OciRegistryResult<T>(T? Value, OciRegistryFailure? Failure)
    where T : class
{
    public bool IsSuccess => Failure is null;

    public static OciRegistryResult<T> Failed(OciRegistryFailure failure) => new(null, failure);

    public static OciRegistryResult<T> Succeeded(T value) => new(value, null);
}
