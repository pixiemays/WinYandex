#nullable enable

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI.Notifications;
using WinRT.Interop;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.Views;

namespace YMusic.WinUI.Services.Implementations;

public sealed class TrayNotificationService : ITrayNotificationService
{
    private const uint TrayIconId = 4201;
    private const uint SubclassId = 101;

    private readonly IAudioPlayerService _audioPlayer;
    private readonly ISettingsService _settingsService;

    private ShellWindow? _shellWindow;
    private AppWindow? _appWindow;
    private IntPtr _hWnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isIconAdded;
    private bool _disposed;
    private bool _hasShownMinimizeHint;
    private uint _wmTaskbarCreated;

    private readonly SubclassProc _subclassProc;

    public bool IsExplicitExit { get; set; }

    public TrayNotificationService(
        IAudioPlayerService audioPlayer,
        ISettingsService settingsService)
    {
        _audioPlayer = audioPlayer;
        _settingsService = settingsService;
        _subclassProc = SubclassWindowProc;
    }

    public void Initialize(ShellWindow shellWindow)
    {
        if (_hWnd != IntPtr.Zero) return;

        _shellWindow = shellWindow;
        _appWindow = shellWindow.AppWindow;
        _hWnd = WindowNative.GetWindowHandle(shellWindow);

        _wmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        LoadTrayIcon();
        AddTrayIcon();

        SetWindowSubclass(_hWnd, _subclassProc, (UIntPtr)SubclassId, IntPtr.Zero);

        _audioPlayer.CurrentTrackChanged += OnCurrentTrackChanged;
        _audioPlayer.IsPlayingChanged += OnIsPlayingChanged;

        UpdateTooltip("YMusic");
    }

