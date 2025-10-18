using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace 以图搜图.Converters;

public class SpeedHistoryToPathConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not IEnumerable<double> speedHistory || values[1] is not double width || values[2] is not double height || width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        var points = speedHistory.ToList();
        if (points.Count < 2)
        {
            return Geometry.Empty;
        }

        var maxSpeed = points.Max();
        if (maxSpeed <= 0)
        {
            return Geometry.Empty;
        }

        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, height),
            IsClosed = true
        };

        var stepWidth = width / Math.Max(points.Count - 1, 1);

        // 绘制面积图的上边缘
        for (int i = 0; i < points.Count; i++)
        {
            var x = i * stepWidth;
            var y = height - points[i] / maxSpeed * height * 0.9; // 0.9 留出顶部空间

            if (i == 0)
            {
                figure.StartPoint = new Point(x, y);
            }
            else
            {
                figure.Segments.Add(new LineSegment(new Point(x, y), true));
            }
        }

        // 封闭路径 - 右下角和左下角
        figure.Segments.Add(new LineSegment(new Point(width, height), true));
        figure.Segments.Add(new LineSegment(new Point(0, height), true));

        geometry.Figures.Add(figure);
        return geometry;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}