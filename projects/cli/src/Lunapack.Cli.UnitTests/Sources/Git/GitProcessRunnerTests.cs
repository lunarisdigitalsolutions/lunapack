using System.Diagnostics;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests.Sources.Git;

[NotInParallel]
public sealed class GitProcessRunnerTests
{
    [Test]
    [NotInParallel]
    public async Task StartInfo_WhenDoctorModeIsUsed_RemovesInheritedGitSecrets()
    {
        var configCount = Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT");
        var configKey = Environment.GetEnvironmentVariable("GIT_CONFIG_KEY_0");
        var configValue = Environment.GetEnvironmentVariable("GIT_CONFIG_VALUE_0");
        var trace = Environment.GetEnvironmentVariable("GIT_TRACE_CURL");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "http.extraHeader");
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "secret-header");
            Environment.SetEnvironmentVariable("GIT_TRACE_CURL", "1");
            var executable = OperatingSystem.IsWindows()
                ? "C:\\Windows\\System32\\cmd.exe"
                : "/bin/sh";

            var startInfo = new GitProcessRunner(
                executable,
                nonInteractive: true
            ).CreateStartInfoForTests(executable);

            await Assert.That(startInfo.Environment).DoesNotContainKey("GIT_CONFIG_COUNT");
            await Assert.That(startInfo.Environment).DoesNotContainKey("GIT_CONFIG_KEY_0");
            await Assert.That(startInfo.Environment).DoesNotContainKey("GIT_CONFIG_VALUE_0");
            await Assert.That(startInfo.Environment).DoesNotContainKey("GIT_TRACE_CURL");
            await Assert.That(startInfo.Environment["GIT_CONFIG_NOSYSTEM"]).IsEqualTo("1");
            await Assert.That(startInfo.Environment["GIT_SSH_COMMAND"]).StartsWith("\"");
            await Assert.That(startInfo.Environment["GIT_SSH_COMMAND"]).DoesNotStartWith("ssh");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", configCount);
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", configKey);
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", configValue);
            Environment.SetEnvironmentVariable("GIT_TRACE_CURL", trace);
        }
    }

    [Test]
    [Arguments(
        "fatal: Authentication failed for 'https://user:sentinel@registry.example.test/packs.git'",
        "Git authentication failed"
    )]
    [Arguments(
        "fatal: could not read Username for 'https://registry.example.test': terminal prompts disabled",
        "Git authentication failed"
    )]
    [Arguments("Permission denied (publickey).", "Git authentication failed")]
    [Arguments("No supported authentication methods available.", "Git authentication failed")]
    [Arguments(
        "fatal: unable to access 'https://registry.example.test/packs.git': Could not resolve host",
        "Git host is unavailable"
    )]
    [Arguments(
        "SSL certificate problem: unable to get local issuer certificate",
        "TLS certificate"
    )]
    [Arguments("fatal: transport 'http' not allowed", "rewritten to an insecure transport")]
    public async Task CreateExitCodeError_WhenGitReportsFailure_ReturnsRedactedGuidance(
        string diagnostic,
        string expectedGuidance
    )
    {
        var error = GitProcessRunner.CreateExitCodeError(
            128,
            new GitProcessOutput(string.Empty, diagnostic)
        );

        await Assert.That(error).Contains("Git exited with code 128");
        await Assert.That(error).Contains(expectedGuidance);
        await Assert.That(error).DoesNotContain("sentinel");
        await Assert.That(error).DoesNotContain("registry.example.test");
    }

    [Test]
    public async Task ProcessRunner_WhenHttpTransportIsSecureOnly_BlocksBeforeConnecting()
    {
        var result = await new GitProcessRunner().RunAsync(
            ["ls-remote", "http://127.0.0.1:1/packs.git"],
            TimeSpan.FromSeconds(10),
            GitTransportMode.SecureOnly,
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("rewritten to an insecure transport");
    }

    [Test]
    public async Task ProcessRunner_WhenGitTransportIsSecureOnly_BlocksBeforeConnecting()
    {
        var result = await new GitProcessRunner().RunAsync(
            ["ls-remote", "git://127.0.0.1:1/packs.git"],
            TimeSpan.FromSeconds(10),
            GitTransportMode.SecureOnly,
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("rewritten to an insecure transport");
    }

    [Test]
    public async Task ProcessRunner_WhenHttpsUrlIsRewrittenToHttp_BlocksRewrittenTransport()
    {
        var result = await new GitProcessRunner().RunAsync(
            [
                "-c",
                "url.http://127.0.0.1:1/.insteadOf=https://example.test/",
                "ls-remote",
                "https://example.test/packs.git",
            ],
            TimeSpan.FromSeconds(10),
            GitTransportMode.SecureOnly,
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("rewritten to an insecure transport");
        await Assert.That(result.Error).DoesNotContain("example.test");
    }

    [Test]
    public async Task ProcessRunner_WhenInheritedProtocolAllowlistEnablesHttp_BlocksRewrite()
    {
        var configPath = CreateGlobalConfig(
            "[url \"http://127.0.0.1:1/\"]\n\tinsteadOf = https://example.test/\n"
        );
        try
        {
            var arguments = new List<string>(
                GitTransportSecurity.CreateTransportArguments(GitTransportMode.SecureOnly)
            )
            {
                "ls-remote",
                "https://example.test/packs.git",
            };
            var (exitCode, _, standardError) = await RunGitAsync(
                configPath,
                arguments,
                GitTransportMode.SecureOnly,
                "https://example.test/packs.git",
                environmentVariables: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["GIT_ALLOW_PROTOCOL"] = "http",
                }
            );

            await Assert.That(exitCode == 0).IsFalse();
            await Assert.That(standardError).Contains("transport 'http' not allowed");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Test]
    [Arguments("followRedirects", "true")]
    [Arguments("sslVerify", "false")]
    public async Task ProcessRunner_WhenRewriteTargetOverridesHttpsSecurity_FailsClosed(
        string setting,
        string inheritedValue
    )
    {
        var configPath = CreateGlobalConfig(
            "[url \"https://mirror.example.test/\"]\n"
                + "\tinsteadOf = https://origin.example.test/\n"
        );
        var names = new[] { "GIT_CONFIG_GLOBAL", "GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_PARAMETERS" };
        var previousValues = names.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal
        );
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
            Environment.SetEnvironmentVariable(
                "GIT_CONFIG_PARAMETERS",
                $"'http.https://mirror.example.test/packs.git.{setting}={inheritedValue}'"
            );

            var result = await new GitProcessRunner().RunAsync(
                ["ls-remote", "https://origin.example.test/packs.git"],
                TimeSpan.FromSeconds(10),
                GitTransportMode.SecureOnly,
                "https://origin.example.test/packs.git",
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("inherited Git configuration overrides");
            await Assert.That(result.Error).DoesNotContain("origin.example.test");
            await Assert.That(result.Error).DoesNotContain("mirror.example.test");
        }
        finally
        {
            foreach (var (name, value) in previousValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
            File.Delete(configPath);
        }
    }

    [Test]
    public async Task ProcessRunner_WhenRewriteTargetEmbedsCredentials_FailsClosed()
    {
        var configPath = CreateGlobalConfig(
            "[url \"https://user:sentinel@mirror.example.test/\"]\n"
                + "\tinsteadOf = https://origin.example.test/\n"
        );
        var previousGlobalConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        var previousNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");

            var result = await new GitProcessRunner().RunAsync(
                ["ls-remote", "https://origin.example.test/packs.git"],
                TimeSpan.FromSeconds(10),
                GitTransportMode.SecureOnly,
                "https://origin.example.test/packs.git",
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("must not embed");
            await Assert.That(result.Error).DoesNotContain("sentinel");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobalConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", previousNoSystem);
            File.Delete(configPath);
        }
    }

    [Test]
    public async Task ProcessRunner_WhenRewriteTargetHasTrailingWhitespace_FailsClosed()
    {
        var configPath = CreateGlobalConfig(
            "[url \"https://mirror.example.test/packs.git\u00a0\"]\n"
                + "\tinsteadOf = https://origin.example.test/packs.git\n"
        );
        var previousGlobalConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        var previousNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");

            var result = await new GitProcessRunner().RunAsync(
                ["ls-remote", "https://origin.example.test/packs.git"],
                TimeSpan.FromSeconds(10),
                GitTransportMode.SecureOnly,
                "https://origin.example.test/packs.git",
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("invalid effective repository URL");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobalConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", previousNoSystem);
            File.Delete(configPath);
        }
    }

    [Test]
    public async Task ProcessRunner_WhenConditionalRewriteDependsOnRepository_UsesSameContext()
    {
        var repository = Path.Combine(
            Path.GetTempPath(),
            $"lunapack-git-config-{Guid.NewGuid():N}"
        );
        var includePath = Path.Combine(
            Path.GetTempPath(),
            $"lunapack-git-{Guid.NewGuid():N}.config"
        );
        var configPath = CreateGlobalConfig(
            "[includeIf \"hasconfig:remote.*.url:https://origin.example.test/**\"]\n"
                + $"\tpath = {includePath.Replace('\\', '/')}\n"
        );
        File.WriteAllText(
            includePath,
            "[url \"https://mirror.example.test/\"]\n"
                + "\tinsteadOf = https://origin.example.test/\n"
        );
        var names = new[] { "GIT_CONFIG_GLOBAL", "GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_PARAMETERS" };
        var previousValues = names.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal
        );
        try
        {
            await RunGitAsync(configPath, ["init", repository]);
            await RunGitAsync(
                configPath,
                [
                    "-C",
                    repository,
                    "remote",
                    "add",
                    "--",
                    "origin",
                    "https://origin.example.test/packs.git",
                ]
            );
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
            Environment.SetEnvironmentVariable(
                "GIT_CONFIG_PARAMETERS",
                "'http.https://mirror.example.test/packs.git.sslVerify=false'"
            );

            var result = await new GitProcessRunner().RunAsync(
                ["-C", repository, "fetch", "origin"],
                TimeSpan.FromSeconds(10),
                GitTransportMode.SecureOnly,
                "https://origin.example.test/packs.git",
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("inherited Git configuration overrides");
        }
        finally
        {
            foreach (var (name, value) in previousValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
            File.Delete(configPath);
            File.Delete(includePath);
            DeleteRepository(repository);
        }
    }

    [Test]
    public async Task ProcessRunner_WhenHttpTransportIsOptedIn_AllowsProtocolAttempt()
    {
        var result = await new GitProcessRunner().RunAsync(
            ["ls-remote", "http://127.0.0.1:1/packs.git"],
            TimeSpan.FromSeconds(10),
            GitTransportMode.AllowHttp,
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).DoesNotContain("rewritten to an insecure transport");
    }

    [Test]
    public async Task ProcessRunner_WhenHttpIsOptedIn_DoesNotInvokeConfiguredCredentialHelpers()
    {
        var configPath = CreateGlobalConfig(
            "[credential]\n\thelper = \"!printf 'username=sentinel\\\\npassword=sentinel\\\\n'\"\n"
        );
        try
        {
            const string credentialInput = "protocol=https\nhost=example.test\n\n";
            var (configuredHelperExitCode, configuredHelperOutput, _) = await RunGitAsync(
                configPath,
                ["credential", "fill"],
                standardInput: credentialInput
            );
            await Assert.That(configuredHelperExitCode).IsEqualTo(0);
            await Assert.That(configuredHelperOutput).Contains("username=sentinel");

            var arguments = new List<string>(
                GitTransportSecurity.CreateTransportArguments(GitTransportMode.AllowHttp)
            )
            {
                "credential",
                "fill",
            };
            var (exitCode, standardOutput, standardError) = await RunGitAsync(
                configPath,
                arguments,
                standardInput: credentialInput
            );

            await Assert.That(exitCode == 0).IsFalse();
            await Assert.That(standardOutput).DoesNotContain("sentinel");
            await Assert.That(standardError).DoesNotContain("sentinel");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Test]
    [Arguments("http.followRedirects", "false")]
    [Arguments("http.sslVerify", "true")]
    public async Task ProcessRunner_WhenSecureOnlyOverridesUrlSpecificHttpsSettings(
        string setting,
        string expectedValue
    )
    {
        var configPath = CreateGlobalConfig(
            "[http]\n\tfollowRedirects = true\n\tsslVerify = false\n"
                + "[http \"https://example.test\"]\n\tfollowRedirects = true\n\tsslVerify = false\n"
        );
        try
        {
            var arguments = new List<string>(
                GitTransportSecurity.CreateTransportArguments(GitTransportMode.SecureOnly)
            )
            {
                "config",
                "--get-urlmatch",
                setting,
                "https://example.test/packs.git",
            };
            var (exitCode, standardOutput, _) = await RunGitAsync(
                configPath,
                arguments,
                GitTransportMode.SecureOnly,
                "https://example.test/packs.git"
            );

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(standardOutput.Trim()).IsEqualTo(expectedValue);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Test]
    public async Task ConfigureSecureHttps_WhenUrlContainsCredentials_RejectsWithoutLeaking()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "test.setting",
            ["GIT_CONFIG_VALUE_0"] = "preserved",
        };
        var error = GitTransportSecurity.ConfigureSecureHttps(
            environment,
            GitTransportMode.SecureOnly,
            "https://sentinel-user:sentinel-password@example.test/packs.git"
        );

        await Assert.That(error).Contains("must not embed");
        await Assert.That(environment["GIT_CONFIG_COUNT"]).IsEqualTo("1");
        await Assert.That(error).DoesNotContain("sentinel");
        await Assert.That(string.Join(" ", environment.Values)).DoesNotContain("sentinel");
    }

    [Test]
    public async Task ConfigureSecureHttps_WhenUrlContainsQuery_RejectsConfiguration()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_CONFIG_COUNT"] = "0",
        };

        var error = GitTransportSecurity.ConfigureSecureHttps(
            environment,
            GitTransportMode.SecureOnly,
            "https://example.test/packs.git?token=sentinel"
        );

        await Assert.That(error).Contains("queries or fragments");
        await Assert.That(environment["GIT_CONFIG_COUNT"]).IsEqualTo("0");
        await Assert.That(error).DoesNotContain("sentinel");
    }

    [Test]
    public async Task ConfigureSecureHttps_WhenGitHttpsUrlHasInvalidAuthority_FailsClosed()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_CONFIG_COUNT"] = "0",
        };

        var error = GitTransportSecurity.ConfigureSecureHttps(
            environment,
            GitTransportMode.SecureOnly,
            "https:///mirror.example.test/packs.git"
        );

        await Assert.That(error).Contains("valid HTTPS URL");
        await Assert.That(environment["GIT_CONFIG_COUNT"]).IsEqualTo("0");
    }

    [Test]
    public async Task ConfigureSecureHttps_WhenScpHostIsNamedHttps_PreservesSshLocation()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_CONFIG_COUNT"] = "0",
        };

        var error = GitTransportSecurity.ConfigureSecureHttps(
            environment,
            GitTransportMode.SecureOnly,
            "https:organization/packs.git"
        );

        await Assert.That(error is null).IsTrue();
        await Assert.That(environment["GIT_CONFIG_COUNT"]).IsEqualTo("0");
    }

    [Test]
    public async Task ConfigureSecureHttps_WhenUrlContainsBackslash_RejectsConfiguration()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_CONFIG_COUNT"] = "0",
        };

        var error = GitTransportSecurity.ConfigureSecureHttps(
            environment,
            GitTransportMode.SecureOnly,
            @"https://example.test/packs\archive.git"
        );

        await Assert.That(error).Contains("must not contain backslashes");
        await Assert.That(environment["GIT_CONFIG_COUNT"]).IsEqualTo("0");
    }

    [Test]
    [Arguments("false\n", true)]
    [Arguments("fal\uFFFDse\n", false)]
    public async Task TransportSecurity_WhenHttpsProbeIsIncomplete_FailsClosed(
        string probeOutput,
        bool truncated
    )
    {
        var security = new GitTransportSecurity(
            (_, _, _) =>
                Task.FromResult(
                    ManifestOperationResult<GitProcessOutput>.Success(
                        new GitProcessOutput(
                            probeOutput,
                            string.Empty,
                            StandardOutputTruncated: truncated
                        )
                    )
                )
        );

        var result = await security.VerifySecureHttpsAsync(
            TimeSpan.FromSeconds(10),
            GitTransportMode.SecureOnly,
            "https://example.test/packs.git",
            workingDirectory: null,
            CancellationToken.None,
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("inherited Git configuration overrides");
    }

    [Test]
    public async Task ProcessRunner_WhenNetworkSourceRewritesToLocalPath_BlocksBeforeAccess()
    {
        var configPath = CreateGlobalConfig(
            "[url \"file:///temporary/lunapack-repository/\"]\n"
                + "\tinsteadOf = https://origin.example.test/\n"
        );
        var previousGlobalConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        var previousNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");

            var result = await new GitProcessRunner().RunAsync(
                ["ls-remote", "https://origin.example.test/packs.git"],
                TimeSpan.FromSeconds(10),
                GitTransportMode.SecureOnly,
                "https://origin.example.test/packs.git",
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("disallowed local");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobalConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", previousNoSystem);
            File.Delete(configPath);
        }
    }

    [Test]
    public async Task ConfigureTransportEnvironment_WhenEnvironmentOverridesSecurity_UsesFixedValues()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_ALLOW_PROTOCOL"] = "http",
            ["GIT_SSL_NO_VERIFY"] = "1",
            ["GIT_DIR"] = "sentinel",
            ["GIT_WORK_TREE"] = "sentinel",
            ["GIT_CONFIG_PARAMETERS"] = "'http.sslVerify=false'",
        };

        GitTransportSecurity.ConfigureTransportEnvironment(
            environment,
            GitTransportMode.SecureOnly,
            "https://example.test/packs.git"
        );

        await Assert.That(environment["GIT_ALLOW_PROTOCOL"]).IsEqualTo("https:ssh");
        await Assert.That(environment).DoesNotContainKey("GIT_SSL_NO_VERIFY");
        await Assert.That(environment).DoesNotContainKey("GIT_DIR");
        await Assert.That(environment).DoesNotContainKey("GIT_WORK_TREE");
        await Assert.That(environment["GIT_LFS_SKIP_SMUDGE"]).IsEqualTo("1");
        await Assert.That(environment["GIT_CONFIG_PARAMETERS"]).IsEqualTo("'http.sslVerify=false'");
    }

    [Test]
    public async Task ConfigureTransportEnvironment_WhenRepositoryIsLocal_AllowsOnlyFile()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        GitTransportSecurity.ConfigureTransportEnvironment(
            environment,
            GitTransportMode.SecureOnly,
            OperatingSystem.IsWindows() ? @"C:\repository" : "/repository"
        );

        await Assert.That(environment["GIT_ALLOW_PROTOCOL"]).IsEqualTo("file");
    }

    [Test]
    [Arguments(".gitattributes", "pack.bin")]
    [Arguments("deep/.gitattributes", "deep/pack.bin")]
    public async Task ProcessRunner_WhenCheckoutUsesSmudgeFilter_BlocksBeforeCheckout(
        string attributesPath,
        string contentPath
    )
    {
        var repository = Path.Combine(
            Path.GetTempPath(),
            $"lunapack-git-filter-{Guid.NewGuid():N}"
        );
        var configPath = CreateGlobalConfig(string.Empty);
        try
        {
            var (initializedExitCode, _, _) = await RunGitAsync(configPath, ["init", repository]);
            await Assert.That(initializedExitCode).IsEqualTo(0);
            var attributesFile = Path.Combine(repository, attributesPath);
            var contentFile = Path.Combine(repository, contentPath);
            Directory.CreateDirectory(Path.GetDirectoryName(attributesFile) ?? repository);
            Directory.CreateDirectory(Path.GetDirectoryName(contentFile) ?? repository);
            await File.WriteAllTextAsync(
                attributesFile,
                "*.bin filter=lfs diff=lfs merge=lfs -text\n"
            );
            await File.WriteAllTextAsync(contentFile, "content");
            await RunGitAsync(configPath, ["-C", repository, "add", "."]);
            var (committedExitCode, _, _) = await RunGitAsync(
                configPath,
                [
                    "-c",
                    "user.name=Lunapack Test",
                    "-c",
                    "user.email=lunapack@example.test",
                    "-C",
                    repository,
                    "commit",
                    "-m",
                    "fixture",
                ]
            );
            await Assert.That(committedExitCode).IsEqualTo(0);
            await RunGitAsync(configPath, ["-C", repository, "fetch", ".", "HEAD"]);
            File.Delete(contentFile);

            var result = await new GitProcessRunner().RunAsync(
                ["-C", repository, "checkout", "--quiet", "FETCH_HEAD"],
                TimeSpan.FromSeconds(10),
                GitTransportMode.SecureOnly,
                repository,
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("smudge filter");
            await Assert.That(File.Exists(contentFile)).IsFalse();
        }
        finally
        {
            File.Delete(configPath);
            DeleteRepository(repository);
        }
    }

    [Test]
    [Arguments("hostile")]
    [Arguments("unset")]
    [Arguments("unspecified")]
    public async Task ProcessRunner_WhenGlobalAttributesSelectSmudgeFilter_BlocksCheckout(
        string filterName
    )
    {
        var repository = Path.Combine(
            Path.GetTempPath(),
            $"lunapack-git-global-filter-{Guid.NewGuid():N}"
        );
        var attributesPath = Path.Combine(
            Path.GetTempPath(),
            $"lunapack-git-{Guid.NewGuid():N}.gitattributes"
        );
        var configPath = CreateGlobalConfig(
            $"[core]\n\tattributesFile = {attributesPath.Replace('\\', '/')}\n"
                + $"[filter \"{filterName}\"]\n\tsmudge = cat\n"
        );
        var previousGlobalConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        var previousNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
            File.WriteAllText(attributesPath, $"*.bin filter={filterName}\n");
            var (initializedExitCode, _, _) = await RunGitAsync(configPath, ["init", repository]);
            await Assert.That(initializedExitCode).IsEqualTo(0);
            var contentFile = Path.Combine(repository, "pack.bin");
            await File.WriteAllTextAsync(contentFile, "content");
            await RunGitAsync(configPath, ["-C", repository, "add", "."]);
            await RunGitAsync(
                configPath,
                [
                    "-c",
                    "user.name=Lunapack Test",
                    "-c",
                    "user.email=lunapack@example.test",
                    "-C",
                    repository,
                    "commit",
                    "-m",
                    "fixture",
                ]
            );
            await RunGitAsync(configPath, ["-C", repository, "fetch", ".", "HEAD"]);
            File.Delete(contentFile);

            var result = await new GitProcessRunner().RunAsync(
                ["-C", repository, "checkout", "--quiet", "FETCH_HEAD"],
                TimeSpan.FromSeconds(10),
                GitTransportMode.SecureOnly,
                repository,
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("filter");
            await Assert.That(File.Exists(contentFile)).IsFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobalConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", previousNoSystem);
            File.Delete(configPath);
            File.Delete(attributesPath);
            DeleteRepository(repository);
        }
    }

    [Test]
    public async Task ProcessRunner_WhenCheckoutHasNoFilter_MaterializesFromRealIndex()
    {
        var sourceRepository = Path.Combine(
            Path.GetTempPath(),
            $"lunapack-git-checkout-source-{Guid.NewGuid():N}"
        );
        var checkoutRepository = Path.Combine(
            Path.GetTempPath(),
            $"lunapack-git-checkout-target-{Guid.NewGuid():N}"
        );
        var configPath = CreateGlobalConfig(string.Empty);
        try
        {
            await RunGitAsync(configPath, ["init", sourceRepository]);
            var sourcePath = Path.Combine(sourceRepository, "pack.txt");
            await File.WriteAllTextAsync(sourcePath, "content");
            await RunGitAsync(configPath, ["-C", sourceRepository, "add", "."]);
            await RunGitAsync(
                configPath,
                [
                    "-c",
                    "user.name=Lunapack Test",
                    "-c",
                    "user.email=lunapack@example.test",
                    "-C",
                    sourceRepository,
                    "commit",
                    "-m",
                    "fixture",
                ]
            );
            await RunGitAsync(configPath, ["init", checkoutRepository]);
            await RunGitAsync(
                configPath,
                ["-C", checkoutRepository, "fetch", sourceRepository, "HEAD"]
            );
            var contentPath = Path.Combine(checkoutRepository, "pack.txt");

            var result = await new GitProcessRunner().RunAsync(
                ["-C", checkoutRepository, "checkout", "--quiet", "FETCH_HEAD"],
                TimeSpan.FromSeconds(10),
                GitTransportMode.SecureOnly,
                sourceRepository,
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
            await Assert.That(await File.ReadAllTextAsync(contentPath)).IsEqualTo("content");
        }
        finally
        {
            File.Delete(configPath);
            DeleteRepository(sourceRepository);
            DeleteRepository(checkoutRepository);
        }
    }

    private static string CreateGlobalConfig(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lunapack-git-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, content);
        return path;
    }

    private static void DeleteRepository(string repository)
    {
        if (!Directory.Exists(repository))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(repository, recursive: true);
    }

    private static async Task<(
        int ExitCode,
        string StandardOutput,
        string StandardError
    )> RunGitAsync(
        string globalConfigPath,
        IReadOnlyList<string> arguments,
        GitTransportMode? transportMode = null,
        string? repositoryUrl = null,
        string? standardInput = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null
    )
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = globalConfigPath;
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_COUNT"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (environmentVariables is not null)
        {
            foreach (var (name, value) in environmentVariables)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }
        if (transportMode is { } mode)
        {
            GitTransportSecurity.ConfigureTransportEnvironment(
                startInfo.Environment,
                mode,
                repositoryUrl
            );
            var error = GitTransportSecurity.ConfigureSecureHttps(
                startInfo.Environment,
                mode,
                repositoryUrl
            );
            if (error is not null)
            {
                throw new InvalidOperationException(error);
            }
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process =
            Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start Git.");

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync();
        return (process.ExitCode, await standardOutput, await standardError);
    }
}
