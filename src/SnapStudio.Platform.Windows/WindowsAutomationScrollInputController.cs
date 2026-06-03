using System.Runtime.InteropServices;
using System.Windows.Automation;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsAutomationScrollInputController : IScrollInputController
{
    private const double EndTolerance = 0.01;

    public Task<ScrollInputResult> ScrollAsync(
        ScrollInputRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => ScrollCore(request, cancellationToken),
            cancellationToken);
    }

    private static ScrollInputResult ScrollCore(
        ScrollInputRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!WindowsAutomationRuntimeId.TryGetRuntimeId(request.Target, out int[] runtimeId))
        {
            return ScrollInputResult.Failed(new ScrollInputFailure(
                ScrollInputFailureReason.TargetUnavailable,
                "The selected scroll target does not contain a UI Automation runtime id."));
        }

        try
        {
            AutomationElement? element = FindElementByRuntimeId(runtimeId, cancellationToken);
            if (element is null)
            {
                return ScrollInputResult.Failed(new ScrollInputFailure(
                    ScrollInputFailureReason.TargetUnavailable,
                    "The selected scroll target could not be found in the current UI Automation tree."));
            }

            if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out object? patternObject)
                || patternObject is not ScrollPattern scrollPattern)
            {
                return ScrollInputResult.Failed(new ScrollInputFailure(
                    ScrollInputFailureReason.CannotScroll,
                    "The selected target no longer exposes UI Automation scrolling."));
            }

            ScrollPattern.ScrollPatternInformation before = scrollPattern.Current;
            if (!CanScroll(before, request.Direction))
            {
                return ScrollInputResult.Failed(
                    new ScrollInputFailure(
                        ScrollInputFailureReason.CannotScroll,
                        "The selected target cannot scroll in the requested direction."),
                    NormalizePercent(before.VerticalScrollPercent),
                    NormalizePercent(before.HorizontalScrollPercent));
            }

            (ScrollAmount horizontal, ScrollAmount vertical) = ResolveScrollAmounts(request.Direction);
            scrollPattern.Scroll(horizontal, vertical);

            cancellationToken.ThrowIfCancellationRequested();

            ScrollPattern.ScrollPatternInformation after = scrollPattern.Current;
            return ScrollInputResult.Success(
                HasReachedEnd(request.Direction, before, after),
                NormalizePercent(after.VerticalScrollPercent),
                NormalizePercent(after.HorizontalScrollPercent));
        }
        catch (ElementNotAvailableException exception)
        {
            return ScrollInputResult.Failed(new ScrollInputFailure(
                ScrollInputFailureReason.TargetUnavailable,
                "The selected scroll target is no longer available.",
                exception));
        }
        catch (InvalidOperationException exception)
        {
            return ScrollInputResult.Failed(new ScrollInputFailure(
                ScrollInputFailureReason.CannotScroll,
                "The selected scroll target could not be scrolled.",
                exception));
        }
        catch (COMException exception)
        {
            return ScrollInputResult.Failed(new ScrollInputFailure(
                ScrollInputFailureReason.Unknown,
                $"UI Automation scrolling failed: {exception.Message}",
                exception));
        }
    }

    private static AutomationElement? FindElementByRuntimeId(
        IReadOnlyList<int> runtimeId,
        CancellationToken cancellationToken)
    {
        AutomationElementCollection elements = AutomationElement.RootElement.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.IsScrollPatternAvailableProperty,
                true));

        foreach (AutomationElement element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (element.GetRuntimeId().SequenceEqual(runtimeId))
                {
                    return element;
                }
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        return null;
    }

    private static bool CanScroll(
        ScrollPattern.ScrollPatternInformation scroll,
        ScrollInputDirection direction)
    {
        return direction switch
        {
            ScrollInputDirection.Down or ScrollInputDirection.Up => scroll.VerticallyScrollable,
            ScrollInputDirection.Right or ScrollInputDirection.Left => scroll.HorizontallyScrollable,
            _ => false
        };
    }

    private static (ScrollAmount Horizontal, ScrollAmount Vertical) ResolveScrollAmounts(
        ScrollInputDirection direction)
    {
        return direction switch
        {
            ScrollInputDirection.Down => (ScrollAmount.NoAmount, ScrollAmount.LargeIncrement),
            ScrollInputDirection.Up => (ScrollAmount.NoAmount, ScrollAmount.LargeDecrement),
            ScrollInputDirection.Right => (ScrollAmount.LargeIncrement, ScrollAmount.NoAmount),
            ScrollInputDirection.Left => (ScrollAmount.LargeDecrement, ScrollAmount.NoAmount),
            _ => (ScrollAmount.NoAmount, ScrollAmount.NoAmount)
        };
    }

    private static bool HasReachedEnd(
        ScrollInputDirection direction,
        ScrollPattern.ScrollPatternInformation before,
        ScrollPattern.ScrollPatternInformation after)
    {
        return direction switch
        {
            ScrollInputDirection.Down => IsAtEnd(after.VerticalScrollPercent, 100)
                || DidNotMove(before.VerticalScrollPercent, after.VerticalScrollPercent),
            ScrollInputDirection.Up => IsAtEnd(after.VerticalScrollPercent, 0)
                || DidNotMove(before.VerticalScrollPercent, after.VerticalScrollPercent),
            ScrollInputDirection.Right => IsAtEnd(after.HorizontalScrollPercent, 100)
                || DidNotMove(before.HorizontalScrollPercent, after.HorizontalScrollPercent),
            ScrollInputDirection.Left => IsAtEnd(after.HorizontalScrollPercent, 0)
                || DidNotMove(before.HorizontalScrollPercent, after.HorizontalScrollPercent),
            _ => false
        };
    }

    private static bool DidNotMove(double before, double after)
    {
        return before >= 0 && after >= 0 && Math.Abs(before - after) < EndTolerance;
    }

    private static bool IsAtEnd(double value, double expected)
    {
        return value >= 0 && Math.Abs(value - expected) < EndTolerance;
    }

    private static double? NormalizePercent(double value)
    {
        return value < 0 ? null : value;
    }
}
