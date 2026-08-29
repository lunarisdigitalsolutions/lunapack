using Lunapack.Cli.Packs;

namespace Lunapack.Cli.SecurityTests;

public sealed class TemplateSecurityTests
{
    [Test]
    [Arguments("{{ os.system 'whoami' }}")]
    [Arguments("{{ process.start 'executable' }}")]
    [Arguments("{{ file.read_all_text 'secret.txt' }}")]
    public async Task RenderText_WhenTemplateRequestsPrivilegedGlobal_ReturnsFailure(
        string template
    )
    {
        var result = PackTemplateRenderer.RenderText(
            template,
            "untrusted-template",
            CreateParameters()
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("cannot be rendered");
    }

    [Test]
    public async Task RenderText_WhenLoopExceedsComputationLimit_ReturnsFailure()
    {
        var result = PackTemplateRenderer.RenderText(
            "{{ for index in 1..1001 }}x{{ end }}",
            "untrusted-template",
            CreateParameters()
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("cannot be rendered");
    }

    private static ResolvedPackParameters CreateParameters() =>
        new(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal),
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
        );
}
