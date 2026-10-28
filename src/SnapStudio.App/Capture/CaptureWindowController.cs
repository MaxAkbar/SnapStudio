using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SnapStudio.Core.Capture;

namespace SnapStudio.App.Capture;

public sealed class CaptureWindowController : ICaptureWindowController
{
    private readonly Window _window;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _closed;

    public CaptureWindowController(Window window)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;
        window.Closed += (_, _) => _closed = true;
    }

    public async Task<IAsyncDisposable> MinimizeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ready = new TaskCompletionSource<RestoreScope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Minimize()
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_closed || _window.AppWindow.Presenter is not OverlappedPresenter presenter)
                    throw new InvalidOperationException("The capture window is unavailable.");

                var scope = new RestoreScope(this, presenter, presenter.State);
                presenter.Minimize();
                ready.SetResult(scope);
            }
            catch (Exception exception) { ready.SetException(exception); }
        }

        if (_dispatcherQueue.HasThreadAccess) Minimize();
        else if (!_dispatcherQueue.TryEnqueue(Minimize))
            throw new InvalidOperationException("The capture window is closing.");

        RestoreScope restore = await ready.Task.ConfigureAwait(false);
        try
        {
            // Let the minimize animation and desktop repaint finish before preview/frame acquisition.
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            return restore;
        }
        catch
        {
            await restore.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class RestoreScope(CaptureWindowController owner, OverlappedPresenter presenter, OverlappedPresenterState state) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
            var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Restore()
            {
                try
                {
                    if (!owner._closed && state != OverlappedPresenterState.Minimized)
                    {
                        if (state == OverlappedPresenterState.Maximized) presenter.Maximize();
                        else presenter.Restore();
                        owner._window.Activate();
                    }
                    restored.SetResult();
                }
                catch (Exception exception) { restored.SetException(exception); }
            }

            // Restoration must not use the cancelled capture token, or run after the window closes.
            if (owner._dispatcherQueue.HasThreadAccess) Restore();
            else if (!owner._dispatcherQueue.TryEnqueue(Restore)) restored.SetResult();
            return new ValueTask(restored.Task);
        }
    }
}
