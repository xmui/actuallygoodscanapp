using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScanApp.App.ViewModels;

namespace ScanApp.App.Views;

public partial class ScanView : UserControl
{
    private Point _dragStart;

    public ScanView() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnPreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (Vm is { } vm)
        {
            vm.Zoom += e.Delta > 0 ? 0.2 : -0.2;
            e.Handled = true; // wheel zooms rather than scrolls
        }
    }

    private void OnFilmstripSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is { } vm && sender is ListBox lb)
        {
            vm.SetSelectedPages(lb.SelectedItems.OfType<PageViewModel>());
        }
    }

    private void OnFilmstripMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(null);

    private void OnFilmstripMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is PageViewModel page)
        {
            DragDrop.DoDragDrop(item, page, DragDropEffects.Move);
        }
    }

    private void OnFilmstripDrop(object sender, DragEventArgs e)
    {
        if (Vm is not { } vm || e.Data.GetData(typeof(PageViewModel)) is not PageViewModel dragged)
        {
            return;
        }
        var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        int index = targetItem?.DataContext is PageViewModel tp ? vm.Pages.IndexOf(tp) : vm.Pages.Count - 1;
        vm.MovePage(dragged, index);
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T)
        {
            d = VisualTreeHelper.GetParent(d);
        }
        return d as T;
    }
}
