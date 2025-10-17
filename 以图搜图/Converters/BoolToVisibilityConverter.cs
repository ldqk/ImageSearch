using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace 以图搜图;

public class BoolToVisibilityConverter : IValueConverter
{
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
  {
    if (value is bool boolValue)
    {
      return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }
    return Visibility.Collapsed;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
  {
    if (value is Visibility visibility)
    {
      return visibility == Visibility.Visible;
    }
    return false;
  }
}
