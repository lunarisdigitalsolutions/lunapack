using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Lunapack.Cli.Trust;

internal static class UserSettingsPathSecurity
{
    private const UnixFileMode DirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode NonUserMode =
        UnixFileMode.GroupRead
        | UnixFileMode.GroupWrite
        | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead
        | UnixFileMode.OtherWrite
        | UnixFileMode.OtherExecute;

    public static string? ValidateExisting(IFileSystem fileSystem, string path, bool directory)
    {
        if (fileSystem.File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            return $"User settings path '{path}' cannot be a link or reparse point.";
        }

        return OperatingSystem.IsWindows()
            ? ValidateWindows(path, directory)
            : ValidateUnix(path, directory);
    }

    public static void Apply(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyWindows(path, directory);
            return;
        }

        File.SetUnixFileMode(path, directory ? DirectoryMode : FileMode);
    }

    [UnsupportedOSPlatform("windows")]
    private static string? ValidateUnix(string path, bool directory)
    {
        var mode = File.GetUnixFileMode(path);
        var requiredMode = directory ? DirectoryMode : FileMode;
        return (mode & NonUserMode) == UnixFileMode.None && (mode & requiredMode) == requiredMode
            ? null
            : $"User settings path '{path}' must be accessible only by its owner.";
    }

    [SupportedOSPlatform("windows")]
    private static string? ValidateWindows(string path, bool directory)
    {
        var identity = GetWindowsIdentity();
        FileSystemSecurity security = directory
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        var hasUnsafeAccessControl =
            !security.AreAccessRulesProtected
            || security.GetOwner(typeof(SecurityIdentifier)) != identity;
        if (hasUnsafeAccessControl)
        {
            return $"User settings path '{path}' must be owned only by the current user.";
        }

        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            typeof(SecurityIdentifier)
        );
        return rules
            .OfType<FileSystemAccessRule>()
            .All(rule => rule.IdentityReference.Equals(identity))
            ? null
            : $"User settings path '{path}' grants access to another identity.";
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindows(string path, bool directory)
    {
        var identity = GetWindowsIdentity();
        FileSystemSecurity security = directory ? new DirectorySecurity() : new FileSecurity();
        security.SetOwner(identity);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inheritanceFlags = directory
            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
            : InheritanceFlags.None;
        security.AddAccessRule(
            new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                inheritanceFlags,
                PropagationFlags.None,
                AccessControlType.Allow
            )
        );

        if (directory)
        {
            new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);
        }
        else
        {
            new FileInfo(path).SetAccessControl((FileSecurity)security);
        }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier GetWindowsIdentity() =>
        WindowsIdentity.GetCurrent().User
        ?? throw new UnauthorizedAccessException(
            "Current Windows user has no security identifier."
        );
}
