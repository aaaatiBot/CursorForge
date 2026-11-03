using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CursorForge.Client.ViewModels;

namespace CursorForge.Client.Views;

public partial class UploadView : UserControl
{
    public UploadView()
    {
        InitializeComponent();
    }

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (sender is Border border)
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF));
                border.Background = new SolidColorBrush(Color.FromArgb(0x28, 0x4C, 0xC2, 0xFF));
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDropZoneDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
            border.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
        }
    }

    private void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        OnDropZoneDragLeave(sender, e);
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                if (DataContext is UploadViewModel vm)
                {
                    vm.LoadFile(files[0]);
                }
            }
        }
        e.Handled = true;
    }

    private void OnPreviewCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && DataContext is UploadViewModel vm && vm.ImageWidth > 0 && vm.ImageHeight > 0)
        {
            Point pos = e.GetPosition(elem);
            int x = Math.Clamp((int)Math.Round(pos.X), 0, vm.ImageWidth - 1);
            int y = Math.Clamp((int)Math.Round(pos.Y), 0, vm.ImageHeight - 1);
            vm.HotspotX = x;
            vm.HotspotY = y;
        }
    }
}
