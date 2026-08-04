using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace DesktopPeople.GodotHost;

/// <summary>Commands the tray can ask the host to carry out. They are queued rather than applied
/// directly: the menu runs on the tray's own thread, while the simulation and the overlay window
/// may only be touched from Godot's main thread.</summary>
internal enum TrayCommand
{
    OpenLauncher,
    ShowCharacter,
    HideCharacter,
    Pause,
    Resume,
    Quit,
    IntensityCalm,
    IntensityNormal,
    IntensityActive,
    ShowPlatformDebug,
    HidePlatformDebug,
}

/// <summary>
/// The Godot host's system-tray icon, built straight on Shell_NotifyIcon.
/// <para>
/// WinForms' <c>NotifyIcon</c> was tried first and does not work here: Godot initialises the
/// .NET runtime itself and never produces a runtimeconfig for the game assembly, so the
/// Windows Desktop framework is not loaded and <c>System.Windows.Forms</c> fails to resolve at
/// runtime — even though it is installed on the machine. Win32 directly has no such dependency,
/// and it is what the rest of this host already talks to.
/// </para>
/// <para>
/// Everything lives on a dedicated STA thread with its own message pump, because a tray icon
/// needs a window that pumps messages and <c>TrackPopupMenu</c> blocks while the menu is open —
/// on Godot's thread that would freeze the character for as long as the menu was up.
/// </para>
/// <para>
/// Until this existed the Godot host could not be closed at all: WS_EX_NOACTIVATE takes its
/// keyboard away and WS_EX_TOOLWINDOW keeps it out of Alt+Tab, so quitting meant Task Manager.
/// </para>
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmTrayCallback = 0x0400 + 1;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint MfString = 0x00000000;
    private const uint MfChecked = 0x00000008;
    private const uint MfSeparator = 0x00000800;
    private const uint MfPopup = 0x00000010;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNoNotify = 0x0080;
    private const int IdiApplication = 32512;
    private const uint WsPopup = 0x80000000;

    private const uint IdOpenLauncher = 7;
    private const uint IdVisibility = 1;
    private const uint IdPause = 2;
    private const uint IdCalm = 3;
    private const uint IdNormal = 4;
    private const uint IdActive = 5;
    private const uint IdQuit = 6;
    private const uint IdPlatformDebug = 8;

    private readonly ConcurrentQueue<TrayCommand> _commands = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;

    // Held in a field for as long as the window lives: the delegate is handed to Win32 as a
    // function pointer, which the GC does not see as a reference.
    private readonly WndProc _wndProc;

    private nint _window;

    /// <summary>Why the tray is or is not there. A tray icon that silently fails to register
    /// leaves the host with no way to quit at all, so the outcome is reported rather than
    /// assumed.</summary>
    public string Status { get; private set; } = "not started";

    private bool _visible;
    private bool _paused;
    private string _intensity;
    private bool _platformDebug;

    public TrayIcon(bool visible, bool paused, string intensity, bool platformDebug)
    {
        _visible = visible;
        _paused = paused;
        _intensity = intensity;
        _platformDebug = platformDebug;
        _wndProc = HandleMessage;

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "DesktopPeople tray",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Bounded: a tray that fails to come up must not take the character down with it.
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public bool TryDequeue(out TrayCommand command) => _commands.TryDequeue(out command);

    /// <summary>Keeps the ticks honest when something other than this menu changes the state —
    /// the launch window can change activity, and releasing the character from it makes the
    /// character visible. The menu is rebuilt from these on every click, so a plain assignment
    /// from the host thread is all the synchronisation needed.</summary>
    public void SyncState(bool visible, bool paused, string intensity)
    {
        _visible = visible;
        _paused = paused;
        _intensity = intensity;
    }

    public void Dispose()
    {
        if (_window != 0)
        {
            PostMessage(_window, WmClose, 0, 0);
        }

        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void Run()
    {
        try
        {
            CreateTray();
        }
        finally
        {
            // Set even on failure, so a broken tray costs the host five seconds at most once.
            _ready.Set();
        }

        while (GetMessage(out Msg message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private void CreateTray()
    {
        nint instance = GetModuleHandle(null);
        var windowClass = new WndClassW
        {
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            HInstance = instance,
            LpszClassName = "DesktopPeopleTray",
        };

        RegisterClassW(ref windowClass);

        // A plain hidden pop-up rather than a message-only window: TrackPopupMenu needs an
        // owner that can be brought to the foreground, and HWND_MESSAGE windows cannot be.
        _window = CreateWindowExW(
            0, "DesktopPeopleTray", "DesktopPeople", WsPopup, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_window == 0)
        {
            Status = $"window creation failed (Win32 error {Marshal.GetLastWin32Error()})";
            return;
        }

        NotifyIconData data = CreateIconData();
        data.UFlags = NifMessage | NifIcon | NifTip;
        data.UCallbackMessage = WmTrayCallback;
        data.HIcon = LoadIconW(0, IdiApplication);
        data.SzTip = "DesktopPeople (Godot)";
        Status = Shell_NotifyIconW(NimAdd, ref data) ? "ok" : "Shell_NotifyIcon rejected the icon";
    }

    /// <summary>Every string field is filled in: ByValTStr marshalling has no null to map.</summary>
    private NotifyIconData CreateIconData() => new()
    {
        CbSize = Marshal.SizeOf<NotifyIconData>(),
        HWnd = _window,
        UID = 1,
        SzTip = string.Empty,
        SzInfo = string.Empty,
        SzInfoTitle = string.Empty,
    };

    private nint HandleMessage(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            // Double-click opens the launcher, as in the WinForms host. It is checked first
            // because Windows sends the plain button-up messages around a double-click too.
            case WmTrayCallback when (uint)lParam == WmLButtonDoubleClick:
                _commands.Enqueue(TrayCommand.OpenLauncher);
                return 0;

            case WmTrayCallback when (uint)lParam is WmRButtonUp or WmLButtonUp:
                ShowMenu();
                return 0;

            case WmClose:
                NotifyIconData data = CreateIconData();
                Shell_NotifyIconW(NimDelete, ref data);
                DestroyWindow(_window);
                _window = 0;
                return 0;

            case WmDestroy:
                PostQuitMessage(0);
                return 0;

            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    /// <summary>Built fresh on every click from this class's own mirror of the state, so the
    /// ticks always match what was last asked for without a second source of truth.</summary>
    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        AppendMenuW(menu, MfString, IdOpenLauncher, "Открыть DesktopPeople");
        AppendMenuW(menu, MfSeparator, 0, null);
        AppendMenuW(menu, MfString | (_visible ? MfChecked : 0), IdVisibility, "Показать персонажа");
        AppendMenuW(menu, MfString | (_paused ? MfChecked : 0), IdPause, "Пауза");

        nint intensity = CreatePopupMenu();
        AppendMenuW(intensity, MfString | (_intensity == "calm" ? MfChecked : 0), IdCalm, "Спокойно");
        AppendMenuW(intensity, MfString | (_intensity == "normal" ? MfChecked : 0), IdNormal, "Обычно");
        AppendMenuW(intensity, MfString | (_intensity == "active" ? MfChecked : 0), IdActive, "Активно");
        AppendMenuW(menu, MfPopup, (nuint)intensity, "Активность");

        AppendMenuW(menu, MfSeparator, 0, null);
        AppendMenuW(
            menu,
            MfString | (_platformDebug ? MfChecked : 0),
            IdPlatformDebug,
            "Developer: платформы");

        AppendMenuW(menu, MfSeparator, 0, null);
        AppendMenuW(menu, MfString, IdQuit, "Завершить");

        GetCursorPos(out Point cursor);

        // Both calls are the documented dance around a tray menu that would otherwise refuse to
        // close when the user clicks elsewhere.
        SetForegroundWindow(_window);
        uint choice = (uint)TrackPopupMenu(
            menu, TpmRightButton | TpmReturnCmd | TpmNoNotify, cursor.X, cursor.Y, 0, _window, 0);
        PostMessage(_window, 0, 0, 0);
        DestroyMenu(menu);

        switch (choice)
        {
            case IdOpenLauncher:
                _commands.Enqueue(TrayCommand.OpenLauncher);
                break;
            case IdVisibility:
                _visible = !_visible;
                _commands.Enqueue(_visible ? TrayCommand.ShowCharacter : TrayCommand.HideCharacter);
                break;
            case IdPause:
                _paused = !_paused;
                _commands.Enqueue(_paused ? TrayCommand.Pause : TrayCommand.Resume);
                break;
            case IdCalm:
                _intensity = "calm";
                _commands.Enqueue(TrayCommand.IntensityCalm);
                break;
            case IdNormal:
                _intensity = "normal";
                _commands.Enqueue(TrayCommand.IntensityNormal);
                break;
            case IdActive:
                _intensity = "active";
                _commands.Enqueue(TrayCommand.IntensityActive);
                break;
            case IdPlatformDebug:
                _platformDebug = !_platformDebug;
                _commands.Enqueue(_platformDebug
                    ? TrayCommand.ShowPlatformDebug
                    : TrayCommand.HidePlatformDebug);
                break;
            case IdQuit:
                _commands.Enqueue(TrayCommand.Quit);
                break;
        }
    }

    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassW
    {
        public uint Style;
        public nint LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public nint HInstance;
        public nint HIcon;
        public nint HCursor;
        public nint HbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? LpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string LpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int CbSize;
        public nint HWnd;
        public uint UID;
        public uint UFlags;
        public uint UCallbackMessage;
        public nint HIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string SzTip;
        public uint DwState;
        public uint DwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string SzInfo;
        public uint UVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string SzInfoTitle;
        public uint DwInfoFlags;
        public Guid GuidItem;
        public nint HBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint HWnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Pt;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WndClassW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out Msg message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    /// <summary>uIDNewItem is UINT_PTR, not UINT: for a submenu it carries an HMENU, which does
    /// not fit in 32 bits on x64.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint idNewItem, string? item);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIconW(nint instance, int name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
}
