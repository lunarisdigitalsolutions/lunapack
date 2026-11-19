using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Sbom;

internal static class SbomSourceUrlSanitizer
{
    private const string RedactedMalformed = "[redacted-malformed]";

    private static readonly HashSet<string> _supportedHierarchicalSchemes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "file",
        "git",
        "http",
        "https",
        "ssh",
    };

    public static string SanitizeFingerprint(string value, string? reference = null)
    {
        const string prefix = "git:";
        const string redacted = $"git:{RedactedMalformed}";
        if (value.StartsWith("local:", StringComparison.Ordinal))
        {
            return SanitizeLocalFingerprint(value);
        }

        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !GitRefSafety.IsSafe(reference))
        {
            return redacted;
        }

        var sourcePathSeparator = value.IndexOf('#', prefix.Length);
        if (sourcePathSeparator < 0 || !IsCanonicalSourcePath(value[(sourcePathSeparator + 1)..]))
        {
            return redacted;
        }

        var fingerprint = value[prefix.Length..sourcePathSeparator];
        var referenceSuffix = $"@{reference}";
        if (
            fingerprint.Length <= referenceSuffix.Length
            || !fingerprint.EndsWith(referenceSuffix, StringComparison.Ordinal)
        )
        {
            return redacted;
        }

        var repository = fingerprint[..^referenceSuffix.Length];
        var sanitizedRepository = Sanitize(repository);
        return
            sanitizedRepository.Length == 0
            || string.Equals(sanitizedRepository, RedactedMalformed, StringComparison.Ordinal)
            ? redacted
            : $"{prefix}{sanitizedRepository}{referenceSuffix}{value[sourcePathSeparator..]}";
    }

    public static string Sanitize(string value)
    {
        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (_supportedHierarchicalSchemes.Contains(uri.Scheme))
            {
                if (!HasValidAuthority(uri))
                {
                    return RedactedMalformed;
                }

                return new UriBuilder(uri)
                {
                    UserName = string.Empty,
                    Password = string.Empty,
                    Query = string.Empty,
                    Fragment = string.Empty,
                }
                    .Uri.GetLeftPart(UriPartial.Path)
                    .TrimEnd('/');
            }
        }

        if (trimmed.ContainsAny(['?', '#']))
        {
            return RedactedMalformed;
        }

        var prefixEnd = trimmed.IndexOf(':');
        if (prefixEnd >= 0 && _supportedHierarchicalSchemes.Contains(trimmed[..prefixEnd]))
        {
            return RedactedMalformed;
        }

        if (
            trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.Contains("://", StringComparison.Ordinal)
        )
        {
            return RedactedMalformed;
        }

        if (TrySanitizeScpLocation(trimmed, out var scpLocation))
        {
            return scpLocation;
        }

        return IsSafeNormalizedRepository(trimmed) ? trimmed : RedactedMalformed;
    }

    private static bool TrySanitizeScpLocation(string value, out string sanitized)
    {
        var userSeparator = value.IndexOf('@');
        if (userSeparator >= 0)
        {
            if (userSeparator != value.LastIndexOf('@') || value[..userSeparator].Contains(':'))
            {
                sanitized = string.Empty;
                return false;
            }

            var candidate = value[(userSeparator + 1)..];
            if (!IsValidScpLocation(candidate))
            {
                sanitized = string.Empty;
                return false;
            }

            sanitized = candidate;
            return true;
        }

        sanitized = IsValidScpLocation(value) && !value.Contains('@') ? value : string.Empty;
        return sanitized.Length > 0;
    }

    private static bool IsValidScpLocation(string value)
    {
        var separator = value.StartsWith('[')
            ? value.IndexOf(':', value.IndexOf(']') + 1)
            : value.IndexOf(':');
        var host = separator > 0 ? value[..separator] : string.Empty;
        var path = separator >= 0 ? value[(separator + 1)..] : string.Empty;
        return IsValidHost(host) && path.Length > 0 && !ContainsUnsafeRepositoryCharacter(path);
    }

    private static bool IsValidHost(string hostWithOptionalPort)
    {
        string host;
        if (hostWithOptionalPort.StartsWith('['))
        {
            var end = hostWithOptionalPort.IndexOf(']');
            if (end <= 1)
            {
                return false;
            }

            host = hostWithOptionalPort[1..end];
            var suffix = hostWithOptionalPort[(end + 1)..];
            if (
                suffix.Length > 0
                && (
                    !suffix.StartsWith(':')
                    || suffix.Length == 1
                    || !suffix[1..].All(char.IsAsciiDigit)
                )
            )
            {
                return false;
            }

            return Uri.CheckHostName(host) == UriHostNameType.IPv6;
        }

        var portSeparator = hostWithOptionalPort.LastIndexOf(':');
        if (portSeparator > 0)
        {
            host = hostWithOptionalPort[..portSeparator];
            var port = hostWithOptionalPort[(portSeparator + 1)..];
            if (port.Length == 0 || !port.All(char.IsAsciiDigit))
            {
                return false;
            }
        }
        else
        {
            host = hostWithOptionalPort;
        }

        return Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4;
    }

    private static bool IsSafeNormalizedRepository(string value)
    {
        if (
            value.Length >= 3
            && char.IsAsciiLetter(value[0])
            && value[1] == ':'
            && value[2] is '/' or '\\'
        )
        {
            return !value.ContainsAny(['?', '#']) && !value.Any(char.IsControl);
        }

        if (
            value.StartsWith('/')
            && !value.StartsWith("//", StringComparison.Ordinal)
            && !value.ContainsAny(['?', '#', '\\'])
            && !value.Any(char.IsControl)
        )
        {
            return true;
        }

        if (value.ContainsAny(['?', '#', '\\']) || value.Any(char.IsControl))
        {
            return false;
        }

        var hostSeparator = value.IndexOf('/');
        var repositoryPath = hostSeparator >= 0 ? value[(hostSeparator + 1)..] : string.Empty;
        return hostSeparator > 0
            && hostSeparator < value.Length - 1
            && IsValidHost(value[..hostSeparator])
            && !repositoryPath.ContainsAny(['@', ':'])
            && !repositoryPath.Contains("//", StringComparison.Ordinal);
    }

    private static bool IsCanonicalSourcePath(string path)
    {
        if (string.Equals(path, "/", StringComparison.Ordinal))
        {
            return true;
        }

        if (
            !path.StartsWith('/')
            || path.EndsWith('/')
            || path.ContainsAny(['?', '#', '\\'])
            || path.Any(char.IsControl)
        )
        {
            return false;
        }

        return path[1..]
            .Split('/')
            .All(segment =>
                segment.Length > 0
                && !string.Equals(segment, ".", StringComparison.Ordinal)
                && !string.Equals(segment, "..", StringComparison.Ordinal)
            );
    }

    private static bool ContainsUnsafeRepositoryCharacter(string value) =>
        value.ContainsAny(['?', '#', '\\', '@', ':']) || value.Any(char.IsControl);

    private static string SanitizeLocalFingerprint(string value)
    {
        var path = value["local:".Length..];
        if (
            path.Length == 0
            || path.Contains('@')
            || path.ContainsAny(['?', '#'])
            || path.Contains("://", StringComparison.Ordinal)
            || path.Any(char.IsControl)
        )
        {
            return $"local:{RedactedMalformed}";
        }

        return value;
    }

    private static bool HasValidAuthority(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)
        || uri.HostNameType is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
}
