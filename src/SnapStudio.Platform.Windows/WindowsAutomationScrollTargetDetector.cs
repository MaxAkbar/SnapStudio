using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsAutomationScrollTargetDetector : IScrollTargetDetector
{
    private readonly int _maximumTargets;

    public WindowsAutomationScrollTargetDetector(int maximumTargets = 50)
    {
        _maximumTargets = Math.Clamp(maximumTargets, 1, 200);
    }

    public Task<IReadOnlyList<ScrollTargetCandidate>> DetectAsync(
        ScrollTargetDetectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => DetectCore(request, cancellationToken),
            cancellationToken);
    }

    private IReadOnlyList<ScrollTargetCandidate> DetectCore(
        ScrollTargetDetectionRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ScrollTargetCandidate>();

        AutomationElementCollection elements;
        try
        {
            elements = AutomationElement.RootElement.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.IsScrollPatternAvailableProperty,
                    true));
        }
        catch (ElementNotAvailableException)
        {
            return candidates;
        }
        catch (InvalidOperationException)
        {
            return candidates;
        }
        catch (COMException)
        {
            return candidates;
        }

        foreach (AutomationElement element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryCreateCandidate(element, out ScrollTargetCandidate? candidate)
                && candidate is not null
                && WindowsScrollTargetCandidateFactory.MatchesRequest(candidate, request)
                && candidates.All(existing => !string.Equals(existing.Id, candidate.Id, StringComparison.Ordinal)))
            {
                candidates.Add(candidate);
                if (candidates.Count >= _maximumTargets)
                {
                    break;
                }
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.X)
            .ToArray();
    }

    private static bool TryCreateCandidate(
        AutomationElement element,
        out ScrollTargetCandidate? candidate)
    {
        candidate = null;

        try
        {
            if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out object? patternObject)
                || patternObject is not ScrollPattern scrollPattern)
            {
                return false;
            }

            ScrollPattern.ScrollPatternInformation scroll = scrollPattern.Current;
            RectD bounds = ToRectD(element.Current.BoundingRectangle);
            var descriptor = new WindowsScrollTargetDescriptor(
                CreateRuntimeId(element),
                SafeRead(() => element.Current.Name),
                SafeRead(() => element.Current.AutomationId),
                SafeRead(() => element.Current.ClassName),
                SafeRead(() => element.Current.ControlType.ProgrammaticName),
                SafeRead(() => element.Current.ProcessId),
                SafeRead(() => element.Current.NativeWindowHandle),
                bounds,
                scroll.VerticallyScrollable,
                scroll.HorizontallyScrollable,
                scroll.VerticalScrollPercent,
                scroll.HorizontalScrollPercent);

            return WindowsScrollTargetCandidateFactory.TryCreate(descriptor, out candidate);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static RectD ToRectD(Rect rectangle)
    {
        if (rectangle.IsEmpty
            || double.IsInfinity(rectangle.Width)
            || double.IsInfinity(rectangle.Height)
            || double.IsNaN(rectangle.Width)
            || double.IsNaN(rectangle.Height))
        {
            return new RectD(0, 0, 0, 0);
        }

        return new RectD(
            rectangle.X,
            rectangle.Y,
            Math.Max(0, rectangle.Width),
            Math.Max(0, rectangle.Height));
    }

    private static string CreateRuntimeId(AutomationElement element)
    {
        try
        {
            int[] runtimeId = element.GetRuntimeId();
            return runtimeId.Length == 0
                ? string.Empty
                : string.Join(".", runtimeId);
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string SafeRead(Func<string?> read)
    {
        try
        {
            return read()?.Trim() ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static int SafeRead(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (ElementNotAvailableException)
        {
            return 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}
