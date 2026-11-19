namespace Lunapack.Cli.Packs.ContentScreening;

internal static class PackContentScreeningPolicy
{
    public const string AllowOption = "--allow-hidden-characters";

    public const string BlockingMessage =
        "Pack content contains high-confidence hidden characters. Review the findings and use "
        + AllowOption
        + " to continue.";

    public static bool Blocks(bool hasHighConfidenceFinding, bool allowHiddenCharacters) =>
        hasHighConfidenceFinding && !allowHiddenCharacters;
}
