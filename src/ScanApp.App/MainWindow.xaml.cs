using System.ComponentModel;
using System.Windows;
using ScanApp.App.ViewModels;

namespace ScanApp.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.SaveSession(); // auto-restore next launch
        _viewModel.Dispose();
        base.OnClosing(e);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            _viewModel.ImportPaths(files);
        }
    }
}
