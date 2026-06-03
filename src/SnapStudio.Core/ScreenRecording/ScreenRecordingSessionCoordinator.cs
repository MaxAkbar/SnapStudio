using System.Diagnostics.CodeAnalysis;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.ScreenRecording;

public sealed class ScreenRecordingSessionCoordinator : IScreenRecordingService
{
    private readonly IScreenRecordingEngine _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ScreenRecordingSession? _activeSession;

    public ScreenRecordingSessionCoordinator(IScreenRecordingEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public async Task<ScreenRecordingStartResult> StartAsync(
        ScreenRecordingStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ScreenRecordingFailure? validationFailure = ValidateStartRequest(request);
        if (validationFailure is not null)
        {
            return ScreenRecordingStartResult.Failed(validationFailure);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeSession is not null && IsActive(_activeSession.State))
            {
                return ScreenRecordingStartResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.AlreadyRecording,
                    "A screen recording session is already active."));
            }

            ScreenRecordingStartResult result = await _engine
                .StartAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded && result.Session is not null)
            {
                _activeSession = result.Session;
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScreenRecordingControlResult> PauseAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetActiveSession(sessionId, out ScreenRecordingSession? session))
            {
                return ScreenRecordingControlResult.Failed(CreateSessionNotFoundFailure());
            }

            if (session.State == ScreenRecordingState.Paused)
            {
                return ScreenRecordingControlResult.Success(session);
            }

            if (session.State != ScreenRecordingState.Recording)
            {
                return ScreenRecordingControlResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.InvalidRequest,
                    $"Cannot pause a recording while it is {session.State}."));
            }

            _activeSession = session with { State = ScreenRecordingState.Paused };
            return ScreenRecordingControlResult.Success(_activeSession);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScreenRecordingControlResult> ResumeAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetActiveSession(sessionId, out ScreenRecordingSession? session))
            {
                return ScreenRecordingControlResult.Failed(CreateSessionNotFoundFailure());
            }

            if (session.State == ScreenRecordingState.Recording)
            {
                return ScreenRecordingControlResult.Success(session);
            }

            if (session.State != ScreenRecordingState.Paused)
            {
                return ScreenRecordingControlResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.InvalidRequest,
                    $"Cannot resume a recording while it is {session.State}."));
            }

            _activeSession = session with { State = ScreenRecordingState.Recording };
            return ScreenRecordingControlResult.Success(_activeSession);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScreenRecordingStopResult> StopAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetActiveSession(sessionId, out ScreenRecordingSession? session))
            {
                return ScreenRecordingStopResult.Failed(CreateSessionNotFoundFailure());
            }

            ScreenRecordingSession stoppingSession = session with
            {
                State = ScreenRecordingState.Stopping
            };
            _activeSession = stoppingSession;

            ScreenRecordingStopResult result = await _engine
                .StopAsync(stoppingSession, cancellationToken)
                .ConfigureAwait(false);

            _activeSession = null;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScreenRecordingStatusResult> GetStatusAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetActiveSession(sessionId, out ScreenRecordingSession? session))
            {
                return ScreenRecordingStatusResult.Failed(CreateSessionNotFoundFailure());
            }

            return ScreenRecordingStatusResult.Success(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ScreenRecordingFailure? ValidateStartRequest(ScreenRecordingStartRequest request)
    {
        if (!Enum.IsDefined(request.TargetKind))
        {
            return new ScreenRecordingFailure(
                ScreenRecordingFailureReason.InvalidRequest,
                "The recording target kind is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return new ScreenRecordingFailure(
                ScreenRecordingFailureReason.InvalidRequest,
                "A recording output path is required.");
        }

        if (request.MaximumDuration is { } maximumDuration && maximumDuration <= TimeSpan.Zero)
        {
            return new ScreenRecordingFailure(
                ScreenRecordingFailureReason.InvalidRequest,
                "The recording maximum duration must be greater than zero.");
        }

        if (request.Bounds is { } bounds && !IsUsableBounds(bounds))
        {
            return new ScreenRecordingFailure(
                ScreenRecordingFailureReason.InvalidRequest,
                "Recording bounds must be finite and larger than zero.");
        }

        if (request.TargetKind == CaptureTargetKind.Region && request.Bounds is null)
        {
            return new ScreenRecordingFailure(
                ScreenRecordingFailureReason.InvalidRequest,
                "Region recording requires capture bounds.");
        }

        return null;
    }

    private static bool IsUsableBounds(RectD bounds)
    {
        return double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width > 0
            && bounds.Height > 0;
    }

    private static bool IsActive(ScreenRecordingState state)
    {
        return state is ScreenRecordingState.Starting
            or ScreenRecordingState.Recording
            or ScreenRecordingState.Paused
            or ScreenRecordingState.Stopping;
    }

    private bool TryGetActiveSession(
        ScreenRecordingSessionId sessionId,
        [NotNullWhen(true)]
        out ScreenRecordingSession? session)
    {
        session = _activeSession;
        return session is not null
            && session.Id == sessionId
            && IsActive(session.State);
    }

    private static ScreenRecordingFailure CreateSessionNotFoundFailure()
    {
        return new ScreenRecordingFailure(
            ScreenRecordingFailureReason.SessionNotFound,
            "The screen recording session was not found.");
    }
}
