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
        bool b = value switch
        {
            bool flag => flag,
            null => false,
            _ => true
        };

        bool shouldInvert = Invert;
        if (parameter is string paramStr && paramStr.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            shouldInvert = !shouldInvert;
        }

        if (shouldInvert)
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
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool hasValue = value != null;
        if (value is int count) hasValue = count > 0;
        else if (value is long lCount) hasValue = lCount > 0;
        else if (value is string s) hasValue = !string.IsNullOrWhiteSpace(s);

        bool shouldInvert = Invert;
        if (parameter is string paramStr && paramStr.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            shouldInvert = !shouldInvert;
        }

        if (shouldInvert)
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isNull = value == null;
        if (value is int count) isNull = count <= 0;
        else if (value is long lCount) isNull = lCount <= 0;
        else if (value is string s) isNull = string.IsNullOrWhiteSpace(s);

        return isNull ? Visibility.Visible : Visibility.Collapsed;
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

public sealed class BoolToLikeGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            return "\uEB52";
        }

        return "\uEB51";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToTrackTitleBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush FallbackAccentTextBrush = new(Windows.UI.Color.FromArgb(255, 96, 205, 255));
    private static readonly SolidColorBrush FallbackPrimaryTextBrush = new(Windows.UI.Color.FromArgb(255, 255, 255, 255));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isCurrent && isCurrent)
        {
            if (Application.Current.Resources.TryGetValue("AccentTextFillColorPrimaryBrush", out var accentText) && accentText is Brush atb)
            {
                return atb;
            }
            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accentFill) && accentFill is Brush afb)
            {
                return afb;
            }
            return FallbackAccentTextBrush;
        }

        if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var primaryText) && primaryText is Brush ptb)
        {
            return ptb;
        }

        return FallbackPrimaryTextBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToTrackHighlightBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush FallbackHighlightBrush = new(Windows.UI.Color.FromArgb(18, 255, 255, 255));
    private static readonly SolidColorBrush TransparentBrush = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isCurrent && isCurrent)
        {
            if (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var brush) && brush is Brush b)
            {
                return b;
            }
            return FallbackHighlightBrush;
        }

        return TransparentBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToPillBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) && accent is Brush ab)
                return ab;
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 204, 0));
        }

        if (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var card) && card is Brush cb)
            return cb;

        return new SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToPillForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            if (Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var onAccent) && onAccent is Brush oab)
                return oab;
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
        }

        if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var sec) && sec is Brush sb)
            return sb;

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToPinGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? "\uE840" : "\uE718";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToPinTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? "Unpin from top" : "Pin to top";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToPinButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? "Pinned" : "Pin";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToPinMenuTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? "Unpin from top" : "Pin to top";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToPinForegroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) && accent is Brush ab)
                return ab;
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 204, 0));
        }

        if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var primary) && primary is Brush pb)
            return pb;

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToSubtleHighlightBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush TransparentBrush = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            if (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var brush) && brush is Brush sb)
            {
                return sb;
            }
            return new SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255));
        }

        return TransparentBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class ActiveLyricToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LyricLine line)
        {
            if (line.IsActive)
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            }

            if (line.IsPast)
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(120, 255, 255, 255));
            }

            return new SolidColorBrush(Windows.UI.Color.FromArgb(215, 255, 255, 255));
        }

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
