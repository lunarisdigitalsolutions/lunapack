using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.UnitTests;

internal static class ManifestOperationResultExtensions
{
    public static T RequireValue<T>(this ManifestOperationResult<T> result)
        where T : class =>
        result.Value
        ?? throw new InvalidOperationException(
            result.Error ?? "The operation did not return a value."
        );
}
