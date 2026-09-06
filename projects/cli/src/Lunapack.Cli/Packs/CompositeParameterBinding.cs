namespace Lunapack.Cli.Packs;

internal sealed record CompositeParameterBinding(
    object Value,
    PackParameterBindingExpression? Expression = null
);
