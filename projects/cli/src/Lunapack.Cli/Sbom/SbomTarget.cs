namespace Lunapack.Cli.Sbom;

internal sealed record SbomTarget(string Name, string Version, SbomFormat Standard)
{
    public static IReadOnlyList<SbomTarget> Supported { get; } =
    [
        new("cyclonedx-1.6", "1.6", SbomFormat.CycloneDx),
        new("cyclonedx-1.7", "1.7", SbomFormat.CycloneDx),
        new("spdx-2.3", "2.3", SbomFormat.Spdx),
        new("spdx-3.0.1", "3.0.1", SbomFormat.Spdx),
    ];

    public static SbomTarget? Parse(string? name) =>
        Supported.FirstOrDefault(target =>
            string.Equals(target.Name, name, StringComparison.Ordinal)
        );
}
