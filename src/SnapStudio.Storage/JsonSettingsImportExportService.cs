using System.Text.Json;
using SnapStudio.Core.Settings;

namespace SnapStudio.Storage;

public sealed class JsonSettingsImportExportService : ISettingsImportExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ApplicationSettings _defaultSettings;
    private readonly ISettingsMigrator _settingsMigrator;

    public JsonSettingsImportExportService(
        ApplicationSettings defaultSettings,
        ISettingsMigrator? settingsMigrator = null)
    {
        ArgumentNullException.ThrowIfNull(defaultSettings);

        _defaultSettings = defaultSettings;
        _settingsMigrator = settingsMigrator ?? new ApplicationSettingsMigrator();
    }

    public async Task<SettingsExportResult> ExportAsync(
        ApplicationSettings settings,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return SettingsExportResult.Failed(new SettingsTransferFailure(
                SettingsTransferFailureReason.InvalidPath,
                "A settings export path is required."));
        }

        try
        {
            string fullPath = Path.GetFullPath(destinationPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            ApplicationSettings migratedSettings = _settingsMigrator
                .Migrate(settings, _defaultSettings)
                .Settings;
            string json = JsonSerializer.Serialize(migratedSettings, JsonOptions);
            string temporaryPath = $"{fullPath}.tmp";

            await File
                .WriteAllTextAsync(temporaryPath, json, cancellationToken)
                .ConfigureAwait(false);

            File.Move(temporaryPath, fullPath, overwrite: true);
            return SettingsExportResult.Success();
        }
        catch (Exception exception) when (IsRecoverableFileException(exception))
        {
            return SettingsExportResult.Failed(CreateFileFailure(
                exception,
                "Settings could not be exported."));
        }
    }

    public async Task<SettingsImportResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return SettingsImportResult.Failed(new SettingsTransferFailure(
                SettingsTransferFailureReason.InvalidPath,
                "A settings import path is required."));
        }

        try
        {
            string fullPath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullPath))
            {
                return SettingsImportResult.Failed(new SettingsTransferFailure(
                    SettingsTransferFailureReason.FileUnavailable,
                    "The selected settings file was not found."));
            }

            ApplicationSettings? settings;
            await using (FileStream stream = File.OpenRead(fullPath))
            {
                settings = await JsonSerializer
                    .DeserializeAsync<ApplicationSettings>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (settings is null)
            {
                return SettingsImportResult.Failed(new SettingsTransferFailure(
                    SettingsTransferFailureReason.InvalidJson,
                    "The selected settings file did not contain valid SnapStudio settings."));
            }

            ApplicationSettings migratedSettings = _settingsMigrator
                .Migrate(settings, _defaultSettings)
                .Settings;

            return SettingsImportResult.Success(migratedSettings);
        }
        catch (JsonException exception)
        {
            return SettingsImportResult.Failed(new SettingsTransferFailure(
                SettingsTransferFailureReason.InvalidJson,
                "The selected settings file did not contain valid JSON.",
                exception));
        }
        catch (Exception exception) when (IsRecoverableFileException(exception))
        {
            return SettingsImportResult.Failed(CreateFileFailure(
                exception,
                "Settings could not be imported."));
        }
    }

    private static SettingsTransferFailure CreateFileFailure(
        Exception exception,
        string fallbackMessage)
    {
        SettingsTransferFailureReason reason = exception is UnauthorizedAccessException
            ? SettingsTransferFailureReason.PermissionDenied
            : exception is IOException
                ? SettingsTransferFailureReason.FileUnavailable
                : SettingsTransferFailureReason.Unknown;

        return new SettingsTransferFailure(
            reason,
            $"{fallbackMessage} {exception.Message}",
            exception);
    }

    private static bool IsRecoverableFileException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
    }
}
