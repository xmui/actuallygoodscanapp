using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScanApp.App.Infrastructure;
using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.App.ViewModels;

/// <summary>
/// A single page in the session: preview/thumbnail plus reversible per-page edits. Adjustments
/// (dust removal, auto-levels, brightness/contrast) are recomputed from a retained pristine original
/// so they can always be undone; the crop is stored as a normalized rectangle and applied only at
/// export (non-destructive).
/// </summary>
public sealed class PageViewModel : ObservableObject, IDisposable
{
    private readonly Action<PageViewModel> _onDelete;
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly Image<Rgba32> _original; // pristine copy; edits recompute from this

    private BitmapSource _thumbnail;
    private BitmapSource _preview;
    private int _number;

    private NormalizedRect _cropRect = NormalizedRect.Full;
    private bool _isCropActive;
    private int _brightness;
    private int _contrast;
    private bool _removeDust;
    private bool _autoLevel;
    private bool _recomputing;
    private bool _recomputePending;

    public PageViewModel(ScannedPage page, Action<PageViewModel> onDelete)
    {
        Page = page;
        _onDelete = onDelete;
        _original = page.Image.Clone();
        _thumbnail = WpfImage.ToThumbnail(page.Image);
        _preview = WpfImage.ToBitmapSource(page.Image);

        RotateLeftCommand = new RelayCommand(() => Rotate(-1));
        RotateRightCommand = new RelayCommand(() => Rotate(1));
        DeleteCommand = new RelayCommand(() => _onDelete(this));
        AutoCropBoxCommand = new RelayCommand(SeedCropFromContent);
        ClearCropCommand = new RelayCommand(ClearCrop);
        ResetEditsCommand = new RelayCommand(ResetEdits);
    }

    public ScannedPage Page { get; }

    public double ImagePixelWidth => _original.Width;
    public double ImagePixelHeight => _original.Height;

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

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    // ---- Crop (non-destructive) ----
    public NormalizedRect CropRect
    {
        get => _cropRect;
        set => SetProperty(ref _cropRect, value);
    }

    public bool IsCropActive
    {
        get => _isCropActive;
        set => SetProperty(ref _isCropActive, value);
    }

    /// <summary>The crop to apply at export (Full when cropping is off).</summary>
    public NormalizedRect EffectiveCrop => _isCropActive ? _cropRect : NormalizedRect.Full;

    // ---- Tonal edits (reversible) ----
    public int Brightness
    {
        get => _brightness;
        set { if (SetProperty(ref _brightness, value)) RecomputeEdits(); }
    }

    public int Contrast
    {
        get => _contrast;
        set { if (SetProperty(ref _contrast, value)) RecomputeEdits(); }
    }

    public bool RemoveDust
    {
        get => _removeDust;
        set { if (SetProperty(ref _removeDust, value)) RecomputeEdits(); }
    }

    public bool AutoLevel
    {
        get => _autoLevel;
        set { if (SetProperty(ref _autoLevel, value)) RecomputeEdits(); }
    }

    public ICommand RotateLeftCommand { get; }
    public ICommand RotateRightCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand AutoCropBoxCommand { get; }
    public ICommand ClearCropCommand { get; }
    public ICommand ResetEditsCommand { get; }

    /// <summary>Builds the final image for export: current edits with the crop applied.</summary>
    public Image<Rgba32> RenderForExport() => PageProcessor.CropNormalized(Page.Image, EffectiveCrop);

    private void Rotate(int quarterTurns)
    {
        int turns = (((quarterTurns % 4) + 4) % 4);
        if (turns == 0)
        {
            return;
        }
        // Rotate both the working page and the pristine original so edits stay consistent.
        PageProcessor.Rotate90(Page, turns);
        _original.Mutate(c => c.Rotate(90f * turns));
        OnPropertyChanged(nameof(ImagePixelWidth));
        OnPropertyChanged(nameof(ImagePixelHeight));
        RefreshFromPage();
    }

    private void SeedCropFromContent()
    {
        var rect = AutoCropProcessor.DetectContentRect(Page.Image);
        CropRect = rect is { } r
            ? NormalizedRect.FromPixels(r, Page.Width, Page.Height)
            : NormalizedRect.Full;
        IsCropActive = true;
    }

    private void ClearCrop()
    {
        CropRect = NormalizedRect.Full;
        IsCropActive = false;
    }

    private void ResetEdits()
    {
        _brightness = 0; _contrast = 0; _removeDust = false; _autoLevel = false;
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(Contrast));
        OnPropertyChanged(nameof(RemoveDust));
        OnPropertyChanged(nameof(AutoLevel));
        ClearCrop();
        RecomputeEdits();
    }

    /// <summary>Rebuilds the working image from the pristine original applying current edits (off-thread).</summary>
    private void RecomputeEdits()
    {
        if (_recomputing)
        {
            _recomputePending = true;
            return;
        }
        _recomputing = true;

        int brightness = _brightness, contrast = _contrast;
        bool dust = _removeDust, auto = _autoLevel;

        Task.Run(() =>
        {
            var img = _original.Clone();
            if (dust) DustRemovalProcessor.Remove(img, 60);
            if (auto) Adjustments.AutoLevels(img);
            Adjustments.Apply(img, brightness, contrast);

            var thumb = WpfImage.ToThumbnail(img);
            var preview = WpfImage.ToBitmapSource(img);

            _dispatcher.Invoke(() =>
            {
                Page.ReplaceImage(img);
                Thumbnail = thumb;
                Preview = preview;
                _recomputing = false;
                if (_recomputePending)
                {
                    _recomputePending = false;
                    RecomputeEdits();
                }
            });
        });
    }

    private void RefreshFromPage()
    {
        Thumbnail = WpfImage.ToThumbnail(Page.Image);
        Preview = WpfImage.ToBitmapSource(Page.Image);
    }

    public void Dispose()
    {
        Page.Dispose();
        _original.Dispose();
    }
}
