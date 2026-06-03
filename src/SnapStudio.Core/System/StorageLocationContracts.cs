namespace SnapStudio.Core.System;

public interface IStorageLocationPicker
{
    Task<string?> PickStorageRootAsync(
        string currentStorageRoot,
        CancellationToken cancellationToken);
}

public interface ISettingsFilePicker
{
    Task<string?> PickSettingsExportPathAsync(
        string suggestedFileName,
        CancellationToken cancellationToken);

    Task<string?> PickSettingsImportPathAsync(CancellationToken cancellationToken);
}
