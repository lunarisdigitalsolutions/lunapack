using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class LifecycleCommandResolver(IFileSystem fileSystem)
{
    public ManifestOperationResult<ResolvedLifecycleHookInvocation> Resolve(
        LifecycleHookInvocation invocation
    )
    {
        var executable = ResolveExecutable(invocation.DeclaredExecutable);
        return executable.Value is { } path
            ? ManifestOperationResult<ResolvedLifecycleHookInvocation>.Success(
                new ResolvedLifecycleHookInvocation(invocation, path)
            )
            : ManifestOperationResult<ResolvedLifecycleHookInvocation>.Failure(
                executable.Error ?? "Unable to resolve lifecycle hook executable."
            );
    }

    private ManifestOperationResult<string> ResolveExecutable(string executable)
    {
        if (fileSystem.Path.IsPathRooted(executable))
        {
            return fileSystem.File.Exists(executable)
                ? ManifestOperationResult<string>.Success(fileSystem.Path.GetFullPath(executable))
                : ManifestOperationResult<string>.Failure(
                    $"Lifecycle hook executable '{executable}' does not exist."
                );
        }

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(
            fileSystem.Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        foreach (var directory in pathDirectories)
        {
            foreach (var candidate in GetCandidates(directory, executable))
            {
                if (fileSystem.File.Exists(candidate))
                {
                    return ManifestOperationResult<string>.Success(
                        fileSystem.Path.GetFullPath(candidate)
                    );
                }
            }
        }

        return ManifestOperationResult<string>.Failure(
            $"Lifecycle hook executable '{executable}' was not found on PATH."
        );
    }

    private IEnumerable<string> GetCandidates(string directory, string executable)
    {
        if (!OperatingSystem.IsWindows() || fileSystem.Path.HasExtension(executable))
        {
            yield return fileSystem.Path.Combine(directory, executable);
            yield break;
        }

        var pathExtensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';');
        foreach (var extension in pathExtensions)
        {
            if (!string.IsNullOrEmpty(extension))
            {
                yield return fileSystem.Path.Combine(directory, $"{executable}{extension}");
            }
        }
    }
}
