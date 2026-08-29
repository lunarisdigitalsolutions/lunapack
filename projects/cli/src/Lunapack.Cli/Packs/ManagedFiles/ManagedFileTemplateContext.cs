using System.Diagnostics.CodeAnalysis;
using Lunapack.Cli.Application.Paths;

namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record ManagedFileTemplateContext
{
    private readonly IReadOnlyDictionary<string, string> _effectiveTargets;

    public ManagedFileTemplateContext(
        string currentEffectiveTarget,
        IReadOnlyDictionary<string, string> effectiveTargets
    )
    {
        CurrentEffectiveTarget = ProjectPath.Normalize(currentEffectiveTarget);
        _effectiveTargets = effectiveTargets;
    }

    public string CurrentEffectiveTarget { get; }

    public bool TryResolve(
        string declaredTarget,
        [NotNullWhen(true)] out string? effectiveTarget
    ) => _effectiveTargets.TryGetValue(ProjectPath.Normalize(declaredTarget), out effectiveTarget);
}
