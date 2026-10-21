#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Controls;

public sealed partial class PlayerBarControl : UserControl
{
    public PlayerBarViewModel ViewModel { get; }

    public PlayerBarControl()
    {
        ViewModel = App.GetService<PlayerBarViewModel>();
        InitializeComponent();
        Bindings.Update();

        SeekSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(SeekSlider_PointerPressed), true);
        SeekSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(SeekSlider_PointerReleased), true);
        SeekSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(SeekSlider_PointerCaptureLost), true);
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        Bindings.Update();
    }

    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.StartSeeking();
    }

    private void SeekSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            ViewModel.FinishSeeking(slider.Value);
        }
    }

    private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            ViewModel.FinishSeeking(slider.Value);
        }
    }

    private void CoverButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (CoverHoverOverlay != null)
        {
            CoverHoverOverlay.Opacity = 1;
        }
    }

    private void CoverButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (CoverHoverOverlay != null)
        {
            CoverHoverOverlay.Opacity = 0;
        }
    }
}
