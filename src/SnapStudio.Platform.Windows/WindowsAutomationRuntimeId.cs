using System.Globalization;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public static class WindowsAutomationRuntimeId
{
    public static bool TryGetRuntimeId(
        ScrollTargetCandidate target,
        out int[] runtimeId)
    {
        ArgumentNullException.ThrowIfNull(target);

        runtimeId = [];
        string value = target.Metadata.TryGetValue("runtimeId", out string? metadataRuntimeId)
            ? metadataRuntimeId
            : target.Id.StartsWith("uia:", StringComparison.OrdinalIgnoreCase)
                ? target.Id["uia:".Length..]
                : string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var ids = new List<int>(parts.Length);
        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                runtimeId = [];
                return false;
            }

            ids.Add(id);
        }

        runtimeId = ids.ToArray();
        return true;
    }
}
