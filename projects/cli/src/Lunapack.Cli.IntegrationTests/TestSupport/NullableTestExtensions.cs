namespace Lunapack.Cli.IntegrationTests;

internal static class NullableTestExtensions
{
    public static T RequireNotNull<T>(this T? value)
        where T : class =>
        value ?? throw new InvalidOperationException("Expected test value to be non-null.");
}
