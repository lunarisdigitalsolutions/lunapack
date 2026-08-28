namespace Lunapack.Cli;

internal sealed record ResolvedPackParameterValue(
    PackParameterType Type,
    string StringValue,
    bool BooleanValue,
    IReadOnlyList<string>? StringValues = null
)
{
    public object Value =>
        StringValues is { } stringValues ? stringValues
        : Type == PackParameterType.Bool ? BooleanValue
        : StringValue;
}
