using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace SnapStudio.CommandPalette;

[Guid("76B2BC17-A2A2-41A7-A9B4-421145E05913")]
public sealed partial class SnapStudioExtension(ManualResetEvent disposed) : IExtension, IDisposable
{
    private readonly SnapStudioCommandsProvider _provider = new();
    public object? GetProvider(ProviderType providerType) => providerType == ProviderType.Commands ? _provider : null;
    public void Dispose() => disposed.Set();
}
