#nullable enable

using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Controls;

public sealed partial class NowPlayingDrawerControl : UserControl
{
    public PlayerBarViewModel ViewModel => App.GetService<PlayerBarViewModel>();

    private bool _isProgrammaticScrolling;
    private DispatcherQueueTimer? _programmaticScrollResetTimer;
    private DispatcherQueueTimer? _smoothCenterTimer;

    public NowPlayingDrawerControl()
    {
        InitializeComponent();
        Visibility = ViewModel.IsNowPlayingDrawerOpen ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlayerBarViewModel.IsNowPlayingDrawerOpen))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    Visibility = ViewModel.IsNowPlayingDrawerOpen ? Visibility.Visible : Visibility.Collapsed;
                    if (ViewModel.IsNowPlayingDrawerOpen)
                    {
                        UpdateFullscreenIcon();
                        if (ViewModel.ActiveLyricLine != null)
                        {
                            ScrollLyricIntoCenter(ViewModel.ActiveLyricLine);
                        }
                    }
                });
            }
        };

        SeekSlider.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(SeekSlider_PointerPressed), true);
        SeekSlider.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(SeekSlider_PointerReleased), true);
        SeekSlider.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(SeekSlider_PointerCaptureLost), true);
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
        if (e.ClickedItem is LyricLine line)
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
}
