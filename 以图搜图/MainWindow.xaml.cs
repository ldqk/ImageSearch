using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using 以图搜图.ViewModels;

namespace 以图搜图;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

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
        if (!string.IsNullOrEmpty(ViewModel.ImagePath) && File.Exists(ViewModel.ImagePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ViewModel.ImagePath,
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
            MessageBox.Show("正在索引或写入文件，请稍后再试", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Cancel = true;
        }
    }
}