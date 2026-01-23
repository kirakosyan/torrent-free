using System.Globalization;
using TorrentFree.Models;

namespace TorrentFree.Converters;

/// <summary>
/// Converts a boolean value to its inverse.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// Converts a string to a boolean indicating if it's not empty.
/// </summary>
public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a reference to a boolean indicating if it's not null.
/// </summary>
public class ObjectNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts download progress (0-100) to ProgressBar progress (0-1).
/// </summary>
public class ProgressConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double progress)
        {
            return progress / 100.0;
        }
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double progress)
        {
            return progress * 100.0;
        }
        return 0.0;
    }
}

/// <summary>
/// Converts DownloadStatus to a color for the status badge.
/// </summary>
public class StatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DownloadStatus status)
        {
            return status switch
            {
                DownloadStatus.Queued => Color.FromArgb("#FFA726"),
                DownloadStatus.Downloading => Color.FromArgb("#42A5F5"),
                DownloadStatus.Paused => Color.FromArgb("#BDBDBD"),
                DownloadStatus.Completed => Color.FromArgb("#66BB6A"),
                DownloadStatus.Seeding => Color.FromArgb("#26A69A"),
                DownloadStatus.Failed => Color.FromArgb("#EF5350"),
                DownloadStatus.Stopped => Color.FromArgb("#9E9E9E"),
                _ => Color.FromArgb("#9E9E9E")
            };
        }
        return Color.FromArgb("#9E9E9E");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
