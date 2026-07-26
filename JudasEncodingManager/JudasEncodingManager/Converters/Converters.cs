using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using JudasEncodingManager.Models;

namespace JudasEncodingManager.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            return new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "Active" : "Inactive";
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class UnsavedChangesColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool hasChanges && hasChanges)
                return new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return false;
        }
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class TestModeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isTestMode && isTestMode)
                return new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
            return new SolidColorBrush(Color.FromRgb(22, 33, 62)); // Surface color
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ConnectionStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value as string ?? "";
            return status.ToLower() switch
            {
                "connected" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),    // Green
                "connecting" => new SolidColorBrush(Color.FromRgb(255, 193, 7)),   // Yellow
                "disconnected" => new SolidColorBrush(Color.FromRgb(244, 67, 54)), // Red
                _ => new SolidColorBrush(Color.FromRgb(158, 158, 158))             // Gray
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class MonitoringStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value as string ?? "";
            return status.ToLower() switch
            {
                "monitoring" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),    // Green
                "processing" => new SolidColorBrush(Color.FromRgb(33, 150, 243)),   // Blue
                "paused" => new SolidColorBrush(Color.FromRgb(255, 193, 7)),        // Yellow
                "stopped" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),       // Red
                _ => new SolidColorBrush(Color.FromRgb(158, 158, 158))              // Gray
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class QueueStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is QueueItemStatus status)
            {
                return status switch
                {
                    QueueItemStatus.Pending => new SolidColorBrush(Color.FromRgb(158, 158, 158)),      // Gray
                    QueueItemStatus.Downloading => new SolidColorBrush(Color.FromRgb(33, 150, 243)),   // Blue
                    QueueItemStatus.DownloadComplete => new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Green
                    QueueItemStatus.AnalyzingTracks => new SolidColorBrush(Color.FromRgb(156, 39, 176)), // Purple
                    QueueItemStatus.Encoding => new SolidColorBrush(Color.FromRgb(255, 152, 0)),       // Orange
                    QueueItemStatus.EncodingComplete => new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Green
                    QueueItemStatus.Muxing => new SolidColorBrush(Color.FromRgb(156, 39, 176)),        // Purple
                    QueueItemStatus.TakingScreenshots => new SolidColorBrush(Color.FromRgb(0, 188, 212)), // Cyan
                    QueueItemStatus.UploadingScreenshots => new SolidColorBrush(Color.FromRgb(0, 188, 212)), // Cyan
                    QueueItemStatus.GeneratingDescription => new SolidColorBrush(Color.FromRgb(156, 39, 176)), // Purple
                    QueueItemStatus.CreatingTorrent => new SolidColorBrush(Color.FromRgb(156, 39, 176)), // Purple
                    QueueItemStatus.UploadingTorrentFile or
                    QueueItemStatus.UploadingDescription or
                    QueueItemStatus.UploadingEpisode => new SolidColorBrush(Color.FromRgb(255, 87, 34)), // Deep Orange
                    QueueItemStatus.AddingToSeedbox => new SolidColorBrush(Color.FromRgb(0, 150, 136)), // Teal
                    QueueItemStatus.PostingToNyaa => new SolidColorBrush(Color.FromRgb(233, 30, 99)),  // Pink
                    QueueItemStatus.Completed => new SolidColorBrush(Color.FromRgb(76, 175, 80)),      // Green
                    QueueItemStatus.Error or
                    QueueItemStatus.DownloadFailed or
                    QueueItemStatus.EncodingFailed or
                    QueueItemStatus.MuxingFailed or
                    QueueItemStatus.UploadFailed => new SolidColorBrush(Color.FromRgb(244, 67, 54)),   // Red
                    QueueItemStatus.Paused => new SolidColorBrush(Color.FromRgb(255, 193, 7)),         // Yellow
                    _ => new SolidColorBrush(Color.FromRgb(158, 158, 158))
                };
            }
            return new SolidColorBrush(Color.FromRgb(158, 158, 158));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class LogLevelColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ActivityLogLevel level)
            {
                return level switch
                {
                    ActivityLogLevel.Debug => new SolidColorBrush(Color.FromRgb(158, 158, 158)),   // Gray
                    ActivityLogLevel.Info => new SolidColorBrush(Color.FromRgb(176, 176, 176)),    // Light Gray
                    ActivityLogLevel.Warning => new SolidColorBrush(Color.FromRgb(255, 152, 0)),   // Orange
                    ActivityLogLevel.Error => new SolidColorBrush(Color.FromRgb(244, 67, 54)),     // Red
                    ActivityLogLevel.Success => new SolidColorBrush(Color.FromRgb(76, 175, 80)),   // Green
                    _ => new SolidColorBrush(Color.FromRgb(176, 176, 176))
                };
            }
            return new SolidColorBrush(Color.FromRgb(176, 176, 176));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class EncodingStatusToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is QueueItemStatus status)
            {
                // Show progress for encoding-related statuses
                var showProgress = status == QueueItemStatus.Downloading ||
                                  status == QueueItemStatus.Encoding ||
                                  status == QueueItemStatus.Muxing ||
                                  status == QueueItemStatus.UploadingTorrentFile ||
                                  status == QueueItemStatus.UploadingDescription ||
                                  status == QueueItemStatus.UploadingEpisode;
                return showProgress ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? 1.0 : 0.4;
            return 0.4;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

}
