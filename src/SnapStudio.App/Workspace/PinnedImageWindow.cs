using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SnapStudio.Core.Rendering;
using Windows.Graphics;

namespace SnapStudio.App.Workspace;

internal sealed class PinnedImageWindow : Window
{
    private const int MaximumInitialWidth = 720;
    private const int MaximumInitialHeight = 540;
    private const int MinimumInitialWidth = 240;
    private const int MinimumInitialHeight = 160;
    private readonly string _imagePath;

    public PinnedImageWindow(
        string title,
        string imagePath,
        RenderedImage image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(image);

        _imagePath = imagePath;
        Title = string.IsNullOrWhiteSpace(title)
            ? "Pinned Capture"
            : $"Pinned - {title.Trim()}";

        Content = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 24, 24)),
            Children =
            {
                new Image
                {
                    Source = new BitmapImage(new Uri(imagePath)),
                    Stretch = Stretch.Uniform
                }
            }
        };

        ConfigureWindow(image);
        Closed += (_, _) => DeletePinnedImage();
    }

    private void ConfigureWindow(RenderedImage image)
    {
        SizeInt32 initialSize = CalculateInitialSize(image.Width, image.Height);
        AppWindow.Resize(initialSize);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
        }
    }

    private void DeletePinnedImage()
    {
        try
        {
            if (File.Exists(_imagePath))
            {
                File.Delete(_imagePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static SizeInt32 CalculateInitialSize(
        int imageWidth,
        int imageHeight)
    {
        int safeWidth = Math.Max(1, imageWidth);
        int safeHeight = Math.Max(1, imageHeight);
        double scale = Math.Min(
            1,
            Math.Min(
                MaximumInitialWidth / (double)safeWidth,
                MaximumInitialHeight / (double)safeHeight));

        int width = Math.Clamp(
            (int)Math.Round(safeWidth * scale),
            Math.Min(MinimumInitialWidth, safeWidth),
            MaximumInitialWidth);
        int height = Math.Clamp(
            (int)Math.Round(safeHeight * scale),
            Math.Min(MinimumInitialHeight, safeHeight),
            MaximumInitialHeight);

        return new SizeInt32(width, height);
    }
}
