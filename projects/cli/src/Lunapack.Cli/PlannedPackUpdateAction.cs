using System.Security.Cryptography;

namespace Lunapack.Cli;

internal abstract record PlannedPackUpdateAction(
    string TargetPath,
    string TargetPathRelativeToProject,
    byte[]? ResultingContents
)
{
    public string? ResultingSha256 =>
        ResultingContents is null ? null : Convert.ToHexString(SHA256.HashData(ResultingContents));
}
