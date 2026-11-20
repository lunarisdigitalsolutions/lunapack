using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sbom;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.UnitTests.Sbom;

public sealed class SbomInventoryBuilderTests
{
    [Test]
    public async Task Build_WhenPackHasDependenciesAndExternalSources_PreservesLockedEvidence()
    {
        var inventory = SbomInventoryBuilder.Build(CreateMixedLockFile());
        var root = inventory.Packs.Single(pack =>
            string.Equals(pack.Id, "root-pack", StringComparison.Ordinal)
        );
        var file = root.Files.Single();
        var externalSource = root.ExternalSources.Single();

        await Assert.That(root.Reference).IsEqualTo("pkg:generic/lunapack/root-pack@1.2.3");
        await Assert.That(root.SourceIdentity?.Type).IsEqualTo("local");
        await Assert.That(root.SourceIdentity?.Path).IsEqualTo("packs");
        await Assert
            .That(root.Dependencies.Select(dependency => dependency.Reference))
            .IsEquivalentTo(["pkg:generic/lunapack/git-pack@2.0.0"]);
        await Assert.That(externalSource.Alias).IsEqualTo("docs");
        await Assert
            .That(externalSource.ResolvedCommit)
            .IsEqualTo("1111111111111111111111111111111111111111");
        await Assert.That(file.TargetPath).IsEqualTo("docs/readme.md");
        await Assert.That(file.Sha256).IsEqualTo(new string('a', 64));
        await Assert.That(file.SourcePath).IsEqualTo("templates/readme.md");
        await Assert.That(file.OwnerReference).IsEqualTo(root.Reference);
        await Assert.That(file.Reference).StartsWith("urn:lunapack:file:sha256:");
    }

    [Test]
    public async Task Build_WhenGitAndOciPacksExist_PreservesImmutableProvenance()
    {
        var inventory = SbomInventoryBuilder.Build(CreateMixedLockFile());
        var git = inventory.Packs.Single(pack =>
            string.Equals(pack.Id, "git-pack", StringComparison.Ordinal)
        );
        var oci = inventory.Packs.Single(pack =>
            string.Equals(pack.Id, "oci-pack", StringComparison.Ordinal)
        );

        await Assert
            .That(git.GitSource?.ResolvedCommit)
            .IsEqualTo("2222222222222222222222222222222222222222");
        await Assert.That(git.GitSource?.Ref).IsEqualTo("v2");
        await Assert.That(oci.OciSource?.RegistryUrl).IsEqualTo("https://registry.example.test");
        await Assert.That(oci.OciSource?.Repository).IsEqualTo("team/packs");
        await Assert.That(oci.OciSource?.ManifestDigest).IsEqualTo($"sha256:{new string('b', 64)}");
        await Assert.That(oci.OciSource?.ContentDigest).IsEqualTo($"sha256:{new string('c', 64)}");
        await Assert.That(oci.OciSource?.ContentSize).IsEqualTo(4096);
    }

    [Test]
    public async Task Build_WhenLinkHasLockedFiles_PreservesProvenanceAndOwnership()
    {
        var inventory = SbomInventoryBuilder.Build(CreateMixedLockFile());
        var link = inventory.Links.Single();
        var file = link.Files.Single();

        await Assert.That(link.Name).IsEqualTo("guides");
        await Assert.That(link.SourceIdentity.Ref).IsEqualTo("main");
        await Assert
            .That(link.GitSource?.ResolvedCommit)
            .IsEqualTo("3333333333333333333333333333333333333333");
        await Assert.That(link.DefinitionSha256).IsEqualTo(new string('d', 64));
        await Assert.That(file.TargetPath).IsEqualTo("docs/setup.md");
        await Assert.That(file.Sha256).IsEqualTo(new string('e', 64));
        await Assert.That(file.OwnerReference).IsEqualTo(link.Reference);
    }

