using Microsoft.UI.Xaml;
using SnapStudio.App.Composition;
using SnapStudio.Core.Diagnostics;

namespace SnapStudio.App;

public partial class App : Application
{
    private readonly ICrashRecoveryJournal _crashRecoveryJournal;
    private readonly string _recoverySessionId = Guid.NewGuid().ToString("N");
    private Window? _window;

    public App()
    {
        InitializeComponent();

        string appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SnapStudio");
        _crashRecoveryJournal = AppServices.CreateCrashRecoveryJournal(appDataRoot);

        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    public ICrashRecoveryJournal CrashRecoveryJournal => _crashRecoveryJournal;

    public string RecoverySessionId => _recoverySessionId;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WriteRecoveryEntry(
            RecoveryJournalEventKind.SessionStarted,
            "Application session started.",
            new Dictionary<string, string>
            {
                ["arguments"] = args.Arguments ?? string.Empty
            });

        _window = new MainWindow();
        _window.Activate();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteExceptionEntry(
            RecoveryJournalEventKind.UnhandledException,
            "Unhandled UI exception.",
            e.Exception,
            new Dictionary<string, string>
            {
                ["source"] = "Microsoft.UI.Xaml.Application"
            });
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception)
        {
            WriteRecoveryEntry(
                RecoveryJournalEventKind.UnhandledException,
                "Unhandled non-exception object.",
                new Dictionary<string, string>
                {
                    ["source"] = "AppDomain",
                    ["isTerminating"] = e.IsTerminating.ToString()
                });
            return;
        }

        WriteExceptionEntry(
            RecoveryJournalEventKind.UnhandledException,
            "Unhandled domain exception.",
            exception,
            new Dictionary<string, string>
            {
                ["source"] = "AppDomain",
                ["isTerminating"] = e.IsTerminating.ToString()
            });
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteExceptionEntry(
            RecoveryJournalEventKind.UnobservedTaskException,
            "Unobserved task exception.",
            e.Exception,
            new Dictionary<string, string>
            {
                ["source"] = "TaskScheduler"
            });
    }

    private void WriteExceptionEntry(
        RecoveryJournalEventKind eventKind,
        string message,
        Exception exception,
        IReadOnlyDictionary<string, string> properties)
    {
        var entryProperties = new Dictionary<string, string>(properties)
        {
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
            ["exceptionMessage"] = exception.Message,
            ["stackTrace"] = exception.StackTrace ?? string.Empty
        };

        WriteRecoveryEntry(eventKind, message, entryProperties);
    }

    private void WriteRecoveryEntry(
        RecoveryJournalEventKind eventKind,
        string message,
        IReadOnlyDictionary<string, string> properties)
    {
        try
        {
            _crashRecoveryJournal
                .WriteAsync(
                    RecoveryJournalEntry.Create(
                        _recoverySessionId,
                        eventKind,
                        message,
                        properties: properties),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception)
        {
        }
    }
}
