using System.Globalization;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed record WindowsScrollTargetDescriptor(
    string RuntimeId,
    string Name,
    string AutomationId,
    string ClassName,
    string ControlTypeProgrammaticName,
    int ProcessId,
    int NativeWindowHandle,
    RectD Bounds,
    bool IsVerticallyScrollable,
    bool IsHorizontallyScrollable,
    double VerticalScrollPercent,
    double HorizontalScrollPercent);

public static class WindowsScrollTargetCandidateFactory
{
    public static bool TryCreate(
        WindowsScrollTargetDescriptor descriptor,
        out ScrollTargetCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        candidate = null;
        if (descriptor.Bounds.Width <= 1
            || descriptor.Bounds.Height <= 1
            || (!descriptor.IsVerticallyScrollable && !descriptor.IsHorizontallyScrollable))
        {
            return false;
        }

        string id = CreateCandidateId(descriptor);
        string displayName = string.IsNullOrWhiteSpace(descriptor.Name)
            ? ResolveDisplayName(descriptor)
            : descriptor.Name.Trim();
        var metadata = new Dictionary<string, string>
        {
            ["runtimeId"] = descriptor.RuntimeId,
            ["automationId"] = descriptor.AutomationId,
            ["className"] = descriptor.ClassName,
            ["controlType"] = descriptor.ControlTypeProgrammaticName,
            ["processId"] = descriptor.ProcessId.ToString(CultureInfo.InvariantCulture),
            ["nativeWindowHandle"] = descriptor.NativeWindowHandle.ToString(CultureInfo.InvariantCulture),
            ["isVerticallyScrollable"] = descriptor.IsVerticallyScrollable.ToString(CultureInfo.InvariantCulture),
            ["isHorizontallyScrollable"] = descriptor.IsHorizontallyScrollable.ToString(CultureInfo.InvariantCulture),
            ["verticalScrollPercent"] = descriptor.VerticalScrollPercent.ToString(CultureInfo.InvariantCulture),
            ["horizontalScrollPercent"] = descriptor.HorizontalScrollPercent.ToString(CultureInfo.InvariantCulture)
        };

        candidate = new ScrollTargetCandidate(
            id,
            displayName,
            ResolveTargetKind(descriptor),
            descriptor.Bounds,
            metadata);
        return true;
    }

    public static bool MatchesRequest(
        ScrollTargetCandidate candidate,
        ScrollTargetDetectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(request);

        if (request.SearchBounds is RectD searchBounds
            && !Intersects(candidate.Bounds, searchBounds))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.TargetHint))
        {
            return true;
        }

        string hint = request.TargetHint.Trim();
        return candidate.Id.Contains(hint, StringComparison.OrdinalIgnoreCase)
            || candidate.DisplayName.Contains(hint, StringComparison.OrdinalIgnoreCase)
            || candidate.Metadata.Values.Any(value => value.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateCandidateId(WindowsScrollTargetDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(descriptor.RuntimeId))
        {
            return $"uia:{descriptor.RuntimeId}";
        }

        if (descriptor.NativeWindowHandle != 0)
        {
            return $"hwnd:{descriptor.NativeWindowHandle.ToString(CultureInfo.InvariantCulture)}";
        }

        if (!string.IsNullOrWhiteSpace(descriptor.AutomationId))
        {
            return $"automation:{descriptor.ProcessId.ToString(CultureInfo.InvariantCulture)}:{descriptor.AutomationId}";
        }

        return $"process:{descriptor.ProcessId.ToString(CultureInfo.InvariantCulture)}:{descriptor.Bounds.X.ToString(CultureInfo.InvariantCulture)}:{descriptor.Bounds.Y.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string ResolveDisplayName(WindowsScrollTargetDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(descriptor.AutomationId))
        {
            return descriptor.AutomationId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(descriptor.ClassName))
        {
            return descriptor.ClassName.Trim();
        }

        return "Scrollable target";
    }

    private static ScrollTargetKind ResolveTargetKind(WindowsScrollTargetDescriptor descriptor)
    {
        string controlType = descriptor.ControlTypeProgrammaticName;
        string className = descriptor.ClassName;

        if (controlType.EndsWith(".Window", StringComparison.OrdinalIgnoreCase))
        {
            return ScrollTargetKind.Window;
        }

        if (controlType.EndsWith(".Document", StringComparison.OrdinalIgnoreCase))
        {
            return ScrollTargetKind.DocumentViewer;
        }

        if (className.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Mozilla", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Internet Explorer_Server", StringComparison.OrdinalIgnoreCase)
            || className.Contains("WebView", StringComparison.OrdinalIgnoreCase))
        {
            return ScrollTargetKind.Browser;
        }

        return ScrollTargetKind.Control;
    }

    private static bool Intersects(RectD first, RectD second)
    {
        double left = Math.Max(first.X, second.X);
        double top = Math.Max(first.Y, second.Y);
        double right = Math.Min(first.Right, second.Right);
        double bottom = Math.Min(first.Bottom, second.Bottom);

        return right > left && bottom > top;
    }
}
