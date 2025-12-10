using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace 以图搜图.Converters;

public class ServerStatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo cultureInfo)
    {
        if (value is bool isRunning)
        {
            return isRunning ? new SolidColorBrush(Color.FromArgb(255, 40, 167, 69)) : new SolidColorBrush(Color.FromArgb(255, 220, 53, 69));
        }
        return new SolidColorBrush(Color.FromArgb(255, 220, 53, 69));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo cultureInfo)
    {
        throw new NotImplementedException();
    }
}