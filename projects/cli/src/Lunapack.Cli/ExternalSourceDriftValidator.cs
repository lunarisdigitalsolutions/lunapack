namespace Lunapack.Cli;

internal static class ExternalSourceDriftValidator
{
    public static ManifestOperationResult<bool> Validate(ProjectState state)
    {
        foreach (var pack in state.LockFile.Packs)
        {
            foreach (var (alias, lockedSource) in pack.ExternalSources)
            {
                var configuredSource = state.Configuration.Sources.SingleOrDefault(source =>
                    string.Equals(source.Name, lockedSource.SourceName, StringComparison.Ordinal)
                );
                if (configuredSource is null)
                {
                    return ManifestOperationResult<bool>.Failure(
                        $"Pack '{pack.Id}' locked external source '{alias}' maps to missing configured source '{lockedSource.SourceName}'."
                    );
                }

                var configuredFingerprint = SourceIdentityNormalizer.Create(configuredSource);
                if (configuredFingerprint.Value is not { } fingerprint)
                {
                    return ManifestOperationResult<bool>.Failure(
                        configuredFingerprint.Error
                            ?? $"Unable to normalize configured source '{lockedSource.SourceName}'."
                    );
                }

                if (
                    !string.Equals(
                        lockedSource.Fingerprint,
                        fingerprint.Value,
                        StringComparison.Ordinal
                    )
                )
                {
                    return ManifestOperationResult<bool>.Failure(
                        $"Pack '{pack.Id}' locked external source '{alias}' fingerprint '{lockedSource.Fingerprint}' does not match configured source '{lockedSource.SourceName}' fingerprint '{fingerprint.Value}'. Source identity drift requires explicit acceptance."
                    );
                }
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }
}
