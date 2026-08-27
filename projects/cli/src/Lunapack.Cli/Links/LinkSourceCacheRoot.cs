using System.IO.Abstractions;

namespace Lunapack.Cli;

internal static class LinkSourceCacheRoot
{
    public static string Resolve(IFileSystem fileSystem) =>
        Resolve(
            fileSystem,
            CurrentPlatform(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
        );

    public static string Resolve(
        IFileSystem fileSystem,
        LinkCachePlatform platform,
        string localApplicationData,
        string userProfile,
        string? xdgCacheHome
    )
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        return platform switch
        {
            LinkCachePlatform.Windows => fileSystem.Path.Combine(
                localApplicationData,
                "LunaPack",
                "cache",
                "sources"
            ),
            LinkCachePlatform.MacOs => fileSystem.Path.Combine(
                userProfile,
                "Library",
                "Caches",
                "LunaPack",
                "sources"
            ),
            _ => fileSystem.Path.Combine(
                string.IsNullOrWhiteSpace(xdgCacheHome)
                    ? fileSystem.Path.Combine(userProfile, ".cache")
                    : xdgCacheHome,
                "lunapack",
                "sources"
            ),
        };
    }

    private static LinkCachePlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return LinkCachePlatform.Windows;
        }

        return OperatingSystem.IsMacOS() ? LinkCachePlatform.MacOs : LinkCachePlatform.Linux;
    }
}
