namespace Lunapack.Cli;

internal sealed record ResolvedPackParameterValue(
    PackParameterType Type,
    string StringValue,
    bool BooleanValue
)
{
    public object Value => Type == PackParameterType.Bool ? BooleanValue : StringValue;
}
