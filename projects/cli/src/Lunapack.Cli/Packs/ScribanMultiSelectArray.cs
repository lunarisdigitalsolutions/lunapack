using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Lunapack.Cli.Packs;

internal sealed class ScribanMultiSelectArray(IEnumerable<string> values)
    : ScriptArray(values.Cast<object?>()),
        IScriptCustomFunction
{
    public int RequiredParameterCount => 2;

    public int ParameterCount => 2;

    public ScriptVarParamKind VarParamKind => ScriptVarParamKind.None;

    public Type ReturnType => typeof(bool);

    public ScriptParameterInfo GetParameterInfo(int index) =>
        index switch
        {
            0 => new ScriptParameterInfo(typeof(object), "contains"),
            1 => new ScriptParameterInfo(typeof(string), "value"),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

    public object Invoke(
        TemplateContext context,
        ScriptNode? callerContext,
        ScriptArray arguments,
        ScriptBlockStatement? blockStatement
    ) =>
        arguments[1] is string value
        && this.Any(item => string.Equals(item as string, value, StringComparison.Ordinal));

    public ValueTask<object?> InvokeAsync(
        TemplateContext context,
        ScriptNode? callerContext,
        ScriptArray arguments,
        ScriptBlockStatement? blockStatement
    ) => ValueTask.FromResult<object?>(Invoke(context, callerContext, arguments, blockStatement));
}
