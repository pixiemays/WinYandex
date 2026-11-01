#nullable enable

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Views;

public sealed partial class GameOverlayWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _eqTimer;
    private IntPtr _hWnd = IntPtr.Zero;
    private int _eqStep;
    private SubclassProc? _subclassDelegate;

    private bool _isCardVisible;
    private double _currentAlpha = 0;
    private double _targetAlpha = 0;
    private double _animationStartAlpha = 0;
    private DateTime _animationStartTime;
    private double _animationDurationMs;
    private Action? _onAnimationCompleted;

    private const int OverlayWidth = 370;
    private const int OverlayHeight = 88;
    private const int ScreenMargin = 20;

    private static readonly int[][] EqPatterns = new[]
    {
        new[] { 14, 6, 12 },
        new[] { 8, 14, 6 },
        new[] { 12, 8, 14 },
        new[] { 6, 12, 8 },
        new[] { 15, 10, 5 },
        new[] { 10, 15, 11 }
    };

    public GameOverlayWindow(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();

        AppWindow.Title = "WinYandex Game HUD";
        AppWindow.IsShownInSwitchers = false;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _hWnd = WindowNative.GetWindowHandle(this);

        _subclassDelegate = OverlaySubclassProc;
        SetWindowSubclass(_hWnd, _subclassDelegate, (UIntPtr)101, IntPtr.Zero);

        ApplyWindowStyles();
        PositionWindow();

        // Initially fully transparent at the OS window manager level
        SetLayeredWindowAttributes(_hWnd, 0, 0, LWA_ALPHA);
        RootCard.Opacity = 1.0;
        RootCard.RequestedTheme = ElementTheme.Dark;

        _eqTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _eqTimer.Tick += (s, e) =>
        {
            _eqStep = (_eqStep + 1) % EqPatterns.Length;
            var pat = EqPatterns[_eqStep];
            if (Bar1 != null && Bar2 != null && Bar3 != null)
            {
                Bar1.Height = pat[0];
                Bar2.Height = pat[1];
                Bar3.Height = pat[2];
            }
        };

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            _eqTimer.Stop();
            HideWindow();
        };
    }

    public void ShowOverlay(Track? track, string actionBadge, bool isLiked, bool isPlaying)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ActionBadgeText.Text = string.IsNullOrWhiteSpace(actionBadge) ? "WINYANDEX HUD" : actionBadge.ToUpperInvariant();

                if (track != null)
                {
                    TitleText.Text = track.Title ?? "Без названия";
                    ArtistText.Text = track.Artists?.Count > 0
                        ? string.Join(", ", track.Artists.Select(a => a.Name))
                        : "Неизвестный исполнитель";

                    string? coverUrl = track.CoverUrl;
                    if (!string.IsNullOrWhiteSpace(coverUrl) && Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri))
                    {
                        CoverImage.Source = new BitmapImage(uri);
                    }
                    else
                    {
                        CoverImage.Source = null;
                    }
                }
                else
                {
                    TitleText.Text = "Трек не выбран";
                    ArtistText.Text = "Ожидание воспроизведения";
                    CoverImage.Source = null;
                }

                LikeIcon.Visibility = isLiked ? Visibility.Visible : Visibility.Collapsed;
                EqualizerBars.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;

                PositionWindow();

                // Show window without stealing focus
                ShowWindow(_hWnd, SW_SHOWNOACTIVATE);

                int duration = Math.Max(2, _settingsService.GameOverlayDurationSeconds);
                _hideTimer.Interval = TimeSpan.FromSeconds(duration);
                _hideTimer.Stop();
                _hideTimer.Start();

                if (isPlaying)
                {
                    _eqTimer.Start();
                }
                else
                {
                    _eqTimer.Stop();
                }

                if (!_isCardVisible)
                {
                    PlayEntranceAnimation();
                }
                else
                {
                    PlayUpdatePulse();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameOverlayWindow] ShowOverlay error: {ex.Message}");
            }
        });
    }

    public void HideWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                PlayExitAnimation(() =>
                {
                    ShowWindow(_hWnd, SW_HIDE);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameOverlayWindow] HideWindow error: {ex.Message}");
            }
        });
    }

    private void PlayEntranceAnimation()
    {
        _isCardVisible = true;
        StartFadeAnimation(255.0, 220.0);
    }

    private void PlayExitAnimation(Action onCompleted)
    {
        if (!_isCardVisible)
        {
            onCompleted?.Invoke();
            return;
        }

        StartFadeAnimation(0.0, 200.0, () =>
        {
            _isCardVisible = false;
            onCompleted?.Invoke();
        });
    }

    private void StartFadeAnimation(double targetAlpha, double durationMs, Action? onCompleted = null)
    {
        _animationStartAlpha = _currentAlpha;
        _targetAlpha = targetAlpha;
        _animationDurationMs = Math.Max(10.0, durationMs);
        _animationStartTime = DateTime.UtcNow;
        _onAnimationCompleted = onCompleted;

        CompositionTarget.Rendering -= OnCompositionRendering;
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void OnCompositionRendering(object? sender, object e)
    {
        double elapsed = (DateTime.UtcNow - _animationStartTime).TotalMilliseconds;
        double progress = Math.Clamp(elapsed / _animationDurationMs, 0.0, 1.0);

        // Smooth cubic ease: ease-out for appearing, ease-in for disappearing
        double easedProgress;
        if (_targetAlpha > _animationStartAlpha)
        {
            // Ease out: 1 - (1 - t)^3
            double inv = 1.0 - progress;
            easedProgress = 1.0 - (inv * inv * inv);
        }
        else
        {
            // Ease in: t * t * t
            easedProgress = progress * progress * progress;
        }

        _currentAlpha = _animationStartAlpha + (_targetAlpha - _animationStartAlpha) * easedProgress;
        byte byteAlpha = (byte)Math.Clamp((int)Math.Round(_currentAlpha), 0, 255);
        SetLayeredWindowAttributes(_hWnd, 0, byteAlpha, LWA_ALPHA);

        if (progress >= 1.0)
        {
            CompositionTarget.Rendering -= OnCompositionRendering;
            _currentAlpha = _targetAlpha;
            SetLayeredWindowAttributes(_hWnd, 0, (byte)Math.Clamp((int)Math.Round(_targetAlpha), 0, 255), LWA_ALPHA);

            var cb = _onAnimationCompleted;
            _onAnimationCompleted = null;
            cb?.Invoke();
        }
    }

    private void PlayUpdatePulse()
    {
        var sb = new Storyboard();
        var pulse = new DoubleAnimation
        {
            From = 0.4,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(pulse, ActionBadgeText);
        Storyboard.SetTargetProperty(pulse, "Opacity");
        sb.Children.Add(pulse);
        sb.Begin();
    }

    private void PositionWindow()
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            uint dpi = GetDpiForWindow(_hWnd);
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;
            int physWidth = (int)Math.Round(OverlayWidth * scale);
            int physHeight = (int)Math.Round(OverlayHeight * scale);
            int cornerRadius = (int)Math.Round(18 * scale);

            int x = _settingsService.GameOverlayPosition switch
            {
                1 /* TopLeft */ => workArea.X + (int)(ScreenMargin * scale),
                4 /* TopCenter */ => workArea.X + (workArea.Width - physWidth) / 2,
                0 /* TopRight */ => (workArea.X + workArea.Width) - physWidth - (int)(ScreenMargin * scale),
                6 /* MiddleLeft */ => workArea.X + (int)(ScreenMargin * scale),
                7 /* MiddleRight */ => (workArea.X + workArea.Width) - physWidth - (int)(ScreenMargin * scale),
                3 /* BottomLeft */ => workArea.X + (int)(ScreenMargin * scale),
                5 /* BottomCenter */ => workArea.X + (workArea.Width - physWidth) / 2,
                2 /* BottomRight */ => (workArea.X + workArea.Width) - physWidth - (int)(ScreenMargin * scale),
                _ /* Default: TopRight */ => (workArea.X + workArea.Width) - physWidth - (int)(ScreenMargin * scale)
            };

            int y = _settingsService.GameOverlayPosition switch
            {
                1 or 4 or 0 /* Top */ => workArea.Y + (int)(ScreenMargin * scale),
                6 or 7 /* Middle */ => workArea.Y + (workArea.Height - physHeight) / 2,
                3 or 5 or 2 /* Bottom */ => (workArea.Y + workArea.Height) - physHeight - (int)(ScreenMargin * scale),
                _ => workArea.Y + (int)(ScreenMargin * scale)
            };

            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, physWidth, physHeight));

            // Clip the window exactly to the rounded rectangle so zero black background is drawn outside the card!
            IntPtr hRgn = CreateRoundRectRgn(0, 0, physWidth + 1, physHeight + 1, cornerRadius * 2, cornerRadius * 2);
            if (SetWindowRgn(_hWnd, hRgn, true) == 0)
            {
                DeleteObject(hRgn);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlayWindow] PositionWindow error: {ex.Message}");
        }
    }

    private const uint WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    private IntPtr OverlaySubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_NCHITTEST)
        {
            return new IntPtr(HTTRANSPARENT);
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ApplyWindowStyles()
    {
        try
        {
            // Strip caption, thickframe and standard borders from GWL_STYLE so Windows treats this as a pure borderless popup
            const int GWL_STYLE = -16;
            const long WS_CAPTION = 0x00C00000L;
            const long WS_THICKFRAME = 0x00040000L;
            const long WS_BORDER = 0x00800000L;
            const long WS_DLGFRAME = 0x00400000L;
            const long WS_POPUP = 0x80000000L;

            IntPtr curStyle = GetWindowLongPtrUniversal(_hWnd, GWL_STYLE);
            long newStyle = (curStyle.ToInt64() & ~(WS_CAPTION | WS_THICKFRAME | WS_BORDER | WS_DLGFRAME)) | WS_POPUP;
            SetWindowLongPtrUniversal(_hWnd, GWL_STYLE, new IntPtr(newStyle));

            const int GWL_EXSTYLE = -20;
            const long WS_EX_TOOLWINDOW = 0x00000080L;
            const long WS_EX_NOACTIVATE = 0x08000000L;
            const long WS_EX_TOPMOST = 0x00000008L;
            const long WS_EX_TRANSPARENT = 0x00000020L;
            const long WS_EX_LAYERED = 0x00080000L;

            IntPtr curEx = GetWindowLongPtrUniversal(_hWnd, GWL_EXSTYLE);
            long newEx = curEx.ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED;
            SetWindowLongPtrUniversal(_hWnd, GWL_EXSTYLE, new IntPtr(newEx));

            // Inform the window manager to strip non-client frames and borders immediately
            SetWindowPos(_hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // Set dark mode and rounded corners in DWM
            int darkMode = 1;
            DwmSetWindowAttribute(_hWnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref darkMode, sizeof(int));
            int roundCorner = 2; /* DWMWCP_ROUND */
            DwmSetWindowAttribute(_hWnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref roundCorner, sizeof(int));

            // Remove DWM border and caption frame colors (DWMWA_COLOR_NONE = 0xFFFFFFFE)
            int noneColor = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(_hWnd, 34 /* DWMWA_BORDER_COLOR */, ref noneColor, sizeof(int));
            DwmSetWindowAttribute(_hWnd, 35 /* DWMWA_CAPTION_COLOR */, ref noneColor, sizeof(int));

            // Extend frame into client area so DWM treats the entire window with desktop composition
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(_hWnd, ref margins);

            // Enable blur behind with an empty region to force transparent composition
            IntPtr emptyRgn = CreateRectRgn(0, 0, 0, 0);
            var blurBehind = new DWM_BLURBEHIND
            {
                dwFlags = 0x00000001 | 0x00000002, // DWM_BB_ENABLE | DWM_BB_BLURREGION
                fEnable = true,
                hRgnBlur = emptyRgn
            };
            DwmEnableBlurBehindWindow(_hWnd, ref blurBehind);
            DeleteObject(emptyRgn);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlayWindow] ApplyWindowStyles error: {ex.Message}");
        }
    }

    #region Win32 P/Invoke

    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint LWA_ALPHA = 0x00000002;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fEnable;
        public IntPtr hRgnBlur;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fTransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hWnd, ref DWM_BLURBEHIND pBlurBehind);

    private static IntPtr GetWindowLongPtrUniversal(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    private static IntPtr SetWindowLongPtrUniversal(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    #endregion
}
