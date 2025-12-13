using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;
using 以图搜图.ViewModels;

namespace 以图搜图;

public partial class MainWindow
{
    private Polygon? _speedPolygon;

    public MainWindow()
    {
        InitializeComponent();

        // 订阅 ViewModel 的 SpeedHistory 变化
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 创建面积图 Polygon
        _speedPolygon = new Polygon
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x7A, 0xCC))
        };
        SpeedChartCanvas.Children.Add(_speedPolygon);

        if (DataContext is MainViewModel vm)
        {
            vm.SpeedHistory.CollectionChanged += SpeedHistory_CollectionChanged;
        }
    }

    private void SpeedHistory_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateSpeedChart();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void UpdateSpeedChart()
    {
        if (_speedPolygon == null || ViewModel.SpeedHistory.Count == 0)
        {
            if (_speedPolygon != null)
            {
                _speedPolygon.Points.Clear();
                _speedPolygon.Points.Add(new Point(0, 80));
            }
            return;
        }

        var speeds = ViewModel.SpeedHistory.ToArray();
        var maxSpeed = speeds.Max();
        if (maxSpeed <= 0) maxSpeed = 1;

        var points = new PointCollection();

        var width = SpeedChartCanvas.ActualWidth;
        var height = SpeedChartCanvas.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            width = 500;
            height = 80;
        }

        // 起始点(左下角)
        points.Add(new Point(0, height));

        // 绘制数据点
        var step = speeds.Length > 1 ? width / (speeds.Length - 1) : 0;

        for (int i = 0; i < speeds.Length; i++)
        {
            var x = i * step;
            var y = height - (speeds[i] / maxSpeed * height * 0.9); // 留10%边距
            points.Add(new Point(x, y));
        }

        // 结束点(右下角)
        points.Add(new Point((speeds.Length - 1) * step, height));

        _speedPolygon.Points = points;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        await ViewModel.HandleDrop(e.Data);
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("FileContents") || e.Data.GetDataPresent(DataFormats.Bitmap) || e.Data.GetDataPresent(DataFormats.Text))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void TxtDirectory_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                ViewModel.DirectoryPath = files[0];
            }
        }
    }

    private void TxtPic_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                ViewModel.ImagePath = files[0];
            }
        }
    }

    private void Txt_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
    }

    private void DataGrid_KeyUp(object sender, KeyEventArgs e)
    {
        ViewModel.HandleDataGridKeyUp(e.Key, Keyboard.Modifiers);
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedResult != null)
        {
            if (File.Exists(ViewModel.SelectedResult.路径))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ViewModel.SelectedResult.路径,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show(this, "文件不存在，可能发生了移动", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SourceImage_Click(object sender, MouseButtonEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.SourceImagePath) && File.Exists(ViewModel.SourceImagePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ViewModel.SourceImagePath,
                UseShellExecute = true
            });
        }
    }

    private void DestImage_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedResult != null && File.Exists(ViewModel.SelectedResult.路径))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ViewModel.SelectedResult.路径,
                UseShellExecute = true
            });
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ViewModel.SearchFromClipboardCommand.Execute(null);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!ViewModel.CanClose())
        {
            MessageBox.Show(this, "正在索引或写入文件，请稍后再试", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Cancel = true;
        }
    }
}