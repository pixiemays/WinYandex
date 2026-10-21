#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using YMusic.WinUI.Models;

namespace YMusic.WinUI.Converters;

public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string url && !string.IsNullOrWhiteSpace(url))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return new BitmapImage(uri);
            }
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            return (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        }

        return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class RepeatToAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is RepeatMode mode && mode != RepeatMode.None)
        {
            return (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        }

        return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool flag && flag;
        if (Invert)
        {
            b = !b;
        }

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class SecondsToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double seconds = 0;
        if (value is double d) seconds = d;
        else if (value is float f) seconds = f;
        else if (value is int i) seconds = i;
        else if (value is string s && double.TryParse(s, out var parsed)) seconds = parsed;

        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToLikeBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush LikedBrush = new(Windows.UI.Color.FromArgb(255, 255, 64, 129));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            return LikedBrush;
        }

        return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