    private void LoadTrayIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            }

            if (_hIcon == IntPtr.Zero)
            {
                // Fallback to standard window icon
                _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayService] Error loading icon: {ex.Message}");
        }
    }

    private void AddTrayIcon()
    {
        if (_hWnd == IntPtr.Zero || _isIconAdded) return;

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "YMusic"
        };

        _isIconAdded = Shell_NotifyIconW(NIM_ADD, ref nid);

        // Request modern NotifyIcon behavior
        nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref nid);
    }

    private void RemoveTrayIcon()
    {
        if (!_isIconAdded || _hWnd == IntPtr.Zero) return;

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId
        };

        Shell_NotifyIconW(NIM_DELETE, ref nid);
        _isIconAdded = false;
    }

    private void UpdateTooltip(string text)
    {
        if (!_isIconAdded || _hWnd == IntPtr.Zero) return;

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId,
            uFlags = NIF_TIP,
            szTip = Truncate(text, 127)
        };

        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    public void UpdateTrack(Track? track, bool isPlaying)
    {
        string tooltip = track != null
            ? $"{(isPlaying ? "▶" : "⏸")} {track.FullTitle} • {track.ArtistNames}"
            : "YMusic — Native Windows Client";

        UpdateTooltip(tooltip);
    }

    private void OnCurrentTrackChanged(object? sender, Track? track)
    {
        bool isPlaying = _audioPlayer.IsPlaying;
        UpdateTrack(track, isPlaying);

        // Windows Toast notification if track changes in background
        if (_settingsService.TrackNotificationsEnabled && track != null)
        {
            bool isForeground = _hWnd != IntPtr.Zero &&
                                GetForegroundWindow() == _hWnd &&
                                _appWindow != null &&
                                _appWindow.IsVisible &&
                                !IsIconic(_hWnd);

            if (!isForeground)
            {
                string title = track.FullTitle;
                string subtitle = !string.IsNullOrWhiteSpace(track.AlbumTitle)
                    ? $"{track.ArtistNames} • {track.AlbumTitle}"
                    : track.ArtistNames;

                ShowNotification(title, subtitle);
            }
        }
    }

    private void OnIsPlayingChanged(object? sender, bool isPlaying)
    {
        UpdateTrack(_audioPlayer.CurrentTrack, isPlaying);
    }

    public void ShowNotification(string title, string message)
    {
        if (_disposed) return;

        // Try modern WinRT Toast Notification
        try
        {
            var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textNodes = template.GetElementsByTagName("text");
            if (textNodes.Length > 0)
            {
                textNodes[0].AppendChild(template.CreateTextNode(title));
            }
            if (textNodes.Length > 1)
            {
                textNodes[1].AppendChild(template.CreateTextNode(message));
            }

            var toast = new ToastNotification(template);
            ToastNotificationManager.CreateToastNotifier().Show(toast);
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayService] WinRT Toast failed, using Shell_NotifyIcon fallback: {ex.Message}");
        }

        // Fallback: Shell_NotifyIcon balloon / Action Center banner
        if (_isIconAdded && _hWnd != IntPtr.Zero)
        {
            var nid = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hWnd,
                uID = TrayIconId,
                uFlags = NIF_INFO,
                szInfoTitle = Truncate(title, 63),
                szInfo = Truncate(message, 255),
                dwInfoFlags = NIIF_INFO | NIIF_RESPECT_QUIET_TIME,
                uTimeoutOrVersion = 3000
            };

            Shell_NotifyIconW(NIM_MODIFY, ref nid);
        }
    }

    public void RestoreWindow()
    {
        if (_shellWindow == null || _hWnd == IntPtr.Zero) return;

        if (_appWindow != null && !_appWindow.IsVisible)
        {
            _appWindow.Show();
        }

        ShowWindow(_hWnd, SW_RESTORE);
        SetForegroundWindow(_hWnd);
    }

    public void MinimizeToTray()
    {
        if (_appWindow != null && _appWindow.IsVisible)
        {
            _appWindow.Hide();

            if (!_hasShownMinimizeHint)
            {
                _hasShownMinimizeHint = true;
                ShowNotification(
                    "YMusic is running in the background",
                    "Right-click the taskbar tray icon to control playback or exit.");
            }
        }
    }

    public void ExitApplication()
    {
        IsExplicitExit = true;
        try
        {
            _audioPlayer.SaveCurrentState();
        }
        catch { }

        Dispose();

        try
        {
            _appWindow?.Destroy();
        }
        catch { }

        try
        {
            Application.Current.Exit();
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    private void ShowContextMenu()
    {
        if (_hWnd == IntPtr.Zero) return;

        GetCursorPos(out POINT pt);
        SetForegroundWindow(_hWnd);

        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            var currentTrack = _audioPlayer.CurrentTrack;
            bool isPlaying = _audioPlayer.IsPlaying;

            // 1. Current Track Header (Disabled)
            string trackTitle = currentTrack != null
                ? $"{currentTrack.FullTitle} — {currentTrack.ArtistNames}"
                : "No track playing";
            if (trackTitle.Length > 48)
            {
                trackTitle = trackTitle.Substring(0, 45) + "...";
            }
            AppendMenuW(hMenu, MF_STRING | MF_DISABLED, (UIntPtr)ID_HEADER, $"🎵  {trackTitle}");
            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);

            // 2. Play / Pause
            string playPauseText = isPlaying ? "⏸   Pause" : "▶   Play";
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_PLAYPAUSE, playPauseText);

            // 3. Next / Previous Track
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_NEXT, "⏭   Next Track");
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_PREV, "⏮   Previous Track");
            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);

            // 4. Show Window
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_RESTORE, "🗖   Open YMusic");
            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);

            // 5. Exit
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_EXIT, "✕   Exit YMusic");

            // Track menu and execute selected command synchronously
            uint cmd = TrackPopupMenuEx(
                hMenu,
                TPM_LEFTALIGN | TPM_BOTTOMALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD,
                pt.X,
                pt.Y,
                _hWnd,
                IntPtr.Zero);

            switch (cmd)
            {
                case ID_PLAYPAUSE:
                    _audioPlayer.TogglePlayPause();
                    break;

                case ID_NEXT:
                    _ = _audioPlayer.NextAsync();
                    break;

                case ID_PREV:
                    _ = _audioPlayer.PreviousAsync();
                    break;

                case ID_RESTORE:
                    RestoreWindow();
                    break;

                case ID_EXIT:
                    ExitApplication();
                    break;
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private IntPtr SubclassWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_TRAYICON)
        {
            uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK || mouseMsg == NIN_BALLOONUSERCLICK)
            {
                _shellWindow?.DispatcherQueue?.TryEnqueue(RestoreWindow);
                return IntPtr.Zero;
            }
            else if (mouseMsg == WM_RBUTTONUP || mouseMsg == WM_CONTEXTMENU)
            {
                _shellWindow?.DispatcherQueue?.TryEnqueue(ShowContextMenu);
                return IntPtr.Zero;
            }
        }
        else if (uMsg != 0 && uMsg == _wmTaskbarCreated)
        {
            // Taskbar was recreated (e.g. Explorer restart), restore tray icon
            _isIconAdded = false;
            AddTrayIcon();
            UpdateTrack(_audioPlayer.CurrentTrack, _audioPlayer.IsPlaying);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private static string Truncate(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return string.Empty;
        return str.Length <= maxLength ? str : str.Substring(0, maxLength);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _audioPlayer.CurrentTrackChanged -= OnCurrentTrackChanged;
        _audioPlayer.IsPlayingChanged -= OnIsPlayingChanged;

        if (_hWnd != IntPtr.Zero)
        {
            RemoveWindowSubclass(_hWnd, _subclassProc, (UIntPtr)SubclassId);
            RemoveTrayIcon();
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    #region Win32 API Definitions

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_RESPECT_QUIET_TIME = 0x00000080;

    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYICON = WM_USER + 2048;

    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint NIN_BALLOONUSERCLICK = WM_USER + 5;

    private const int SW_RESTORE = 9;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_DISABLED = 0x00000002;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint ID_HEADER = 1001;
    private const uint ID_PLAYPAUSE = 1002;
    private const uint ID_NEXT = 1003;
    private const uint ID_PREV = 1004;
    private const uint ID_RESTORE = 1005;
    private const uint ID_EXIT = 1006;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    #endregion
}
