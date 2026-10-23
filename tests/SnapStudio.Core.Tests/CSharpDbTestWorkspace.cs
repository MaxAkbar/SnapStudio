using CSharpDB.Data;
using SnapStudio.Storage;

namespace SnapStudio.Core.Tests;

internal sealed class CSharpDbTestWorkspace : IDisposable
{
    private readonly TemporaryWorkspace _workspace = TemporaryWorkspace.Create();
    private bool _preserveFiles;

    public string Path => _workspace.Path;

    public static CSharpDbTestWorkspace Create() => new();

    public void PreserveEvidence() => _preserveFiles = true;

    public void Dispose()
    {
        var builder = new CSharpDbConnectionStringBuilder
        {
            DataSource = new CSharpDbDocumentStore(Path).DatabasePath,
            Pooling = true
        };
        // Retire only this test's pool before deleting its temporary files.
        CSharpDbConnection.ClearPool(builder.ConnectionString);
        if (!_preserveFiles)
        {
            _workspace.Dispose();
        }
    }
}
