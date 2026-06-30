using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScanApp.App.Infrastructure;
using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;

namespace ScanApp.App.ViewModels;

/// <summary>A single scanned page in the session: preview/thumbnail plus per-page edit commands.</summary>
public sealed class PageViewModel : ObservableObject, IDisposable
{
    private readonly Action<PageViewModel> _onDelete;
    private BitmapSource _thumbnail;
    private BitmapSource _preview;
    private int _number;

    public PageViewModel(ScannedPage page, Action<PageViewModel> onDelete)
    {
        Page = page;
        _onDelete = onDelete;
        _thumbnail = WpfImage.ToThumbnail(page.Image);
        _preview = WpfImage.ToBitmapSource(page.Image);

        RotateLeftCommand = new RelayCommand(() => Rotate(-1));
        RotateRightCommand = new RelayCommand(() => Rotate(1));
        DeleteCommand = new RelayCommand(() => _onDelete(this));
    }

    public ScannedPage Page { get; }

    public BitmapSource Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public BitmapSource Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    /// <summary>1-based page number shown in the filmstrip.</summary>
    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public ICommand RotateLeftCommand { get; }
    public ICommand RotateRightCommand { get; }
    public ICommand DeleteCommand { get; }

    private void Rotate(int quarterTurns)
    {
        PageProcessor.Rotate90(Page, quarterTurns);
        RefreshImages();
    }

    /// <summary>Applies a manual crop (page-pixel coordinates) and refreshes the preview.</summary>
    public void ApplyManualCrop(Rectangle rect)
    {
        PageProcessor.ApplyManualCrop(Page, rect);
        RefreshImages();
    }

    private void RefreshImages()
    {
        Thumbnail = WpfImage.ToThumbnail(Page.Image);
        Preview = WpfImage.ToBitmapSource(Page.Image);
    }

    public void Dispose() => Page.Dispose();
}