    [Test]
    public async Task Build_WhenDependencyEdgesRepeat_EmitsOneCanonicalEdge()
    {
        var lockFile = CreateMixedLockFile();
        var root = lockFile.Packs.Single(pack =>
            string.Equals(pack.Id, "root-pack", StringComparison.Ordinal)
        );
        root.Packs.Add(root.Packs.Single() with { });

        var inventory = SbomInventoryBuilder.Build(lockFile);

        await Assert
            .That(
                inventory
                    .Packs.Single(pack =>
                        string.Equals(pack.Id, "root-pack", StringComparison.Ordinal)
                    )
                    .Dependencies
            )
            .Count()
            .IsEqualTo(1);
    }

    [Test]
    public async Task Build_WhenOptionalSourceEvidenceIsAbsent_OmitsIt()
    {
        var lockFile = new ProjectLockFile
        {
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "local-pack",
                    Version = "1.0.0",
                    PackPath = "local-pack",
                },
            ],
        };

        var inventory = SbomInventoryBuilder.Build(lockFile);
        var pack = inventory.Packs.Single();

        await Assert.That(pack.SourceIdentity).IsNull();
        await Assert.That(pack.GitSource).IsNull();
        await Assert.That(pack.OciSource).IsNull();
        await Assert.That(pack.ExternalSources).IsEmpty();
        await Assert.That(inventory.Links).IsEmpty();
    }

    [Test]
    public async Task Build_WhenGitUrlsContainCredentialsAndQuery_RedactsSensitiveParts()
    {
        const string unsafeUrl =
            "https://user:token@example.test/packs.git?access_token=secret#fragment";
        const string unsafeFingerprint =
            "git:https://user:token@example.test/docs.git?access_token=secret@main#/";
        var pack = CreateGitPack();
        pack.SourceIdentity = ConfiguredSourceIdentity.CreateGit(unsafeUrl, "v2", "packs");
        pack.ExternalSources["docs"] = new ProjectLockFile.ExternalSourceLock
        {
            SourceName = "documentation",
            Fingerprint = unsafeFingerprint,
            Ref = "main",
            ResolvedCommit = "1111111111111111111111111111111111111111",
        };
        pack.GitSource = new GitSourceProvenance
        {
            Url = unsafeUrl,
            Ref = "v2",
            Path = "packs",
            ResolvedCommit = "2222222222222222222222222222222222222222",
        };
        var inventory = SbomInventoryBuilder.Build(new ProjectLockFile { Packs = [pack] });
        var resolved = inventory.Packs.Single();

        await Assert.That(resolved.SourceIdentity?.Url).IsEqualTo("https://example.test/packs.git");
        await Assert.That(resolved.GitSource?.Url).IsEqualTo("https://example.test/packs.git");
        await Assert
            .That(resolved.ExternalSources.Single().Fingerprint)
            .IsEqualTo("git:https://example.test/docs.git@main#/");
    }

    [Test]
    public async Task Build_WhenFingerprintHasNoCanonicalSuffix_RedactsEntireMalformedValue()
    {
        const string malformed =
            "git:https://user:token@example.test/private.git?access_token=secret";
        var pack = CreateGitPack();
        pack.ExternalSources["docs"] = new ProjectLockFile.ExternalSourceLock
        {
            SourceName = "documentation",
            Fingerprint = malformed,
            Ref = "main",
            ResolvedCommit = "1111111111111111111111111111111111111111",
        };

        var inventory = SbomInventoryBuilder.Build(new ProjectLockFile { Packs = [pack] });

        await Assert
            .That(inventory.Packs.Single().ExternalSources.Single().Fingerprint)
            .IsEqualTo("git:[redacted-malformed]");
    }

    [Test]
    [Arguments("git:user:password@host:repo@main#/", "git:[redacted-malformed]")]
    [Arguments("git:git@host:repo@main#/", "git:host:repo@main#/")]
    [Arguments("git:https://host/repo@main#access_token=secret", "git:[redacted-malformed]")]
    [Arguments("git:https:////user:password@host/repo@main#/", "git:[redacted-malformed]")]
    [Arguments("git:https://-bad/repo@main#/", "git:[redacted-malformed]")]
    [Arguments("git:example.test/team/repo@archive@main#/", "git:[redacted-malformed]")]
    [Arguments("git:https:/user:password@host/repo@main#/", "git:[redacted-malformed]")]
    [Arguments("git:user:password@host:repo@archive@main#/", "git:[redacted-malformed]")]
    [Arguments("git:custom://user:password@/repo@main#/", "git:[redacted-malformed]")]
    [Arguments("git:ftp://user:password@host/repo@main#/", "git:[redacted-malformed]")]
    [Arguments("git:git.example:team/repo@main#/", "git:git.example:team/repo@main#/")]
    [Arguments("git:git://user:SECRET@host/repo@main#/", "git:git://host/repo@main#/")]
    [Arguments("git:user:part@secret@host:repo@main#/", "git:[redacted-malformed]")]
    [Arguments("git:token@example.test@main#/", "git:[redacted-malformed]")]
    [Arguments("git:https//user:password@host/repo@main#/", "git:[redacted-malformed]")]
    [Arguments("git:user:supersecret?@host:repo@main#/", "git:[redacted-malformed]")]
    [Arguments("local:https://user:password@host/repo?token=secret", "local:[redacted-malformed]")]
    [Arguments("oci:https://host/repo", "git:[redacted-malformed]")]
    [Arguments("git:https://host/repo@main#/?access_token=secret", "git:[redacted-malformed]")]
    public async Task Build_WhenFingerprintUsesUnsafeLegacyShape_EmitsOnlyRedactedEvidence(
        string unsafeFingerprint,
        string expectedFingerprint
    )
    {
        var pack = CreateGitPack();
        pack.ExternalSources["docs"] = new ProjectLockFile.ExternalSourceLock
        {
            SourceName = "documentation",
            Fingerprint = unsafeFingerprint,
            Ref = "main",
            ResolvedCommit = "1111111111111111111111111111111111111111",
        };

        var inventory = SbomInventoryBuilder.Build(new ProjectLockFile { Packs = [pack] });

        await Assert
            .That(inventory.Packs.Single().ExternalSources.Single().Fingerprint)
            .IsEqualTo(expectedFingerprint);
    }

    [Test]
    [Arguments("@", false)]
    [Arguments(".main", false)]
    [Arguments("feature/.hidden", false)]
    [Arguments("release/main", true)]
    public async Task GitRefSafety_WhenRefUsesGitNameRules_ReportsValidity(
        string reference,
        bool expected
    )
    {
        await Assert.That(GitRefSafety.IsSafe(reference)).IsEqualTo(expected);
    }

    [Test]
    public async Task Validate_WhenLockReusesManagedTarget_ReportsDuplicateOwnerTarget()
    {
        var lockFile = CreateMixedLockFile();
        var root = lockFile.Packs.Single(pack =>
            string.Equals(pack.Id, "root-pack", StringComparison.Ordinal)
        );
        root.ManagedFiles.Add(root.ManagedFiles.Single() with { });
        var link = lockFile.Links["guides"];
        link.Files.Add(link.Files.Single() with { });

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert
            .That(issues)
            .Contains(
                "Resolved pack 'root-pack' contains duplicate managed-file target 'docs/readme.md'."
            );
        await Assert
            .That(issues)
            .Contains(
                "Resolved link 'guides' contains duplicate managed-file target 'docs/setup.md'."
            );
    }

    [Test]
    public async Task Validate_WhenLinkGitProvenanceHasNoUrl_ReportsInvalidEvidence()
    {
        var lockFile = CreateMixedLockFile();
        lockFile.Links["guides"].GitSource!.Url = string.Empty;

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert
            .That(issues)
            .Contains(
                "Git provenance must define a URL, resolved commit, and valid optional ref and path."
            );
        await Assert
            .That(issues)
            .Contains("Resolved link 'guides' Git source identity and provenance must match.");
    }

    [Test]
    public async Task Validate_WhenGitLinkHasNoProvenance_ReportsMissingCommitEvidence()
    {
        var lockFile = CreateMixedLockFile();
        lockFile.Links["guides"].GitSource = null;

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert.That(issues).Contains("Resolved link 'guides' must record Git provenance.");
    }

    [Test]
    public async Task Validate_WhenExternalRefContainsUrlDelimiters_ReportsInvalidEvidence()
    {
        var lockFile = CreateMixedLockFile();
        var root = lockFile.Packs.Single(pack =>
            string.Equals(pack.Id, "root-pack", StringComparison.Ordinal)
        );
        root.ExternalSources["docs"].Ref = "main?access_token=secret";

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert
            .That(issues)
            .Contains(
                "Resolved pack 'root-pack' external source 'docs' must record source name, fingerprint, ref, and resolved commit."
            );
    }

    private static ProjectLockFile CreateMixedLockFile() =>
        new()
        {
            SchemaVersion = 1,
            Packs = [CreateOciPack(), CreateRootPack(), CreateGitPack()],
            Links = { ["guides"] = CreateLink() },
        };

    private static ProjectLockFile.ResolvedPack CreateRootPack() =>
        new()
        {
            Id = "root-pack",
            Version = "1.2.3",
            PackPath = "root-pack",
            SourceName = "local",
            SourcePath = "packs",
            SourceIdentity = ConfiguredSourceIdentity.CreateLocal("packs"),
            Packs = [new ProjectLockFile.PackReference { Id = "git-pack", Version = "2.0.0" }],
            ExternalSources =
            {
                ["docs"] = new ProjectLockFile.ExternalSourceLock
                {
                    SourceName = "documentation",
                    Fingerprint = "git:https://example.test/docs@main#/",
                    Ref = "main",
                    ResolvedCommit = "1111111111111111111111111111111111111111",
                },
            },
            ManagedFiles =
            [
                new ProjectLockFile.ManagedFile
                {
                    DeclaredTargetPath = "docs/readme.md",
                    TargetPath = "docs/readme.md",
                    SourcePath = "templates/readme.md",
                    SourceAlias = "docs",
                    SourceName = "documentation",
                    SourceFingerprint = "git:https://example.test/docs@main#/",
                    Sha256 = new string('a', 64),
                    Content = "must not be exported",
                },
            ],
        };

    private static ProjectLockFile.ResolvedPack CreateGitPack() =>
        new()
        {
            Id = "git-pack",
            Version = "2.0.0",
            PackPath = "git-pack",
            SourceName = "git-source",
            SourceIdentity = ConfiguredSourceIdentity.CreateGit(
                "https://example.test/packs.git",
                "v2",
                "packs"
            ),
            GitSource = new GitSourceProvenance
            {
                Url = "https://example.test/packs.git",
                Ref = "v2",
                Path = "packs",
                ResolvedCommit = "2222222222222222222222222222222222222222",
            },
        };

    private static ProjectLockFile.ResolvedPack CreateOciPack() =>
        new()
        {
            Id = "oci-pack",
            Version = "3.0.0",
            PackPath = "oci-pack",
            SourceName = "container-registry",
            SourceIdentity = ConfiguredSourceIdentity.CreateOci(
                "https://registry.example.test",
                "team/packs"
            ),
            OciSource = new OciSourceProvenance
            {
                RegistryUrl = "https://registry.example.test",
                Repository = "team/packs",
                Tag = "v3",
                ManifestDigest = $"sha256:{new string('b', 64)}",
                ContentDigest = $"sha256:{new string('c', 64)}",
                ContentSize = 4096,
            },
        };

    private static ProjectLockFile.ResolvedLink CreateLink() =>
        new()
        {
            SourceName = "git-source",
            SourceIdentity = ConfiguredSourceIdentity.CreateGit(
                "https://example.test/links.git",
                "main",
                "guides"
            ),
            GitSource = new GitSourceProvenance
            {
                Url = "https://example.test/links.git",
                Ref = "main",
                Path = "guides",
                ResolvedCommit = "3333333333333333333333333333333333333333",
            },
            DefinitionSha256 = new string('d', 64),
            Files =
            [
                new ProjectLockFile.LinkFile
                {
                    DeclaredTargetPath = "docs/setup.md",
                    TargetPath = "docs/setup.md",
                    SourcePath = "setup.md",
                    Sha256 = new string('e', 64),
                },
            ],
        };
}
