using Lunapack.Cli.Audit;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests.Audit;

public sealed class AuditOutputFormatterTests
{
    [Test]
    public async Task Scenario_ResolvedPackHasManagedTarget_IncludesProvenanceAndDigest()
    {
        var pack = new ProjectLockFile.ResolvedPack
        {
            Id = "dotnet-gitignore",
            Version = "1.0.0",
            SourcePath = "projects/packs",
            PackPath = "dotnet-gitignore",
            ManagedFiles =
            [
                new ProjectLockFile.ManagedFile
                {
                    TargetPath = ".gitignore",
                    Sha256 = "46CBF75C6CD48E8D4F3D3830AE06D5C23F00F29A23DC521A95A5DCCDF53BDA15",
                },
            ],
        };

        var output = AuditOutputFormatter.Format(pack);

        await Assert
            .That(output)
            .IsEqualTo(
                $"dotnet-gitignore@1.0.0{Environment.NewLine}  source: projects/packs/dotnet-gitignore{Environment.NewLine}  manages: .gitignore (46CBF75C6CD48E8D4F3D3830AE06D5C23F00F29A23DC521A95A5DCCDF53BDA15)"
            );
    }

    [Test]
    public async Task Scenario_CompositeResolvedPack_IncludesExactDependencies()
    {
        var pack = new ProjectLockFile.ResolvedPack
        {
            Id = "application",
            Version = "1.0.0",
            SourcePath = "projects/packs",
            PackPath = "application",
            Packs = [new ProjectLockFile.PackReference { Id = "shared", Version = "1.0.0" }],
        };

        var output = AuditOutputFormatter.Format(pack);

        await Assert
            .That(output)
            .IsEqualTo(
                $"application@1.0.0{Environment.NewLine}  source: projects/packs/application{Environment.NewLine}  depends on: shared@1.0.0"
            );
    }
}
