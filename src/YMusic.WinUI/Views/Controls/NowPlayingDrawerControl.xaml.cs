#nullable enable

using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Controls;

public sealed partial class NowPlayingDrawerControl : UserControl
{
    public PlayerBarViewModel ViewModel => App.GetService<PlayerBarViewModel>();
    public UIElement TitleBarDragRegion => HeaderBarGrid;

    private Storyboard? _currentTransition;
    private bool _isProgrammaticScrolling;
    private DispatcherQueueTimer? _programmaticScrollResetTimer;
    private DispatcherQueueTimer? _smoothCenterTimer;

    public NowPlayingDrawerControl()
    {
        InitializeComponent();

        if (ViewModel.IsNowPlayingDrawerOpen)
        {
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;
            RootDrawerGrid.Opacity = 1.0;
            DrawerTranslateTransform.Y = 0.0;
            CoverScaleTransform.ScaleX = 1.0;
            CoverScaleTransform.ScaleY = 1.0;
        }
        else
        {
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
            RootDrawerGrid.Opacity = 0.0;
            DrawerTranslateTransform.Y = 50.0;
            CoverScaleTransform.ScaleX = 0.94;
            CoverScaleTransform.ScaleY = 0.94;
        }

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlayerBarViewModel.IsNowPlayingDrawerOpen))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ViewModel.IsNowPlayingDrawerOpen)
                    {
                        AnimateOpen();
                    }
                    else
                    {
                        if (Visibility == Visibility.Visible)
                        {
                            AnimateClose();
                        }
                    }
                });
            }
        };

        SeekSlider.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(SeekSlider_PointerPressed), true);
        SeekSlider.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(SeekSlider_PointerReleased), true);
        SeekSlider.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(SeekSlider_PointerCaptureLost), true);

        DrawerVolumeContainer.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(DrawerVolumeContainer_PointerWheelChanged), true);
    }

    private void DrawerVolumeContainer_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
        if (delta != 0)
        {
            double step = delta > 0 ? 2.0 : -2.0;
            ViewModel.ChangeVolume(step);
            e.Handled = true;
        }
    }

    private void DrawerVolumePercentageBadge_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (DrawerVolumePercentageBox != null && DrawerVolumePercentageBadge != null)
        {
            DrawerVolumePercentageBadge.Visibility = Visibility.Collapsed;
            DrawerVolumePercentageBox.Visibility = Visibility.Visible;
            DrawerVolumePercentageBox.Text = Math.Round(ViewModel.Volume).ToString();
            DrawerVolumePercentageBox.Focus(FocusState.Programmatic);
            DrawerVolumePercentageBox.SelectAll();
            e.Handled = true;
        }
    }

    private void DrawerVolumePercentageBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitDrawerVolumeText();
    }

    private void DrawerVolumePercentageBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            CommitDrawerVolumeText();
            DrawerVolumeSlider.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (DrawerVolumePercentageBox != null && DrawerVolumePercentageBadge != null)
            {
                DrawerVolumePercentageBox.Visibility = Visibility.Collapsed;
                DrawerVolumePercentageBadge.Visibility = Visibility.Visible;
            }
            DrawerVolumeSlider.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }

    private void CommitDrawerVolumeText()
    {
        if (DrawerVolumePercentageBox == null || DrawerVolumePercentageBadge == null) return;
        ViewModel.SetVolumeFromInput(DrawerVolumePercentageBox.Text);
        DrawerVolumePercentageBox.Visibility = Visibility.Collapsed;
        DrawerVolumePercentageBadge.Visibility = Visibility.Visible;
    }

    private void SeekSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        ViewModel.StartSeeking();
    }

    private void SeekSlider_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            ViewModel.FinishSeeking(slider.Value);
        }
    }

    private void SeekSlider_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            ViewModel.FinishSeeking(slider.Value);
        }
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveLyricChanged += ViewModel_ActiveLyricChanged;
        LyricsListView.VerticalOffsetChanged += LyricsListView_VerticalOffsetChanged;

        _programmaticScrollResetTimer = DispatcherQueue.CreateTimer();
        _programmaticScrollResetTimer.Interval = TimeSpan.FromMilliseconds(450);
        _programmaticScrollResetTimer.Tick += (s, args) =>
        {
            _programmaticScrollResetTimer.Stop();
            _isProgrammaticScrolling = false;
        };

        _smoothCenterTimer = DispatcherQueue.CreateTimer();
        _smoothCenterTimer.Interval = TimeSpan.FromMilliseconds(80);
        _smoothCenterTimer.Tick += (s, args) =>
        {
            _smoothCenterTimer.Stop();
            if (ViewModel.ActiveLyricLine != null && !ViewModel.IsUserScrolledAway)
            {
                CenterLyricContainer(ViewModel.ActiveLyricLine, false);
            }
        };

        if (ViewModel.ActiveLyricLine != null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ScrollLyricIntoCenter(ViewModel.ActiveLyricLine);
            });
        }

        UpdateFullscreenIcon();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveLyricChanged -= ViewModel_ActiveLyricChanged;
        LyricsListView.VerticalOffsetChanged -= LyricsListView_VerticalOffsetChanged;
        _programmaticScrollResetTimer?.Stop();
        _smoothCenterTimer?.Stop();
    }

    private void ViewModel_ActiveLyricChanged(object? sender, LyricLine? line)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (line != null && !ViewModel.IsUserScrolledAway && ViewModel.IsAutoScrollEnabled && ViewModel.IsNowPlayingDrawerOpen)
            {
                ScrollLyricIntoCenter(line);
            }
        });
    }

    private void LyricsListView_VerticalOffsetChanged(object? sender, double offset)
    {
        if (_isProgrammaticScrolling)
        {
            return;
        }

        if (ViewModel.ActiveLyricLine != null && !ViewModel.IsUserScrolledAway)
        {
            ViewModel.IsUserScrolledAway = true;
        }
    }

    private void ScrollLyricIntoCenter(LyricLine line)
    {
        if (LyricsListView == null || line == null)
        {
            return;
        }

        _isProgrammaticScrolling = true;
        _programmaticScrollResetTimer?.Stop();
        _programmaticScrollResetTimer?.Start();

        bool centered = CenterLyricContainer(line, false);
        if (!centered)
        {
            LyricsListView.ScrollIntoView(line);
            _smoothCenterTimer?.Stop();
            _smoothCenterTimer?.Start();
        }
    }

    private bool CenterLyricContainer(LyricLine line, bool disableAnimation)
    {
        try
        {
            var sv = LyricsListView.InnerScrollViewer;
            if (sv == null)
            {
                return false;
            }

            var container = LyricsListView.ContainerFromItem(line) as ListViewItem;
            if (container == null)
            {
                return false;
            }

            var transform = container.TransformToVisual(sv);
            var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            double targetOffset = sv.VerticalOffset + pt.Y - (sv.ViewportHeight / 2.0) + (container.ActualHeight / 2.0);
            targetOffset = Math.Max(0, Math.Min(targetOffset, sv.ScrollableHeight));

            sv.ChangeView(null, targetOffset, null, disableAnimation);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LyricsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricLine line && line.HasTimestamp && line.Timestamp >= TimeSpan.Zero)
        {
            ViewModel.SeekToLyricCommand.Execute(line);
            ScrollLyricIntoCenter(line);
        }
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.ToggleFullScreen();
        UpdateFullscreenIcon();
    }

    private void UpdateFullscreenIcon()
    {
        if (FullscreenIcon == null) return;
        bool isFull = App.MainWindow?.IsFullScreen == true;
        FullscreenIcon.Glyph = isFull ? "\uE1D9" : "\uE740"; // Exit vs Enter Fullscreen
    }

    public void AnimateOpen()
    {
        _currentTransition?.Stop();
        _currentTransition = null;

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;

        UpdateFullscreenIcon();
        if (ViewModel.ActiveLyricLine != null)
        {
            ScrollLyricIntoCenter(ViewModel.ActiveLyricLine);
        }

        var sb = new Storyboard();

        // 1. Root Slide Up (50 -> 0)
        var slideAnim = new DoubleAnimation
        {
            From = DrawerTranslateTransform.Y != 0 ? DrawerTranslateTransform.Y : 50.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideAnim, DrawerTranslateTransform);
        Storyboard.SetTargetProperty(slideAnim, "Y");
        sb.Children.Add(slideAnim);

        // 2. Root Fade In (0 -> 1)
        var fadeAnim = new DoubleAnimation
        {
            From = RootDrawerGrid.Opacity < 1.0 ? RootDrawerGrid.Opacity : 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeAnim, RootDrawerGrid);
        Storyboard.SetTargetProperty(fadeAnim, "Opacity");
        sb.Children.Add(fadeAnim);

        // 3. Cover Scale In (0.94 -> 1.0)
        var scaleXAnim = new DoubleAnimation
        {
            From = CoverScaleTransform.ScaleX != 1.0 ? CoverScaleTransform.ScaleX : 0.94,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleXAnim, CoverScaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
        sb.Children.Add(scaleXAnim);

        var scaleYAnim = new DoubleAnimation
        {
            From = CoverScaleTransform.ScaleY != 1.0 ? CoverScaleTransform.ScaleY : 0.94,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleYAnim, CoverScaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
        sb.Children.Add(scaleYAnim);

        sb.Completed += (s, e) =>
        {
            _currentTransition = null;
            RootDrawerGrid.Opacity = 1.0;
            DrawerTranslateTransform.Y = 0.0;
            CoverScaleTransform.ScaleX = 1.0;
            CoverScaleTransform.ScaleY = 1.0;
        };

        _currentTransition = sb;
        sb.Begin();
    }

    public void AnimateClose(Action? onCompleted = null)
    {
        _currentTransition?.Stop();
        _currentTransition = null;

        IsHitTestVisible = false;

        var sb = new Storyboard();

        // 1. Root Slide Down (current Y -> 50)
        var slideAnim = new DoubleAnimation
        {
            From = DrawerTranslateTransform.Y,
            To = 50.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(slideAnim, DrawerTranslateTransform);
        Storyboard.SetTargetProperty(slideAnim, "Y");
        sb.Children.Add(slideAnim);

        // 2. Root Fade Out (current Opacity -> 0)
        var fadeAnim = new DoubleAnimation
        {
            From = RootDrawerGrid.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(190),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fadeAnim, RootDrawerGrid);
        Storyboard.SetTargetProperty(fadeAnim, "Opacity");
        sb.Children.Add(fadeAnim);

        // 3. Cover Scale Out (current Scale -> 0.94)
        var scaleXAnim = new DoubleAnimation
        {
            From = CoverScaleTransform.ScaleX,
            To = 0.94,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleXAnim, CoverScaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
        sb.Children.Add(scaleXAnim);

        var scaleYAnim = new DoubleAnimation
        {
            From = CoverScaleTransform.ScaleY,
            To = 0.94,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleYAnim, CoverScaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
        sb.Children.Add(scaleYAnim);

        sb.Completed += (s, e) =>
        {
            _currentTransition = null;
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = true;
            RootDrawerGrid.Opacity = 1.0;
            DrawerTranslateTransform.Y = 0.0;
            CoverScaleTransform.ScaleX = 1.0;
            CoverScaleTransform.ScaleY = 1.0;
            onCompleted?.Invoke();
        };

        _currentTransition = sb;
        sb.Begin();
    }
}
