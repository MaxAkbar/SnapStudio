using System.ComponentModel;
using System.Runtime.InteropServices;
using SnapStudio.Core.System;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsHotkeyService : IHotkeyService, IDisposable
{
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private static readonly UIntPtr SubclassId = new(0x5353484B);

    private readonly Dictionary<int, HotkeyRegistration> _registrationsById = [];
    private readonly Dictionary<string, int> _registrationIdsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly nint _ownerWindowHandle;
    private readonly SubclassProc _subclassProc;
    private bool _subclassAttached;
    private int _nextRegistrationId = 1;

    public WindowsHotkeyService(nint ownerWindowHandle)
    {
        _ownerWindowHandle = ownerWindowHandle;
        _subclassProc = OnWindowMessage;
    }

    public event EventHandler<HotkeyPressedEvent>? HotkeyPressed;

    public Task<HotkeyRegistrationResult> RegisterAsync(
        HotkeyRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Gesture);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParseGesture(registration.Gesture, out ParsedHotkey parsedHotkey, out string parseError))
        {
            return Task.FromResult(HotkeyRegistrationResult.Failed(parseError));
        }

        if (_ownerWindowHandle == 0)
        {
            return Task.FromResult(HotkeyRegistrationResult.Failed(
                "Hotkey registration requires a valid owner window."));
        }

        HotkeyRegistrationResult subclassResult = EnsureSubclassAttached();
        if (!subclassResult.Succeeded)
        {
            return Task.FromResult(subclassResult);
        }

        if (_registrationIdsByName.ContainsKey(registration.Name))
        {
            Unregister(registration.Name);
        }

        int registrationId = _nextRegistrationId++;
        bool registered = RegisterHotKey(
            _ownerWindowHandle,
            registrationId,
            parsedHotkey.Modifiers,
            parsedHotkey.VirtualKey);

        if (!registered)
        {
            return Task.FromResult(HotkeyRegistrationResult.Failed(
                CreateRegistrationFailureMessage(registration.Gesture, Marshal.GetLastWin32Error())));
        }

        _registrationIdsByName[registration.Name] = registrationId;
        _registrationsById[registrationId] = registration;

        return Task.FromResult(HotkeyRegistrationResult.Success());
    }

    public Task UnregisterAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        Unregister(name);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (int registrationId in _registrationsById.Keys.ToArray())
        {
            UnregisterHotKey(_ownerWindowHandle, registrationId);
        }

        _registrationIdsByName.Clear();
        _registrationsById.Clear();

        if (_subclassAttached)
        {
            RemoveWindowSubclass(_ownerWindowHandle, _subclassProc, SubclassId);
            _subclassAttached = false;
        }
    }

    private HotkeyRegistrationResult EnsureSubclassAttached()
    {
        if (_subclassAttached)
        {
            return HotkeyRegistrationResult.Success();
        }

        bool attached = SetWindowSubclass(
            _ownerWindowHandle,
            _subclassProc,
            SubclassId,
            0);

        if (!attached)
        {
            int errorCode = Marshal.GetLastWin32Error();
            return HotkeyRegistrationResult.Failed(
                errorCode == 0
                    ? "Hotkey message handling could not be attached to the owner window."
                    : $"Hotkey message handling could not be attached to the owner window: {new Win32Exception(errorCode).Message}");
        }

        _subclassAttached = true;
        return HotkeyRegistrationResult.Success();
    }

    private void Unregister(string name)
    {
        if (!_registrationIdsByName.Remove(name, out int registrationId))
        {
            return;
        }

        _registrationsById.Remove(registrationId);
        UnregisterHotKey(_ownerWindowHandle, registrationId);
    }

    private nint OnWindowMessage(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        UIntPtr subclassId,
        nint referenceData)
    {
        if (message == WmHotkey
            && _registrationsById.TryGetValue(wParam.ToInt32(), out HotkeyRegistration? registration))
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEvent(registration.Name, registration.Gesture));
            return 0;
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private static string CreateRegistrationFailureMessage(string gesture, int errorCode)
    {
        if (errorCode == ErrorHotkeyAlreadyRegistered)
        {
            return $"Hotkey '{gesture}' is already registered by another application.";
        }

        return errorCode == 0
            ? $"Hotkey '{gesture}' could not be registered."
            : $"Hotkey '{gesture}' could not be registered: {new Win32Exception(errorCode).Message}";
    }

    private static bool TryParseGesture(
        string gesture,
        out ParsedHotkey parsedHotkey,
        out string errorMessage)
    {
        parsedHotkey = default;
        errorMessage = string.Empty;

        string[] tokens = gesture.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            errorMessage = "Hotkey gesture must include a key.";
            return false;
        }

        uint modifiers = ModNoRepeat;
        uint? virtualKey = null;

        foreach (string token in tokens)
        {
            string normalized = token.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            switch (normalized)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModControl;
                    break;
                case "SHIFT":
                    modifiers |= ModShift;
                    break;
                case "ALT":
                    modifiers |= ModAlt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModWin;
                    break;
                default:
                    if (!TryResolveVirtualKey(normalized, out uint resolvedVirtualKey))
                    {
                        errorMessage = $"Hotkey token '{token}' is not supported.";
                        return false;
                    }

                    if (virtualKey.HasValue)
                    {
                        errorMessage = "Hotkey gesture can include only one key.";
                        return false;
                    }

                    virtualKey = resolvedVirtualKey;
                    break;
            }
        }

        if (!virtualKey.HasValue)
        {
            errorMessage = "Hotkey gesture must include a key.";
            return false;
        }

        parsedHotkey = new ParsedHotkey(modifiers, virtualKey.Value);
        return true;
    }

    private static bool TryResolveVirtualKey(string token, out uint virtualKey)
    {
        virtualKey = 0;

        if (token.Length == 1)
        {
            char key = token[0];
            if (key is >= 'A' and <= 'Z')
            {
                virtualKey = key;
                return true;
            }

            if (key is >= '0' and <= '9')
            {
                virtualKey = key;
                return true;
            }
        }

        if (token.Length is >= 2 and <= 3
            && token[0] == 'F'
            && int.TryParse(token[1..], out int functionKey)
            && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x6F + functionKey);
            return true;
        }

        return NamedVirtualKeys.TryGetValue(token, out virtualKey);
    }

    private static readonly IReadOnlyDictionary<string, uint> NamedVirtualKeys =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["BACKSPACE"] = 0x08,
            ["TAB"] = 0x09,
            ["ENTER"] = 0x0D,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["SPACE"] = 0x20,
            ["PAGEUP"] = 0x21,
            ["PAGEDOWN"] = 0x22,
            ["END"] = 0x23,
            ["HOME"] = 0x24,
            ["LEFT"] = 0x25,
            ["UP"] = 0x26,
            ["RIGHT"] = 0x27,
            ["DOWN"] = 0x28,
            ["PRINTSCREEN"] = 0x2C,
            ["PRTSC"] = 0x2C,
            ["PRTSCN"] = 0x2C,
            ["SNAPSHOT"] = 0x2C,
            ["INSERT"] = 0x2D,
            ["DELETE"] = 0x2E
        };

    private readonly record struct ParsedHotkey(uint Modifiers, uint VirtualKey);

    private delegate nint SubclassProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        UIntPtr subclassId,
        nint referenceData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        nint hWnd,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc subclassProc,
        UIntPtr subclassId,
        nint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc subclassProc,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam);
}
