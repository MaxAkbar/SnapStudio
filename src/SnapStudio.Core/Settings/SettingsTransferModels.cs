namespace SnapStudio.Core.Settings;

public enum SettingsTransferFailureReason
{
    InvalidPath,
    InvalidJson,
    FileUnavailable,
    PermissionDenied,
    Unknown
}

public sealed record SettingsTransferFailure(
    SettingsTransferFailureReason Reason,
    string Message,
    Exception? Exception = null);

public sealed record SettingsExportResult(SettingsTransferFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static SettingsExportResult Success()
    {
        return new SettingsExportResult((SettingsTransferFailure?)null);
    }

    public static SettingsExportResult Failed(SettingsTransferFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new SettingsExportResult(failure);
    }
}

public sealed record SettingsImportResult(
    ApplicationSettings? Settings,
    SettingsTransferFailure? Failure)
{
    public bool Succeeded => Settings is not null && Failure is null;

    public static SettingsImportResult Success(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SettingsImportResult(settings, null);
    }

    public static SettingsImportResult Failed(SettingsTransferFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new SettingsImportResult(null, failure);
    }
}
