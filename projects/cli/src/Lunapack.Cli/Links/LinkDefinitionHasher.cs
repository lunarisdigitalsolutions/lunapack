using System.Security.Cryptography;
using System.Text;

namespace Lunapack.Cli;

internal static class LinkDefinitionHasher
{
    private static readonly UTF8Encoding _utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    public static string ComputeSha256(string name, ProjectConfiguration.Link link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var projection = new StringBuilder();
        AppendField(projection, "name", name);
        AppendField(projection, "source", link.Source);
        AppendField(projection, "path", NormalizeSelectorPath(link.Path));
        AppendField(projection, "target", NormalizeSelectorPath(link.Target));
        AppendField(projection, "stripPrefix", NormalizeSelectorPath(link.StripPrefix));
        AppendField(projection, "ref", link.Ref ?? string.Empty);
        AppendField(projection, "flatten", link.Flatten is true ? "true" : "false");
        AppendSelectors(projection, "includes", link.Includes);
        AppendSelectors(projection, "excludes", link.Excludes);

        return Convert.ToHexString(SHA256.HashData(_utf8.GetBytes(projection.ToString())));
    }

    private static void AppendSelectors(
        StringBuilder projection,
        string field,
        IReadOnlyList<string> selectors
    )
    {
        var canonicalSelectors = selectors
            .Select(NormalizeSelector)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var selector in canonicalSelectors)
        {
            AppendField(projection, field, selector);
        }
    }

    private static void AppendField(StringBuilder projection, string field, string value) =>
        projection.Append(field).Append('\u001f').Append(value).Append('\u001e');

    private static string NormalizeSelector(string selector) =>
        ProjectPath.Normalize(selector).Trim('/');

    private static string NormalizeSelectorPath(string? path) =>
        path is null ? string.Empty : NormalizeSelector(path);
}
