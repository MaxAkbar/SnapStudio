using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace SnapStudio.App.Capture;

internal sealed class RegionSelectionWindow : Window
{
    private const double MinimumRegionSize = 4;
    private const double HandleSize = 12;
    private const double ToolbarEstimatedWidth = 184;
    private const double ToolbarEstimatedHeight = 44;
    private readonly TaskCompletionSource<RectD?> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<RegionSelectionHandle, Border> _handles = [];
    private readonly Border _selectionBox;
    private readonly Border _toolbar;
    private readonly Grid _root;
    private readonly RectD _virtualScreenBounds;
    private readonly Canvas _surface;
    private Point _startPoint;
    private Point _dragStartPoint;
    private RectD _dragStartBounds;
    private RectD? _selectionBounds;
    private RegionSelectionDragMode _dragMode;
    private RegionSelectionHandle _activeHandle = RegionSelectionHandle.None;

    public RegionSelectionWindow(
        RectD virtualScreenBounds,
        ScreenPreviewImage? previewImage)
    {
        _virtualScreenBounds = virtualScreenBounds;
        Title = "SnapStudio Region Capture";

        _root = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0))
        };

        if (previewImage is not null && File.Exists(previewImage.Path))
        {
            _root.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(previewImage.Path)),
                Stretch = Stretch.Fill
            });
        }

        _surface = new Canvas
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(96, 0, 0, 0)),
            IsTabStop = true
        };

        _selectionBox = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(32, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(2),
            Visibility = Visibility.Collapsed
        };

        _surface.Children.Add(_selectionBox);
        CreateResizeHandles();
        _toolbar = CreateToolbar();
        _surface.Children.Add(_toolbar);
        _root.Children.Add(_surface);
        Content = _root;

        _surface.PointerPressed += Surface_PointerPressed;
        _surface.PointerMoved += Surface_PointerMoved;
        _surface.PointerReleased += Surface_PointerReleased;
        _surface.PointerCanceled += Surface_PointerCanceled;
        _surface.KeyDown += Surface_KeyDown;
        _root.KeyDown += Surface_KeyDown;
        Closed += (_, _) => _completion.TrySetResult(null);

        ConfigureWindow();
    }

    private enum RegionSelectionDragMode
    {
        None,
        Draw,
        Move,
        Resize
    }

    private enum RegionSelectionHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }

    public async Task<RectD?> SelectAsync(CancellationToken cancellationToken)
    {
        await using CancellationTokenRegistration registration = cancellationToken.Register(
            () => Complete(null));

        Activate();
        _surface.Focus(FocusState.Programmatic);

        return await _completion.Task.ConfigureAwait(true);
    }

    private void ConfigureWindow()
    {
        AppWindow.MoveAndResize(new RectInt32(
            (int)Math.Floor(_virtualScreenBounds.X),
            (int)Math.Floor(_virtualScreenBounds.Y),
            (int)Math.Ceiling(_virtualScreenBounds.Width),
            (int)Math.Ceiling(_virtualScreenBounds.Height)));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsToolbarSource(e.OriginalSource))
        {
            return;
        }

        Point point = ClampPoint(e.GetCurrentPoint(_surface).Position);
        _dragStartPoint = point;
        _dragStartBounds = _selectionBounds ?? new RectD(point.X, point.Y, 0, 0);
        _activeHandle = RegionSelectionHandle.None;

        if (TryResolveHandle(e.OriginalSource, out RegionSelectionHandle handle))
        {
            _dragMode = RegionSelectionDragMode.Resize;
            _activeHandle = handle;
        }
        else if (_selectionBounds is RectD existingBounds && ContainsPoint(existingBounds, point))
        {
            _dragMode = RegionSelectionDragMode.Move;
        }
        else
        {
            _dragMode = RegionSelectionDragMode.Draw;
            _startPoint = point;
            _selectionBounds = new RectD(point.X, point.Y, 0, 0);
            SetToolbarVisibility(Visibility.Collapsed);
        }

        _selectionBox.Visibility = Visibility.Visible;
        _surface.CapturePointer(e.Pointer);
        UpdateSelectionForPointer(point);
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragMode == RegionSelectionDragMode.None)
        {
            return;
        }

        UpdateSelectionForPointer(ClampPoint(e.GetCurrentPoint(_surface).Position));
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragMode == RegionSelectionDragMode.None)
        {
            return;
        }

        UpdateSelectionForPointer(ClampPoint(e.GetCurrentPoint(_surface).Position));
        _dragMode = RegionSelectionDragMode.None;
        _activeHandle = RegionSelectionHandle.None;
        _surface.ReleasePointerCapture(e.Pointer);

        if (_selectionBounds is not RectD bounds
            || bounds.Width < MinimumRegionSize
            || bounds.Height < MinimumRegionSize)
        {
            ClearSelection();
            return;
        }

        _selectionBounds = bounds;
        UpdateSelectionVisual();
        SetToolbarVisibility(Visibility.Visible);
        _surface.Focus(FocusState.Programmatic);
    }

    private void Surface_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_dragMode != RegionSelectionDragMode.None)
        {
            _dragMode = RegionSelectionDragMode.None;
            _activeHandle = RegionSelectionHandle.None;
            _surface.ReleasePointerCapture(e.Pointer);
        }

        ClearSelection();
    }

    private void Surface_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Complete(null);
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            CompleteSelection();
        }
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        CompleteSelection();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Complete(null);
    }

    private void CreateResizeHandles()
    {
        foreach (RegionSelectionHandle handle in Enum.GetValues<RegionSelectionHandle>())
        {
            if (handle == RegionSelectionHandle.None)
            {
                continue;
            }

            var border = new Border
            {
                Width = HandleSize,
                Height = HandleSize,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(2),
                Tag = handle,
                Visibility = Visibility.Collapsed
            };

            _handles[handle] = border;
            _surface.Children.Add(border);
        }
    }

    private Border CreateToolbar()
    {
        var captureButton = new Button
        {
            MinWidth = 96,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new SymbolIcon(Symbol.Camera),
                    new TextBlock { Text = "Capture" }
                }
            }
        };
        captureButton.Click += CaptureButton_Click;

        var cancelButton = new Button
        {
            MinWidth = 88,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new SymbolIcon(Symbol.Cancel),
                    new TextBlock { Text = "Cancel" }
                }
            }
        };
        cancelButton.Click += CancelButton_Click;

        return new Border
        {
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(245, 32, 32, 32)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 96, 96)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    captureButton,
                    cancelButton
                }
            }
        };
    }

    private void UpdateSelectionForPointer(Point point)
    {
        _selectionBounds = _dragMode switch
        {
            RegionSelectionDragMode.Draw => CreateLocalBounds(_startPoint, point),
            RegionSelectionDragMode.Move => MoveBounds(_dragStartBounds, point),
            RegionSelectionDragMode.Resize => ResizeBounds(_dragStartBounds, point, _activeHandle),
            _ => _selectionBounds
        };

        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        if (_selectionBounds is not RectD bounds)
        {
            ClearSelection();
            return;
        }

        bounds = ClampBounds(bounds);
        _selectionBounds = bounds;

        Canvas.SetLeft(_selectionBox, bounds.X);
        Canvas.SetTop(_selectionBox, bounds.Y);
        _selectionBox.Width = bounds.Width;
        _selectionBox.Height = bounds.Height;
        _selectionBox.Visibility = Visibility.Visible;

        SetHandlePositions(bounds);
        UpdateToolbarPosition(bounds);
    }

    private void SetHandlePositions(RectD bounds)
    {
        double centerX = bounds.X + (bounds.Width / 2);
        double centerY = bounds.Y + (bounds.Height / 2);
        double right = bounds.Right;
        double bottom = bounds.Bottom;

        SetHandlePosition(RegionSelectionHandle.TopLeft, bounds.X, bounds.Y);
        SetHandlePosition(RegionSelectionHandle.Top, centerX, bounds.Y);
        SetHandlePosition(RegionSelectionHandle.TopRight, right, bounds.Y);
        SetHandlePosition(RegionSelectionHandle.Right, right, centerY);
        SetHandlePosition(RegionSelectionHandle.BottomRight, right, bottom);
        SetHandlePosition(RegionSelectionHandle.Bottom, centerX, bottom);
        SetHandlePosition(RegionSelectionHandle.BottomLeft, bounds.X, bottom);
        SetHandlePosition(RegionSelectionHandle.Left, bounds.X, centerY);
        SetHandlesVisibility(Visibility.Visible);
    }

    private void SetHandlePosition(RegionSelectionHandle handle, double centerX, double centerY)
    {
        Border border = _handles[handle];
        double surfaceWidth = GetSurfaceWidth();
        double surfaceHeight = GetSurfaceHeight();

        Canvas.SetLeft(border, Math.Clamp(centerX - (HandleSize / 2), 0, Math.Max(0, surfaceWidth - HandleSize)));
        Canvas.SetTop(border, Math.Clamp(centerY - (HandleSize / 2), 0, Math.Max(0, surfaceHeight - HandleSize)));
    }

    private void UpdateToolbarPosition(RectD bounds)
    {
        if (_toolbar.Visibility != Visibility.Visible)
        {
            return;
        }

        double surfaceWidth = GetSurfaceWidth();
        double surfaceHeight = GetSurfaceHeight();
        double left = Math.Clamp(
            bounds.Right - ToolbarEstimatedWidth,
            0,
            Math.Max(0, surfaceWidth - ToolbarEstimatedWidth));
        double top = bounds.Bottom + 12;
        if (top + ToolbarEstimatedHeight > surfaceHeight)
        {
            top = bounds.Y - ToolbarEstimatedHeight - 12;
        }

        top = Math.Clamp(top, 0, Math.Max(0, surfaceHeight - ToolbarEstimatedHeight));
        Canvas.SetLeft(_toolbar, left);
        Canvas.SetTop(_toolbar, top);
    }

    private void CompleteSelection()
    {
        if (_selectionBounds is not RectD bounds
            || bounds.Width < MinimumRegionSize
            || bounds.Height < MinimumRegionSize)
        {
            ClearSelection();
            return;
        }

        Complete(ConvertToScreenBounds(bounds));
    }

    private void ClearSelection()
    {
        _selectionBounds = null;
        _selectionBox.Visibility = Visibility.Collapsed;
        SetHandlesVisibility(Visibility.Collapsed);
        SetToolbarVisibility(Visibility.Collapsed);
    }

    private void SetHandlesVisibility(Visibility visibility)
    {
        foreach (Border handle in _handles.Values)
        {
            handle.Visibility = visibility;
        }
    }

    private void SetToolbarVisibility(Visibility visibility)
    {
        _toolbar.Visibility = visibility;
        if (_selectionBounds is RectD bounds)
        {
            UpdateToolbarPosition(bounds);
        }
    }

    private RectD CreateLocalBounds(Point start, Point end)
    {
        double left = Math.Min(start.X, end.X);
        double top = Math.Min(start.Y, end.Y);
        double right = Math.Max(start.X, end.X);
        double bottom = Math.Max(start.Y, end.Y);

        return ClampBounds(new RectD(left, top, right - left, bottom - top));
    }

    private RectD MoveBounds(RectD bounds, Point point)
    {
        double dx = point.X - _dragStartPoint.X;
        double dy = point.Y - _dragStartPoint.Y;
        double surfaceWidth = GetSurfaceWidth();
        double surfaceHeight = GetSurfaceHeight();
        double left = Math.Clamp(bounds.X + dx, 0, Math.Max(0, surfaceWidth - bounds.Width));
        double top = Math.Clamp(bounds.Y + dy, 0, Math.Max(0, surfaceHeight - bounds.Height));

        return new RectD(left, top, bounds.Width, bounds.Height);
    }

    private RectD ResizeBounds(
        RectD bounds,
        Point point,
        RegionSelectionHandle handle)
    {
        double surfaceWidth = GetSurfaceWidth();
        double surfaceHeight = GetSurfaceHeight();
        double left = bounds.X;
        double top = bounds.Y;
        double right = bounds.Right;
        double bottom = bounds.Bottom;

        if (handle is RegionSelectionHandle.TopLeft
            or RegionSelectionHandle.Left
            or RegionSelectionHandle.BottomLeft)
        {
            left = Math.Clamp(point.X, 0, right - MinimumRegionSize);
        }

        if (handle is RegionSelectionHandle.TopLeft
            or RegionSelectionHandle.Top
            or RegionSelectionHandle.TopRight)
        {
            top = Math.Clamp(point.Y, 0, bottom - MinimumRegionSize);
        }

        if (handle is RegionSelectionHandle.TopRight
            or RegionSelectionHandle.Right
            or RegionSelectionHandle.BottomRight)
        {
            right = Math.Clamp(point.X, left + MinimumRegionSize, surfaceWidth);
        }

        if (handle is RegionSelectionHandle.BottomLeft
            or RegionSelectionHandle.Bottom
            or RegionSelectionHandle.BottomRight)
        {
            bottom = Math.Clamp(point.Y, top + MinimumRegionSize, surfaceHeight);
        }

        return ClampBounds(new RectD(left, top, right - left, bottom - top));
    }

    private RectD ClampBounds(RectD bounds)
    {
        double surfaceWidth = GetSurfaceWidth();
        double surfaceHeight = GetSurfaceHeight();
        double left = Math.Clamp(bounds.X, 0, surfaceWidth);
        double top = Math.Clamp(bounds.Y, 0, surfaceHeight);
        double right = Math.Clamp(bounds.Right, left, surfaceWidth);
        double bottom = Math.Clamp(bounds.Bottom, top, surfaceHeight);

        return new RectD(left, top, right - left, bottom - top);
    }

    private Point ClampPoint(Point point)
    {
        return new Point(
            Math.Clamp(point.X, 0, GetSurfaceWidth()),
            Math.Clamp(point.Y, 0, GetSurfaceHeight()));
    }

    private double GetSurfaceWidth()
    {
        return Math.Max(1, _surface.ActualWidth);
    }

    private double GetSurfaceHeight()
    {
        return Math.Max(1, _surface.ActualHeight);
    }

    private static bool ContainsPoint(RectD bounds, Point point)
    {
        return point.X >= bounds.X
            && point.X <= bounds.Right
            && point.Y >= bounds.Y
            && point.Y <= bounds.Bottom;
    }

    private bool IsToolbarSource(object? source)
    {
        return source is DependencyObject dependencyObject
            && IsDescendantOf(dependencyObject, _toolbar);
    }

    private bool TryResolveHandle(object? source, out RegionSelectionHandle handle)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { Tag: RegionSelectionHandle currentHandle })
            {
                handle = currentHandle;
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        handle = RegionSelectionHandle.None;
        return false;
    }

    private static bool IsDescendantOf(
        DependencyObject source,
        DependencyObject target)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private RectD ConvertToScreenBounds(RectD localBounds)
    {
        double scaleX = _virtualScreenBounds.Width / Math.Max(1, _surface.ActualWidth);
        double scaleY = _virtualScreenBounds.Height / Math.Max(1, _surface.ActualHeight);

        return new RectD(
            _virtualScreenBounds.X + (localBounds.X * scaleX),
            _virtualScreenBounds.Y + (localBounds.Y * scaleY),
            localBounds.Width * scaleX,
            localBounds.Height * scaleY);
    }

    private void Complete(RectD? result)
    {
        if (_completion.TrySetResult(result))
        {
            Close();
        }
    }
}
