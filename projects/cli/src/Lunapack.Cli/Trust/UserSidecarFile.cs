using System.IO.Abstractions;

namespace Lunapack.Cli.Trust;

internal sealed class UserSidecarFile
{
    private readonly string _description;
    private readonly IFileSystem _fileSystem;

    public UserSidecarFile(
        IFileSystem fileSystem,
        string settingsDirectory,
        string fileName,
        string description
    )
    {
        _fileSystem = fileSystem;
        _description = description;
        SettingsDirectory = fileSystem.Path.GetFullPath(settingsDirectory);
        SettingsPath = fileSystem.Path.Combine(SettingsDirectory, fileName);
    }

    public string SettingsDirectory { get; }

    public string SettingsPath { get; }

    public bool Exists() => _fileSystem.File.Exists(SettingsPath);

    public string? ValidateExistingPaths()
    {
        if (_fileSystem.File.Exists(SettingsDirectory))
        {
            return $"{_description} path '{SettingsDirectory}' must be a directory.";
        }

        if (_fileSystem.Directory.Exists(SettingsDirectory))
        {
            var directoryError = UserSettingsPathSecurity.ValidateExisting(
                _fileSystem,
                SettingsDirectory,
                directory: true
            );
            if (directoryError is not null)
            {
                return directoryError;
            }
        }

        if (_fileSystem.Directory.Exists(SettingsPath))
        {
            return $"{_description} path '{SettingsPath}' must be a regular file.";
        }

        return _fileSystem.File.Exists(SettingsPath)
            ? UserSettingsPathSecurity.ValidateExisting(_fileSystem, SettingsPath, directory: false)
            : null;
    }

    public string ReadAllText() => _fileSystem.File.ReadAllText(SettingsPath);

    public void WriteAllText(string content)
    {
        var temporaryPath = _fileSystem.Path.Combine(
            SettingsDirectory,
            $".{_fileSystem.Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp"
        );
        try
        {
            EnsureSettingsDirectory();
            _fileSystem.File.WriteAllText(temporaryPath, content);
            UserSettingsPathSecurity.Apply(temporaryPath, directory: false);
            _fileSystem.File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (_fileSystem.File.Exists(temporaryPath))
            {
                _fileSystem.File.Delete(temporaryPath);
            }
        }
    }

    private void EnsureSettingsDirectory()
    {
        if (!_fileSystem.Directory.Exists(SettingsDirectory))
        {
            _fileSystem.Directory.CreateDirectory(SettingsDirectory);
            UserSettingsPathSecurity.Apply(SettingsDirectory, directory: true);
        }
    }
}
