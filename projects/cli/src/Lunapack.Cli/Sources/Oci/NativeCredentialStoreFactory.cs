namespace Lunapack.Cli.Sources.Oci;

internal static class NativeCredentialStoreFactory
{
    public static INativeCredentialBackend Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialBackend();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainCredentialBackend();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretServiceCredentialBackend();
        }

        throw new PlatformNotSupportedException(
            "The operating-system credential store is not supported on this platform."
        );
    }
}
