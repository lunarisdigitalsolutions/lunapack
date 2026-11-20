using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Lunapack.Cli.Application.Preferences;

internal static class ResourceLimitYamlValidator
{
    public static string? ValidateProject(string contents)
    {
        try
        {
            var root = LoadRoot(contents);
            return root is null ? null : ValidatePreferencesResourceLimits(root);
        }
        catch (YamlException)
        {
            return null;
        }
    }

    public static string? ValidateUserSettings(string contents)
    {
        try
        {
            var root = LoadRoot(contents);
            if (root is null)
            {
                return null;
            }

            if (TryGetNode(root, "global", out var globalNode))
            {
                if (globalNode is not YamlMappingNode global)
                {
                    return "User settings 'global' must be a mapping.";
                }

                var globalError = ValidatePreferencesResourceLimits(global);
                if (globalError is not null)
                {
                    return globalError;
                }
            }

            if (TryGetNode(root, "projects", out var projectsNode))
            {
                if (projectsNode is not YamlMappingNode projects)
                {
                    return "User settings 'projects' must be a mapping.";
                }

                foreach (var projectNode in projects.Children.Values)
                {
                    if (projectNode is not YamlMappingNode project)
                    {
                        return "Each user-project setting must be a mapping.";
                    }

                    var projectError = ValidatePreferencesResourceLimits(project);
                    if (projectError is not null)
                    {
                        return projectError;
                    }
                }
            }

            return null;
        }
        catch (YamlException)
        {
            return null;
        }
    }

    private static YamlMappingNode? LoadRoot(string contents)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(contents));
        return stream.Documents.Count == 1 ? stream.Documents[0].RootNode as YamlMappingNode : null;
    }

    private static string? ValidatePreferencesResourceLimits(YamlMappingNode root)
    {
        if (!TryGetNode(root, "preferences", out var preferencesNode))
        {
            return null;
        }

        if (preferencesNode is not YamlMappingNode preferences)
        {
            return "Settings 'preferences' must be a mapping.";
        }

        if (!TryGetNode(preferences, "resourceLimits", out var resourceLimitsNode))
        {
            return null;
        }

        if (resourceLimitsNode is not YamlMappingNode resourceLimits)
        {
            return "Settings 'preferences.resourceLimits' must be a mapping.";
        }

        foreach (var keyNode in resourceLimits.Children.Keys)
        {
            if (keyNode is not YamlScalarNode { Value: { } value })
            {
                return "Resource-limit keys must be scalar names.";
            }

            if (!ResourceLimits.TryParseYamlKey(value, out _))
            {
                return $"Unknown resource-limit key '{value}' in preferences.resourceLimits.";
            }
        }

        return null;
    }

    private static bool TryGetNode(
        YamlMappingNode map,
        string key,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out YamlNode? value
    )
    {
        value = map
            .Children.Where(pair =>
                pair.Key is YamlScalarNode scalar
                && string.Equals(scalar.Value, key, StringComparison.Ordinal)
            )
            .Select(pair => pair.Value)
            .FirstOrDefault();
        return value is not null;
    }
}
